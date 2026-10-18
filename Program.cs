using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NBoardLocalGameServer
{
    internal class Program
    {
        /// <summary>終了コード. 0: 完了, 1: 設定/引数エラーまたは開始失敗, 2: 中断.</summary>
        const int ExitOk = 0, ExitError = 1, ExitCancelled = 2;

        // オプション未指定を表す番号. 明示指定された値は必ず正の整数として検証されるため衝突しない.
        const int Unset = -1;

        static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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

            var configOption = new Option<string>("--config")
            {
                Description = "Path to the match configuration JSON file. Every setting below can also be written in this file " +
                    "(Matches, Sessions, TimeControl, TimeControlFirst, TimeControlSecond, First, Second, Stats, Record); " +
                    "a command-line option overrides the file's value.",
                Required = true
            };
            configOption.Validators.Add(CheckFileExistance);

            var firstPlayerOption = new Option<string>("--first") { Description = "Path to the first player's configuration JSON file.", DefaultValueFactory = _ => string.Empty };
            firstPlayerOption.Validators.Add(CheckFileExistance);

            var secondPlayerOption = new Option<string>("--second") { Description = "Path to the second player's configuration JSON file.", DefaultValueFactory = _ => string.Empty };
            secondPlayerOption.Validators.Add(CheckFileExistance);

            var numMatchesOption = new Option<int>("--matches", "-nm")
            {
                Description = "The number of matches (Normal mode: 1 match = 1 game; Synchro mode: 1 match = 2 games).",
                DefaultValueFactory = _ => Unset
            };
            numMatchesOption.Validators.Add(CheckPositiveOrUnset("matches"));

            var numSessionsOption = new Option<int>("--sessions", "-ns")
            {
                Description = "The number of simultaneous games. Defaults to the number of processors.",
                DefaultValueFactory = _ => Unset
            };
            numSessionsOption.Validators.Add(CheckPositiveOrUnset("sessions"));

            var gameRecordOption = new Option<string>("--record", "-r")
            {
                Description = "Path to the game record GGF file.",
                DefaultValueFactory = _ => string.Empty
            };

            var playerStatsOption = new Option<string>("--stats", "-s")
            {
                Description = "Path to the game stats JSON file.",
                DefaultValueFactory = _ => string.Empty
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
                DefaultValueFactory = _ => string.Empty
            };
            timeOption.Validators.Add(CheckTimeControlFormat);

            var timeFirstOption = new Option<string>("--time-first", "-tf")
            {
                Description = $"Time control for the first player only, overriding --time. {TimeControlFormatDescription}",
                DefaultValueFactory = _ => string.Empty
            };
            timeFirstOption.Validators.Add(CheckTimeControlFormat);

            var timeSecondOption = new Option<string>("--time-second", "-ts")
            {
                Description = $"Time control for the second player only, overriding --time. {TimeControlFormatDescription}",
                DefaultValueFactory = _ => string.Empty
            };
            timeSecondOption.Validators.Add(CheckTimeControlFormat);

            var outputFormatOption = new Option<string>("--output-format", "-o")
            {
                Description = "\"text\" (default): human-readable progress and a final summary on stdout. " +
                    "\"json\": progress goes to stderr and stdout carries exactly one JSON object with the final result " +
                    "(also on errors), suitable for scripts and LLM tool use.",
                DefaultValueFactory = _ => "text"
            };
            outputFormatOption.AcceptOnlyFromAmong("text", "json");

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
            rootCmd.Options.Add(outputFormatOption);

            rootCmd.SetAction(async parseResult =>
            {
                var json = parseResult.GetValue(outputFormatOption) == "json";
                var stdout = Console.Out;

                // jsonモードではstdoutを最終結果のJSON専用にするため, 進行ログはstderrへ流す.
                if (json)
                    Console.SetOut(Console.Error);

                var configPath = parseResult.GetValue(configOption)!;
                var serverConfig = TryLoadJson(configPath, GameServerConfig.Load);
                if (serverConfig is null)
                    return Fail(json, stdout, $"Failed to load the config file \"{configPath}\".");

                // コマンドライン引数 > 設定ファイル > 既定値の順で決定する.
                var firstPath = parseResult.GetValue(firstPlayerOption)!;
                var secondPath = parseResult.GetValue(secondPlayerOption)!;
                PlayerConfig? firstPlayer, secondPlayer;
                try
                {
                    firstPlayer = string.IsNullOrEmpty(firstPath) ? serverConfig.First?.Resolve() : PlayerConfig.Load(firstPath);
                    secondPlayer = string.IsNullOrEmpty(secondPath) ? serverConfig.Second?.Resolve() : PlayerConfig.Load(secondPath);
                }
                catch (Exception ex) when (ex is JsonException || ex is NotSupportedException || ex is IOException)
                {
                    return Fail(json, stdout, $"Failed to load a player config.\nDetail: {ex.Message}");
                }

                if (firstPlayer is null)
                    return Fail(json, stdout, "The first player is not specified (use --first or \"First\" in the config).");
                if (secondPlayer is null)
                    return Fail(json, stdout, "The second player is not specified (use --second or \"Second\" in the config).");

                var numMatches = Pick(parseResult.GetValue(numMatchesOption), serverConfig.Matches);
                if (numMatches is null or <= 0)
                    return Fail(json, stdout, "The number of matches is not specified (use --matches or \"Matches\" in the config).");

                var numSessions = Pick(parseResult.GetValue(numSessionsOption), serverConfig.Sessions) ?? Environment.ProcessorCount;
                if (numSessions <= 0)
                    return Fail(json, stdout, "The number of sessions must be positive.");

                var statsPath = Pick(parseResult.GetValue(playerStatsOption), serverConfig.Stats);
                if (string.IsNullOrEmpty(statsPath))
                    return Fail(json, stdout, "The stats output path is not specified (use --stats or \"Stats\" in the config).");

                var gameRecordPath = Pick(parseResult.GetValue(gameRecordOption), serverConfig.Record) ?? string.Empty;

                if (!TryResolveClocks(
                        Pick(parseResult.GetValue(timeOption), serverConfig.TimeControl),
                        Pick(parseResult.GetValue(timeFirstOption), serverConfig.TimeControlFirst),
                        Pick(parseResult.GetValue(timeSecondOption), serverConfig.TimeControlSecond),
                        out var firstClock, out var secondClock, out var clockError))
                    return Fail(json, stdout, clockError!);

                var server = new GameServer(serverConfig, firstPlayer, secondPlayer, gameRecordPath, statsPath, numSessions, firstClock, secondClock);

                // Ctrl+Cでは, 実行中の対局を打ち切って, そこまでの統計を保存してから終了する.
                ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; server.RequestStop(); };
                Console.CancelKeyPress += onCancel;

                var startedAt = DateTime.Now;
                try
                {
                    await server.RunAsync(numMatches.Value);
                }
                finally
                {
                    Console.CancelKeyPress -= onCancel;
                }
                var finishedAt = DateTime.Now;

                var status = server.FailedToStart ? "FailedToStart" : server.WasCancelled ? "Cancelled" : "Completed";
                var result = new RunResult(status, null, server.CompletedGameCount, server.TotalGameCount,
                    server.CurrentPlayerStats, server.CurrentMatchStats, statsPath, gameRecordPath, startedAt, finishedAt);

                if (json)
                    stdout.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
                else
                    PrintSummary(result);

                return status switch
                {
                    "Completed" => ExitOk,
                    "Cancelled" => ExitCancelled,
                    _ => ExitError
                };
            });

            return rootCmd;
        }

        /// <summary>1回の実行結果. --output-format jsonではこれがそのままstdoutに出力される.</summary>
        record RunResult(
            string Status, string? Message, int CompletedGames, int TotalGames,
            IReadOnlyList<PlayerStats>? Players, MatchStats? Match,
            string StatsPath, string RecordPath, DateTime StartedAt, DateTime FinishedAt);

        static int Fail(bool json, System.IO.TextWriter stdout, string message)
        {
            Console.Error.WriteLine(message);
            if (json)
            {
                var result = new RunResult("Error", message, 0, 0, null, null, string.Empty, string.Empty, DateTime.Now, DateTime.Now);
                stdout.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            }
            return ExitError;
        }

        static void PrintSummary(RunResult result)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {result.Status}: {result.CompletedGames}/{result.TotalGames} games ===");

            if (result.Players is null)
                return;

            foreach (var p in result.Players)
            {
                var elo = p.EloDiff is { } e ? $"{e:+0.0;-0.0}" + (p.EloDiffMargin95 is { } m ? $" (+/-{m:0.0})" : string.Empty) : "n/a";
                var conf = p.ConfidenceLevel is { } c ? $"{c * 100:0.0}%" : "n/a";
                Console.WriteLine($"{p.Label}: {p.TotalWinCount}W {p.TotalDrawCount}D {p.TotalLossCount}L, " +
                    $"score rate {p.TotalWinRate * 100:0.0}%, Elo diff {elo}, confidence {conf}");
            }

            if (result.Match is { } match)
            {
                var elo = match.EloDiffForPlayer0 is { } e ? $"{e:+0.0;-0.0}" + (match.EloDiffMargin95ForPlayer0 is { } m ? $" (+/-{m:0.0})" : string.Empty) : "n/a";
                var conf = match.ConfidenceLevelForPlayer0 is { } c ? $"{c * 100:0.0}%" : "n/a";
                Console.WriteLine($"Matches ({match.Label0} vs {match.Label1}): {match.MatchWinCount[0]}-{match.MatchDrawCount}-{match.MatchWinCount[1]}, " +
                    $"Elo diff {elo}, confidence {conf}");
            }
        }

        /// <summary>CLI引数(空文字/未指定を意味する値)が指定されていればそれを, なければ設定ファイルの値を返す.</summary>
        static string? Pick(string? cliValue, string? configValue) => string.IsNullOrEmpty(cliValue) ? configValue : cliValue;

        static int? Pick(int cliValue, int? configValue) => cliValue == Unset ? configValue : cliValue;

        static bool TryResolveClocks(string? common, string? first, string? second,
            out GameClockConfig? firstClock, out GameClockConfig? secondClock, out string? error)
        {
            firstClock = secondClock = null;
            error = null;

            GameClockConfig? commonClock = null;
            if (!string.IsNullOrEmpty(common) && !GameClockConfig.TryParseTime(common, out commonClock))
            {
                error = $"Invalid time control format: \"{common}\".";
                return false;
            }

            firstClock = commonClock;
            if (!string.IsNullOrEmpty(first) && !GameClockConfig.TryParseTime(first, out firstClock))
            {
                error = $"Invalid time control format: \"{first}\".";
                return false;
            }

            secondClock = commonClock;
            if (!string.IsNullOrEmpty(second) && !GameClockConfig.TryParseTime(second, out secondClock))
            {
                error = $"Invalid time control format: \"{second}\".";
                return false;
            }

            return true;
        }

        static Action<OptionResult> CheckPositiveOrUnset(string name) => result =>
        {
            var value = result.GetValueOrDefault<int>();
            if (value != Unset && value <= 0)
                result.AddError($"The number of {name} must be positive.");
        };

        static void CheckFileExistance(OptionResult result)
        {
            var path = result.GetValueOrDefault<string>();

            // 任意のオプションでは, 未指定(空文字)は有効.
            if (string.IsNullOrEmpty(path))
            {
                if (result.Option.Required)
                    result.AddError("Invalid path.");
                return;
            }

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
