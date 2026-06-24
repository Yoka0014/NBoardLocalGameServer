using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

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

            var numGamesOption = new Option<int>("--games", "-ng") { Description = "The number of games.", Required = true };
            numGamesOption.Validators.Add(result =>
            {
                if (result.GetValueOrDefault<int>() <= 0)
                    result.AddError("The number of games must be positive.");
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

            rootCmd.Options.Add(configOption);
            rootCmd.Options.Add(firstPlayerOption);
            rootCmd.Options.Add(secondPlayerOption);
            rootCmd.Options.Add(numGamesOption);
            rootCmd.Options.Add(numSessionsOption);
            rootCmd.Options.Add(gameRecordOption);
            rootCmd.Options.Add(playerStatsOption);

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

                var numGames = parseResult.GetValue(numGamesOption);
                var numSessions = parseResult.GetValue(numSessionsOption);

                var gameRecordPath = parseResult.GetValue(gameRecordOption);
                var playerStatsPath = parseResult.GetValue(playerStatsOption);

                var server = new GameServer(serverConfig, firstPlayerConfig, secondPlayerConfig, gameRecordPath!, playerStatsPath!, numSessions);
                await server.RunAsync(numGames);
            });

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
