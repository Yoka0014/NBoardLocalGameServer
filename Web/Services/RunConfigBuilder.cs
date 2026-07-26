using System;
using System.Collections.Generic;
using System.IO;

using NBoardLocalGameServer.Web.Models;
using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Services
{
    /// <summary>The real match-orchestration inputs resolved from a QueueEntry, ready to hand to GameServer.</summary>
    internal record ResolvedRun(
        GameServerConfig ServerConfig, PlayerConfig Player0, PlayerConfig Player1,
        GameClockConfig? Clock0, GameClockConfig? Clock1,
        string Engine0Name, string Engine1Name, string PresetName);

    /// <summary>
    /// Translates stored engine/book/preset records into the real GameServerConfig/PlayerConfig/
    /// GameClockConfig objects GameServer expects — the glue that lets the web layer reuse the
    /// existing match-orchestration code unmodified.
    /// </summary>
    internal class RunConfigBuilder(EngineStore engineStore, BookStore bookStore, PresetStore presetStore)
    {
        public ResolvedRun Build(QueueEntry entry)
        {
            var engine0 = engineStore.Load(entry.Engine0Id) ?? throw new InvalidOperationException($"Engine \"{entry.Engine0Id}\" was not found.");
            var engine1 = engineStore.Load(entry.Engine1Id) ?? throw new InvalidOperationException($"Engine \"{entry.Engine1Id}\" was not found.");
            var preset = presetStore.Load(entry.PresetId) ?? throw new InvalidOperationException($"Preset \"{entry.PresetId}\" was not found.");

            var player0 = BuildPlayerConfig(engine0, entry.Engine0ArgumentsOverride, entry.Engine0InitialCommandsOverride);
            var player1 = BuildPlayerConfig(engine1, entry.Engine1ArgumentsOverride, entry.Engine1InitialCommandsOverride);

            var serverConfig = new GameServerConfig
            {
                SessionMode = preset.SessionMode,
                MatchMode = preset.MatchMode,
                SwapPlayer = preset.SwapPlayer,
                UseSamePositionWhenSwapPlayer = preset.UseSamePositionWhenSwapPlayer,
                ShuffleBook = preset.ShuffleBook,
                OpeningBookPath = preset.BookId is not null ? bookStore.GetFilePath(preset.BookId) : string.Empty
            };

            var (clock0, clock1) = ResolveClocks(preset);

            return new ResolvedRun(serverConfig, player0, player1, clock0, clock1, engine0.Name, engine1.Name, preset.Name);
        }

        // EngineProcess.Start only Path.GetFullPath()s against the server's own CWD — it knows nothing
        // about an engine's extraction root — so the extraction root must be pre-combined here.
        PlayerConfig BuildPlayerConfig(EngineRecord engine, string? argumentsOverride, List<string>? initialCommandsOverride)
        {
            var root = engineStore.GetExtractedRoot(engine.Id);
            var path = Path.Combine(root, engine.DefaultPath);
            var workDir = Path.Combine(root, engine.DefaultWorkDir);
            var arguments = argumentsOverride ?? engine.DefaultArguments;
            var initialCommands = initialCommandsOverride ?? engine.DefaultInitialCommands;
            return new PlayerConfig(path, arguments, workDir, initialCommands);
        }

        static (GameClockConfig? Clock0, GameClockConfig? Clock1) ResolveClocks(MatchPresetRecord preset)
        {
            GameClockConfig? common = null;
            if (!string.IsNullOrEmpty(preset.TimeControlCommon))
                GameClockConfig.TryParseTime(preset.TimeControlCommon, out common);

            var clock0 = common;
            if (!string.IsNullOrEmpty(preset.TimeControlFirst) && GameClockConfig.TryParseTime(preset.TimeControlFirst, out var c0))
                clock0 = c0;

            var clock1 = common;
            if (!string.IsNullOrEmpty(preset.TimeControlSecond) && GameClockConfig.TryParseTime(preset.TimeControlSecond, out var c1))
                clock1 = c1;

            return (clock0, clock1);
        }
    }
}
