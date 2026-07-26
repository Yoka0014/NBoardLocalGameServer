using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Services
{
    /// <summary>
    /// After each match, mirrors the completed match's artifacts (plus a manifest of every match) into
    /// data/history-export/ and optionally pushes that folder to a static host via a configurable shell
    /// command. The exported folder is self-contained (viewer page + manifest + per-match stats/GGF) so
    /// syncing it wholesale to any static file host (S3, Oracle Object Storage, etc.) is enough to serve
    /// a read-only results viewer that needs no live backend — see StaticExport/index.html.
    /// </summary>
    internal class HistoryExportService(PathConventions paths, HistoryStore historyStore, SettingsStore settingsStore, ILogger<HistoryExportService> logger)
    {
        public async Task ExportAndSyncAsync(string matchId)
        {
            try
            {
                Directory.CreateDirectory(paths.HistoryExportDir);
                CopyViewerPage();
                WriteManifest();
                CopyMatchArtifacts(matchId);
                await RunSyncCommandAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to export/sync history for match {MatchId}.", matchId);
            }
        }

        void CopyViewerPage()
        {
            var staticExportSrcDir = Path.Combine(AppContext.BaseDirectory, "Web", "StaticExport");

            var indexSrc = Path.Combine(staticExportSrcDir, "index.html");
            if (File.Exists(indexSrc))
                File.Copy(indexSrc, Path.Combine(paths.HistoryExportDir, "index.html"), overwrite: true);

            // config.js holds the user's own Lambda Function URL/token (see config.example.js) -- copied
            // once from the example and never overwritten again, so edits survive future syncs.
            var configDest = Path.Combine(paths.HistoryExportDir, "config.js");
            var configExampleSrc = Path.Combine(staticExportSrcDir, "config.example.js");
            if (!File.Exists(configDest) && File.Exists(configExampleSrc))
                File.Copy(configExampleSrc, configDest);
        }

        void WriteManifest()
        {
            var all = historyStore.ListAll();
            var manifestPath = Path.Combine(paths.HistoryExportDir, "history-index.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(all, JsonConventions.Options));
        }

        void CopyMatchArtifacts(string matchId)
        {
            var destDir = Path.Combine(paths.HistoryExportDir, matchId);
            Directory.CreateDirectory(destDir);

            var statsSrc = paths.HistoryStatsPath(matchId);
            if (File.Exists(statsSrc))
                File.Copy(statsSrc, Path.Combine(destDir, "stats.json"), overwrite: true);

            var recordSrc = paths.HistoryRecordPath(matchId);
            if (File.Exists(recordSrc))
                File.Copy(recordSrc, Path.Combine(destDir, "record.ggf"), overwrite: true);
        }

        async Task RunSyncCommandAsync()
        {
            var command = settingsStore.Load().HistorySyncCommand;
            if (string.IsNullOrWhiteSpace(command))
                return;

            var resolved = command.Replace("{dir}", paths.HistoryExportDir);

            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                Arguments = OperatingSystem.IsWindows()
                    ? $"/c chcp 65001>nul && {resolved}"
                    : $"-c \"{resolved.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                logger.LogError("Failed to start history sync command process.");
                return;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
                logger.LogError("History sync command exited with code {Code}. stdout: {Stdout} stderr: {Stderr}", process.ExitCode, stdout, stderr);
            else
                logger.LogInformation("History sync command completed successfully.");
        }
    }
}
