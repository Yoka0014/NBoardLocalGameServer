using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using NBoardLocalGameServer.Web.Models;
using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Services
{
    /// <summary>
    /// Runs exactly one queued match at a time, reusing the existing, unmodified GameServer for
    /// orchestration. Registered as both a hosted service and an injectable singleton so endpoints
    /// can read CurrentGameServer/CurrentMatchId directly for queue status and spectating.
    /// </summary>
    internal class QueueRunner(
        QueueStore queueStore, HistoryStore historyStore, SettingsStore settingsStore,
        RunConfigBuilder runConfigBuilder, Ec2SelfStopService ec2SelfStop, HistoryExportService historyExport,
        ILogger<QueueRunner> logger) : BackgroundService
    {
        volatile GameServer? _currentServer;
        volatile string? _currentMatchId;
        volatile QueueEntry? _currentEntry;
        volatile ResolvedRun? _currentRun;
        DateTime _currentStartedAt;
        DateTime? _queueEmptySince;
        bool _autoStopRequested;

        public GameServer? CurrentGameServer => _currentServer;
        public string? CurrentMatchId => _currentMatchId;

        /// <summary>
        /// The registered (server-side) engine names for the currently running match, e.g. "Edax 4.6" —
        /// distinct from the engine's own self-reported NBoard name, which can collide across different
        /// registrations of the same underlying engine binary. Null while nothing is running.
        /// </summary>
        public (string Engine0Name, string Engine1Name)? CurrentEngineNames
        {
            get
            {
                var run = _currentRun;
                return run is null ? null : (run.Engine0Name, run.Engine1Name);
            }
        }

        /// <summary>
        /// Requests that the currently running match stop. Games still waiting for an engine to free up
        /// are cancelled immediately and discarded; any game whose engine is mid-think finishes that one
        /// move first, then also stops and is discarded (not saved to stats/record). Already-flushed
        /// completed games (every 100 games, per GameServer's own chunking) are unaffected.
        /// </summary>
        public bool CancelRunning()
        {
            var server = _currentServer;
            if (server is null)
                return false;

            server.RequestStop();
            return true;
        }

        /// <summary>Null-safe snapshot of the running match for the dashboard/queue-status endpoint.</summary>
        public RunningMatchInfo? GetRunningInfo()
        {
            var server = _currentServer;
            var entry = _currentEntry;
            var run = _currentRun;
            var matchId = _currentMatchId;
            if (server is null || entry is null || run is null || matchId is null)
                return null;

            return new RunningMatchInfo(matchId, run.Engine0Name, run.Engine1Name, run.PresetName,
                entry.Matches, entry.Sessions, server.CompletedGameCount, server.TotalGameCount, _currentStartedAt);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var entry = queueStore.DequeueNext();
                if (entry is null)
                {
                    var settings = settingsStore.Load();
                    if (settings.AutoStopWhenQueueEmpty)
                    {
                        _queueEmptySince ??= DateTime.Now;

                        var idleFor = DateTime.Now - _queueEmptySince.Value;
                        if (!_autoStopRequested && idleFor >= TimeSpan.FromMinutes(settings.AutoStopIdleMinutes))
                        {
                            _autoStopRequested = true;
                            logger.LogInformation(
                                "Queue has been empty for {IdleMinutes} min (threshold reached) — requesting EC2 self-stop.",
                                settings.AutoStopIdleMinutes);
                            await ec2SelfStop.StopSelfAsync();
                        }
                    }
                    else
                    {
                        _queueEmptySince = null;
                        _autoStopRequested = false;
                    }

                    try { await Task.Delay(1000, stoppingToken); } catch (OperationCanceledException) { }
                    continue;
                }

                _queueEmptySince = null;
                _autoStopRequested = false;
                await RunEntryAsync(entry, stoppingToken);
            }
        }

        async Task RunEntryAsync(QueueEntry entry, CancellationToken stoppingToken)
        {
            var matchId = Guid.NewGuid().ToString("N");

            ResolvedRun run;
            try
            {
                run = runConfigBuilder.Build(entry);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to resolve queue entry {EntryId}.", entry.Id);
                historyStore.Save(new HistoryEntryRecord
                {
                    Id = matchId,
                    StartedAt = DateTime.Now,
                    FinishedAt = DateTime.Now,
                    Status = "Failed",
                    Engine0Id = entry.Engine0Id,
                    Engine0Name = entry.Engine0Id,
                    Engine1Id = entry.Engine1Id,
                    Engine1Name = entry.Engine1Id,
                    PresetId = entry.PresetId,
                    PresetName = entry.PresetId,
                    Matches = entry.Matches,
                    Sessions = entry.Sessions,
                    ErrorMessage = ex.Message,
                    Note = entry.Note
                });
                return;
            }

            _currentStartedAt = DateTime.Now;
            historyStore.Save(new HistoryEntryRecord
            {
                Id = matchId,
                StartedAt = _currentStartedAt,
                Status = "Running",
                Engine0Id = entry.Engine0Id,
                Engine0Name = run.Engine0Name,
                Engine1Id = entry.Engine1Id,
                Engine1Name = run.Engine1Name,
                PresetId = entry.PresetId,
                PresetName = run.PresetName,
                Matches = entry.Matches,
                Sessions = entry.Sessions,
                Engine0Config = new EngineLaunchConfig(run.Player0.Path, run.Player0.Arguments, run.Player0.WorkDir, [.. run.Player0.InitialCommands]),
                Engine1Config = new EngineLaunchConfig(run.Player1.Path, run.Player1.Arguments, run.Player1.WorkDir, [.. run.Player1.InitialCommands]),
                Note = entry.Note
            });

            // HistoryStore.Save above already created the history/<matchId>/ directory, so GameServer
            // can write stats.json/record.ggf there directly — no temp file/move step needed.
            var server = new GameServer(run.ServerConfig, run.Player0, run.Player1,
                historyStore.RecordPath(matchId), historyStore.StatsPath(matchId),
                entry.Sessions, run.Clock0, run.Clock1, run.Engine0Name, run.Engine1Name);

            _currentServer = server;
            _currentMatchId = matchId;
            _currentEntry = entry;
            _currentRun = run;

            string status = "Failed";
            string? error = null;
            try
            {
                await server.RunAsync(entry.Matches);
                status = server.FailedToStart ? "Failed" : server.WasCancelled ? "Cancelled" : "Completed";
                if (server.FailedToStart)
                    error = "Engine startup or opening book load failed — see the server console log.";
                else if (server.WasCancelled)
                    error = "Stopped by user request before all matches finished.";
            }
            catch (Exception ex)
            {
                status = "Failed";
                error = ex.Message;
                logger.LogError(ex, "Queue entry {EntryId} failed.", entry.Id);
            }
            finally
            {
                var previous = historyStore.Load(matchId)!;
                historyStore.Save(previous with { Status = status, FinishedAt = DateTime.Now, ErrorMessage = error });

                _currentServer = null;
                _currentMatchId = null;
                _currentEntry = null;
                _currentRun = null;

                await historyExport.ExportAndSyncAsync(matchId);
            }
        }
    }
}
