using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using NBoardLocalGameServer.Web;

namespace NBoardLocalGameServer
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            DebugOut.SetOutFile("log.txt", autoFlush: false);
            var cmd = InitCommand();
            var exitCode = await cmd.Parse(args).InvokeAsync();
            DebugOut.Close();
            return exitCode;
        }

        static RootCommand InitCommand()
        {
            var rootCmd = new RootCommand("NBoardLocalGameServer");

            var configOption = new Option<string>("--config") { Description = "Path to the server configuration JSON file.", Required = true };
            configOption.Validators.Add(CheckFileExistance);

            var firstPlayerOption = new Option<string>("--first") { Description = "Path to the first player's configuration JSON file.", Required = true };
            configOption.Validators.Add(CheckFileExistance);

            var secondPlayerOption = new Option<string>("--second") { Description = "Path to the second player's configuration JSON file.", Required = true };
            configOption.Validators.Add(CheckFileExistance);

            var numMatchesOption = new Option<int>("--matches", "-nm")
            {
                Description = "The number of matches (Normal mode: 1 match = 1 game; Synchro mode: 1 match = 2 games).",
                Required = true
            };
            numMatchesOption.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<int>() <= 0)
                    result.AddError("The number of matches must be positive.");
            });

            var numSessionsOption = new Option<int>("--sessions", "-ns")
            {
                Description = "The number of simultaneous games.",
                DefaultValueFactory = _ => Environment.ProcessorCount,
                Required = false
            };

            numSessionsOption.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<int>() <= 0)
                    result.AddError("The number of sessions must be positive.");
            });

            var gameRecordOption = new Option<string>("--record", "-r")
            {
                Description = "Path to the game record GGF file.",
                DefaultValueFactory = _ => string.Empty,
                Required = false
            };

            var playerStatsOption = new Option<string>("--stats", "-s")
            {
                Description = "Path to the game stats JSON file.",
                Required = true
            };

            const string TimeControlFormatDescription =
                "Time control in \"ini/inc/extra\" format (same as GGF's BT[]/WT[] fields), each part being " +
                "\"HH:MM:SS[,N<moves>]\". ini: initial time (N = do not lose on timeout). inc: per-move increment " +
                "(N = Bronstein-style instead of Fischer-style). extra: extra/byoyomi time granted once the " +
                "initial time runs out (N = replace instead of accumulate). All 3 parts are required. " +
                "Example: \"00:05:00/00:00:10/00:00:00\" = 5 min + 10 sec Fischer increment per move, no extra time. " +
                "Example: \"00:25:00/00:00:00/00:01:00,N1\" = 25 min, then 1 min byoyomi per move.";

            var timeOption = new Option<string>("--time", "-t")
            {
                Description = $"Time control shared by both players, unless overridden by --time-first/--time-second. {TimeControlFormatDescription}",
                DefaultValueFactory = _ => string.Empty,
                Required = false
            };
            timeOption.Validators.Add(CheckTimeControlFormat);

            var timeFirstOption = new Option<string>("--time-first", "-tf")
            {
                Description = $"Time control for the first player only, overriding --time. {TimeControlFormatDescription}",
                DefaultValueFactory = _ => string.Empty,
                Required = false
            };
            timeFirstOption.Validators.Add(CheckTimeControlFormat);

            var timeSecondOption = new Option<string>("--time-second", "-ts")
            {
                Description = $"Time control for the second player only, overriding --time. {TimeControlFormatDescription}",
                DefaultValueFactory = _ => string.Empty,
                Required = false
            };
            timeSecondOption.Validators.Add(CheckTimeControlFormat);

            rootCmd.Options.Add(configOption);
            rootCmd.Options.Add(firstPlayerOption);
            rootCmd.Options.Add(secondPlayerOption);
            rootCmd.Options.Add(numMatchesOption);
            rootCmd.Options.Add(numSessionsOption);
            rootCmd.Options.Add(gameRecordOption);
            rootCmd.Options.Add(playerStatsOption);
            rootCmd.Options.Add(timeOption);
            rootCmd.Options.Add(timeFirstOption);
            rootCmd.Options.Add(timeSecondOption);

            rootCmd.SetAction(async parseResult =>
            {
                var configPath = parseResult.GetValue(configOption)!;
                var serverConfig = TryLoadJson(configPath, GameServerConfig.Load);

                if (serverConfig is null)
                    return;

                var firstPlayerConfigPath = parseResult.GetValue(firstPlayerOption);
                var firstPlayerConfig = TryLoadJson(firstPlayerConfigPath!, PlayerConfig.Load);

                if (firstPlayerConfig is null)
                    return;

                var secondPlayerConfigPath = parseResult.GetValue(secondPlayerOption);
                var secondPlayerConfig = TryLoadJson(secondPlayerConfigPath!, PlayerConfig.Load);

                if (secondPlayerConfig is null)
                    return;

                var numMatches = parseResult.GetValue(numMatchesOption);
                var numSessions = parseResult.GetValue(numSessionsOption);

                var gameRecordPath = parseResult.GetValue(gameRecordOption);
                var playerStatsPath = parseResult.GetValue(playerStatsOption);

                var timeStr = parseResult.GetValue(timeOption);
                var timeFirstStr = parseResult.GetValue(timeFirstOption);
                var timeSecondStr = parseResult.GetValue(timeSecondOption);

                GameClockConfig? commonClock = null;
                if (!string.IsNullOrEmpty(timeStr))
                    GameClockConfig.TryParseTime(timeStr, out commonClock);

                var firstClock = commonClock;
                if (!string.IsNullOrEmpty(timeFirstStr))
                    GameClockConfig.TryParseTime(timeFirstStr, out firstClock);

                var secondClock = commonClock;
                if (!string.IsNullOrEmpty(timeSecondStr))
                    GameClockConfig.TryParseTime(timeSecondStr, out secondClock);

                var server = new GameServer(serverConfig, firstPlayerConfig, secondPlayerConfig, gameRecordPath!, playerStatsPath!, numSessions, firstClock, secondClock);
                await server.RunAsync(numMatches);
            });

            var serveCmd = new Command("serve", "Run the local web server (API + frontend) for interactive match management.");

            var portOption = new Option<int>("--port")
            {
                Description = "TCP port for the local HTTP server.",
                DefaultValueFactory = _ => 5000
            };

            var dataDirOption = new Option<string>("--data-dir")
            {
                Description = "Directory (relative to the current directory unless absolute) holding registered engines, opening books, match presets, the match queue, and match history.",
                DefaultValueFactory = _ => "data"
            };

            var bindAddressOption = new Option<string>("--bind-address")
            {
                Description = "IP address to bind the HTTP server to. Defaults to 127.0.0.1 (loopback only, reachable " +
                    "solely via SSH/SSM port forwarding). Set to a private overlay-network address (e.g. a Tailscale " +
                    "IP) to allow specific enrolled devices to connect directly instead. Avoid 0.0.0.0 unless a " +
                    "firewall/security group is independently blocking the port from the public internet.",
                DefaultValueFactory = _ => "127.0.0.1"
            };

            serveCmd.Options.Add(portOption);
            serveCmd.Options.Add(dataDirOption);
            serveCmd.Options.Add(bindAddressOption);

            serveCmd.SetAction(async parseResult =>
            {
                var port = parseResult.GetValue(portOption);
                var dataDir = parseResult.GetValue(dataDirOption)!;
                var bindAddress = parseResult.GetValue(bindAddressOption)!;
                await ServeHost.RunAsync(port, dataDir, bindAddress);
            });

            rootCmd.Subcommands.Add(serveCmd);

            return rootCmd;
        }

        static void CheckFileExistance(OptionResult result)
        {
            var path = result.GetValueOrDefault<string>();

            if (path is null)
                result.AddError("Invalid path.");

            if (!File.Exists(path))
                result.AddError($"File \"{path}\" was not found.");
        }

        static void CheckTimeControlFormat(OptionResult result)
        {
            var str = result.GetValueOrDefault<string>();

            if (string.IsNullOrEmpty(str))
                return;

            if (!GameClockConfig.TryParseTime(str, out _))
                result.AddError($"Invalid time control format: \"{str}\".");
        }

        static T? TryLoadJson<T>(string path, Func<string, T?> loader) where T : class
        {
            T? obj = null;
            try
            {
                obj = loader(path);
            }
            catch (Exception ex) when (ex is JsonException || ex is NotSupportedException)
            {
                Console.Error.WriteLine($"Failed to load JSON file from \"{path}\".\nDetail: {ex.Message}");
            }

            return obj;
        }
    }
}
