using System;
using System.IO;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using NBoardLocalGameServer.Engine;
using NBoardLocalGameServer.Reversi;

namespace NBoardLocalGameServer
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    internal enum MatchMode
    {
        // 1局ごとに勝敗を決める通常モード.
        Normal,

        // GGSのSynchro Matchに準拠したモード.
        // 同一開始局面に対し手番を入れ替えた2局を1マッチとして扱い,
        // 2局合計の石差でマッチの勝敗を決める.
        Synchro
    }

    internal class GameServerConfig
    {
        static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        public GameSessionMode SessionMode { get; init; } = GameSessionMode.StatefulEngine;

        /// <summary>
        /// どの対局ルールで進行するか.
        /// </summary>
        public MatchMode MatchMode { get; init; } = MatchMode.Normal;

        /// <summary>
        /// 1ゲームごとに手番を入れ替えるか.
        /// MatchModeがSynchroの場合は常に手番を入れ替えるため, この設定値は無視される.
        /// </summary>
        public bool SwapPlayer { get; init; } = true;

        /// <summary>
        /// 手番を入れ替えたとき, 手番入れ替える前と同じ局面で再対局するか, もしくは別の局面を用意するか.
        /// SwapPlayerがtrueのときのみ有効.
        /// MatchModeがSynchroの場合は常に同じ局面で再対局するため, この設定値は無視される.
        /// </summary>
        public bool UseSamePositionWhenSwapPlayer { get; init; } = true;

        /// <summary>
        /// 開始局面集のパス
        /// </summary>
        public string OpeningBookPath { get; set; } = string.Empty;

        /// <summary>
        /// 開始局面集をシャッフルするか
        /// </summary>
        public bool ShuffleBook { get; set; } = false;

        public static GameServerConfig? Load(string path) => JsonSerializer.Deserialize<GameServerConfig>(File.ReadAllText(path));
        public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));
    }

    internal class GameServer(GameServerConfig config, PlayerConfig playerConfig0, PlayerConfig playerConfig1, string gameRecordPath, string playerStatsPath, int maxSessions, GameClockConfig? player0Clock = null, GameClockConfig? player1Clock = null)
    {
        readonly GameServerConfig _config = config;
        readonly PlayerConfig[] _playerConfigs = [playerConfig0, playerConfig1];
        readonly GameClockConfig?[] _clockConfigs = [player0Clock, player1Clock];
        readonly string _gameRecordPath = gameRecordPath;
        readonly string _playerStatsPath = playerStatsPath;
        readonly int _maxSessions = maxSessions;
        readonly ConcurrentDictionary<int, GameSession> _activeSessions = new();
        readonly ConcurrentDictionary<int, bool> _blackIsPlayerZero = new();
        int _completedGameCount;
        CancellationTokenSource? _cts;

        /// <summary>Total games this run will play (numMatches * (Synchro ? 2 : 1)). 0 until MainloopAsync starts.</summary>
        public int TotalGameCount { get; private set; }

        /// <summary>
        /// The engine-pool size (--sessions). At most this many games can be in flight at once, which
        /// callers can use to derive a stable "slot" identity for a game (gameID % MaxSessions is unique
        /// among concurrently active games, since more than MaxSessions of them can never overlap).
        /// </summary>
        public int MaxSessions => _maxSessions;

        /// <summary>
        /// True once RequestStop() has actually taken effect (MainloopAsync observed the cancellation).
        /// Distinguishes a deliberately stopped run from one that finished all its games normally —
        /// RunAsync returns without throwing in both cases, so callers need this to tell them apart.
        /// </summary>
        public bool WasCancelled { get; private set; }

        /// <summary>Games whose StartSession has returned (success, cancel, or engine error) — live-readable while RunAsync runs.</summary>
        public int CompletedGameCount => _completedGameCount;

        /// <summary>
        /// True if CreatePlayersAsync or opening-book loading failed and RunAsync returned early without
        /// playing anything. RunAsync otherwise returns normally in this case (just logs to the console),
        /// so external callers (e.g. a web queue runner) need this to distinguish that from "played 0
        /// games because numMatches was 0".
        /// </summary>
        public bool FailedToStart { get; private set; }

        /// <summary>
        /// Currently in-flight sessions, keyed by the MainloopAsync loop's gameID. Safe to enumerate/poll
        /// from another thread — GameSession.CurrentGameInfo already returns an independent deep copy
        /// per call.
        /// </summary>
        public IReadOnlyDictionary<int, GameSession> ActiveSessions => _activeSessions;

        /// <summary>
        /// For each active gameID, whether players[0] is playing Black in that game (false means
        /// players[1] is Black). Lets callers report which *registered* engine (players[0]/[1]) is on
        /// which side without relying on the engine's own self-reported NBoard name, which can collide
        /// across different registrations of the same underlying engine binary.
        /// </summary>
        public IReadOnlyDictionary<int, bool> BlackIsPlayerZero => _blackIsPlayerZero;

        public async Task RunAsync(int numMatches)
        {
            Console.WriteLine($"The number of sessions: {_maxSessions}");

            Player[]? players = null;
            OpeningBook? book;
            try
            {
                players = await CreatePlayersAsync();

                if (players is null)
                {
                    FailedToStart = true;
                    return;
                }

                book = LoadOpeningBook(_config.OpeningBookPath);

                if (book is null)
                {
                    FailedToStart = true;
                    return;
                }

                if (_config.ShuffleBook)
                {
                    Console.WriteLine("Shuffle opening book");
                    book.Shuffle();
                    Console.WriteLine("done");
                }

                await MainloopAsync(numMatches, players, book);

            }
            finally
            {
                foreach (var p in players ?? [])
                    p.Dispose();
            }
        }

        public void RequestStop() => _cts?.Cancel();

        async Task MainloopAsync(int numMatches, Player[] players, OpeningBook book)
        {
            const int SaveChunk = 100;

            var isSynchro = _config.MatchMode == MatchMode.Synchro;
            var numGames = numMatches * (isSynchro ? 2 : 1);
            TotalGameCount = numGames;
            var matchStats = isSynchro ? new MatchStats(players[0].Name, players[1].Name) : null;

            _cts = new CancellationTokenSource();
            var games = new List<Task<GameInfo?>>();
            Position? pos = null;
            using var gameRecordsSw = string.IsNullOrEmpty(_gameRecordPath) ? StreamWriter.Null : new StreamWriter(_gameRecordPath, File.Exists(_gameRecordPath));

            try
            {
                int firstIdx = 0, secondIdx = 1;
                for (var i = 0; i < numGames; i++)
                {
                    if (games.Count == SaveChunk)
                    {
                        var results = await Task.WhenAll(games);
                        SaveGameRecords(gameRecordsSw, results);
                        if (matchStats is not null)
                            UpdateMatchStats(matchStats, results);
                        SaveStats(players, matchStats);
                        games.Clear();
                    }

                    // Synchroモードでは, SwapPlayer/UseSamePositionWhenSwapPlayerの値に関わらず
                    // 常に「同一局面・手番反転ペア」で対局させる.
                    var forcePairing = isSynchro || (_config.SwapPlayer && _config.UseSamePositionWhenSwapPlayer);
                    if (i % 2 == 0 || !forcePairing)
                        pos = book.NumPositions != 0 ? book.GetPosition() : new Position();

                    games.Add(StartSession(i, new Position(pos!), players, players[firstIdx], players[secondIdx], _cts.Token));

                    if (isSynchro || _config.SwapPlayer)
                        (firstIdx, secondIdx) = (secondIdx, firstIdx);
                }

                var finalResults = await Task.WhenAll(games);
                SaveGameRecords(gameRecordsSw, finalResults);
                if (matchStats is not null)
                    UpdateMatchStats(matchStats, finalResults);
                SaveStats(players, matchStats);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                Console.WriteLine("Info: Game sessions were canceled by user interruption.");
            }
        }

        void SaveStats(Player[] players, MatchStats? matchStats)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            object output = matchStats is not null
                ? new SynchroStatsOutput([.. from p in players select p.Stats], matchStats)
                : (from p in players select p.Stats).ToArray();
            File.WriteAllText(_playerStatsPath, JsonSerializer.Serialize(output, options));
        }

        /// <summary>
        /// 2局を1組として, 合計石差でマッチの勝敗/引き分けを集計する.
        /// MainloopAsyncの逐次ループからのみ呼ばれる(並行呼び出しはない)ためロック不要.
        /// </summary>
        static void UpdateMatchStats(MatchStats matchStats, GameInfo?[] results)
        {
            for (var k = 0; k + 1 < results.Length; k += 2)
            {
                var gameA = results[k];     // 偶数インデックス: players[0] = Black
                var gameB = results[k + 1]; // 奇数インデックス: players[0] = White

                if (gameA?.Result is null || gameB?.Result is null)
                {
                    matchStats.IncompleteMatchCount++;
                    continue;
                }

                var net = NetMargin(gameA.Result, DiscColor.Black) + NetMargin(gameB.Result, DiscColor.White);
                if (net > 0)
                    matchStats.MatchWinCount[0]++;
                else if (net < 0)
                    matchStats.MatchWinCount[1]++;
                else
                    matchStats.MatchDrawCount++;

                matchStats.TotalNetScoreForPlayer0 += net;
            }
        }

        static int NetMargin(GameResult res, DiscColor colorPlayedByPlayer0)
            => res.Winner == DiscColor.Null ? 0 : (res.Winner == colorPlayedByPlayer0 ? res.ScoreFromWinner : -res.ScoreFromWinner);

        async Task<GameInfo?> StartSession(int gameID, Position pos, Player[] players, Player blackPlayer, Player whitePlayer, CancellationToken ct)
        {
            GameSession? session = null;
            NBoardEngine? blackEngine = null, whiteEngine = null;
            GameInfo? resultedGame = null;
            var sb = new StringBuilder();

            try
            {
                // 必ずplayers[0] -> players[1]の順で借りるようにする．
                // この制約を設けないとデッドロックが起きる．
                if (blackPlayer == players[0])
                {
                    blackEngine = await blackPlayer.EnginePool.RentAsync(ct);
                    whiteEngine = await whitePlayer.EnginePool.RentAsync(ct);
                }
                else
                {
                    whiteEngine = await whitePlayer.EnginePool.RentAsync(ct);
                    blackEngine = await blackPlayer.EnginePool.RentAsync(ct);
                }

                sb.AppendLine($"[Start Game {gameID}]");

                sb.AppendLine("Initial Position:");
                sb.AppendLine(pos.ToString());

                sb.AppendLine($"\nFirst Player: {blackPlayer.Name}");
                sb.AppendLine($"Second Player: {whitePlayer.Name}");

                Console.WriteLine(sb.ToString());

                var gameInfo = new GameInfo(blackPlayer.Name, whitePlayer.Name, pos)
                {
                    BlackGameClock = blackPlayer == players[0] ? _clockConfigs[0] : _clockConfigs[1],
                    WhiteGameClock = whitePlayer == players[0] ? _clockConfigs[0] : _clockConfigs[1]
                };
                session = new GameSession(_config.SessionMode, blackEngine, whiteEngine, gameInfo);
                _activeSessions[gameID] = session;
                _blackIsPlayerZero[gameID] = blackPlayer == players[0];

                resultedGame = await session.Start(ct);

                if(session.State == GameSessionState.GameOver)
                {
                    var res = resultedGame!.Result;
                    Player winner, loser;
                    if (res?.Winner == DiscColor.Black)
                        (winner, loser) = (blackPlayer, whitePlayer);
                    else
                        (winner, loser) = (whitePlayer, blackPlayer);

                    sb.Clear();
                    sb.AppendLine($"[End Game {gameID}]");

                    if (res is null)
                    {
                        sb.AppendLine("Result: Unknown");
                    }
                    else
                    {
                        sb.Append("Result: ");
                        if (res.Winner != DiscColor.Null)
                        {
                            sb.Append($"{winner.Name}({res.Winner}) wins by {res.ScoreFromWinner} discs");

                            if (res.EndStatus == GameEndStatus.Timeout)
                                sb.Append(" (timeout)");

                            sb.AppendLine(".");

                            lock (winner)
                            {
                                winner.Stats.WinCount[(int)res.Winner]++;
                                winner.Stats.GainedScore[(int)res.Winner] += res.ScoreFromWinner;
                            }

                            lock (loser)
                            {
                                var color = (int)ReversiTypes.ToOpponent(res.Winner);
                                loser.Stats.LossCount[color]++;
                                loser.Stats.GainedScore[color] -= res.ScoreFromWinner;
                            }
                        }
                        else
                        {
                            sb.AppendLine("Draw.");

                            lock (blackPlayer)
                                blackPlayer.Stats.DrawCount[(int)DiscColor.Black]++;

                            lock (whitePlayer)
                                whitePlayer.Stats.DrawCount[(int)DiscColor.White]++;
                        }

                        for (var i = 0; i < 2; i++)
                        {
                            sb.Append($"{players[i].Name} v.s. {players[1 - i].Name}: ");
                            sb.Append(players[i].Stats.TotalWinCount).Append(" wins ");
                            sb.Append(players[i].Stats.TotalDrawCount).Append(" draws ");
                            sb.Append(players[i].Stats.TotalLossCount).Append(" losses (WinRate: ");
                            sb.Append(players[i].Stats.TotalWinRate * 100.0).AppendLine("%)");
                        }
                    }

                    Console.WriteLine(sb.ToString());
                }
            }
            catch (EngineException ex)
            {
                var currentGame = session?.CurrentGameInfo;

                // 思考エンジンがおかしな挙動をした場合は，現在の局面と棋譜が思考エンジンのデバッグの大きなヒントになるので出力する．
                sb.Clear();
                sb.AppendLine($"[Engine Error in Game {gameID}]");
                sb.AppendLine($"Detail: {ex.Message}");
                sb.AppendLine("Current position:");
                sb.AppendLine(currentGame?.TryGenerateFinalPosition()?.ToString());
                sb.AppendLine($"Move history: {string.Join(string.Empty, from move in currentGame?.Moves select move.Coord.ToString())}");
                Console.Error.WriteLine(sb.ToString());
            }
            finally
            {
                _activeSessions.TryRemove(gameID, out _);
                _blackIsPlayerZero.TryRemove(gameID, out _);
                Interlocked.Increment(ref _completedGameCount);

                if(blackEngine is not null)
                    blackPlayer.EnginePool.Return(blackEngine);

                if(whiteEngine is not null)
                    whitePlayer.EnginePool.Return(whiteEngine);

                session?.Dispose();
            }

            return resultedGame;
        }

        async Task<Player[]?> CreatePlayersAsync()
        {
            var players = new Player[2];

            try
            {
                for (var i = 0; i < 2; i++)
                {
                    Console.WriteLine($"Starting {_maxSessions} engines for player {i + 1}...");
                    players[i] = await Player.CreatePlayerAsync(_playerConfigs[i], _maxSessions);
                    Console.WriteLine("Done");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Failed to start engines. Detail: {ex.Message}");

                if (ex is not EngineException)  
                    Console.Error.WriteLine(ex.StackTrace);

                foreach (var p in players)
                    p?.Dispose();

                return null;
            }

            return players;
        }

        void SaveGameRecords(StreamWriter sw, GameInfo?[] games)
        {
            if (string.IsNullOrEmpty(_gameRecordPath))
                return;

            foreach (var game in games)
            {
                if (game is not null)
                    sw.WriteLine(game.ToGGFString());
            }
            sw.Flush();
        }

        static OpeningBook? LoadOpeningBook(string path)
        {
            if (string.IsNullOrEmpty(path))
                return OpeningBook.Empty;

            Console.WriteLine("Loading an opening book...");
            try
            {
                var book = new OpeningBook(path);

                if (book.NumPositions > 0)
                    Console.WriteLine($"{book.NumPositions} position{(book.NumPositions > 1 ? "s were" : " was")} loaded.");
                else
                    Console.Error.WriteLine($"Warning: Specified opening book is empty.");

                return book;
            }
            catch(IOException ex)
            {
                Console.Error.WriteLine($"Error: Failed to load an opening book. Detail: {ex.Message}");
                return null;
            }
        }
    }
}
