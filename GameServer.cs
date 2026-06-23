using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using NBoardLocalGameServer.Engine;
using NBoardLocalGameServer.Reversi;

namespace NBoardLocalGameServer
{
    internal class GameServerConfig
    {
        static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

        public GameSessionMode SessionMode { get; init; } = GameSessionMode.StatefulEngine;

        /// <summary>
        /// 1ゲームごとに手番を入れ替えるか
        /// </summary>
        public bool SwapPlayer { get; init; } = true;

        /// <summary>
        /// 手番を入れ替えたとき, 手番入れ替える前と同じ局面で再対局するか, もしくは別の局面を用意するか.
        /// SwapPlayerがtrueのときのみ有効.
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

    internal class GameServer(GameServerConfig config, PlayerConfig playerConfig0, PlayerConfig playerConfig1, string gameRecordPath, string playerStatsPath, int maxSessions)
    {
        readonly GameServerConfig _config = config;
        readonly PlayerConfig[] _playerConfigs = [playerConfig0, playerConfig1];
        readonly string _gameRecordPath = gameRecordPath;
        readonly string _playerStatsPath = playerStatsPath;
        readonly int _maxSessions = maxSessions;
        CancellationTokenSource? _cts;

        public async Task RunAsync(int numGames) 
        {
            Console.WriteLine($"The number of sessions: {_maxSessions}");

            Player[]? players = null;
            OpeningBook? book;
            try
            {
                players = await CreatePlayersAsync();

                if (players is null)
                    return;

                book = LoadOpeningBook(_config.OpeningBookPath);

                if (book is null)
                    return;

                if (_config.ShuffleBook)
                {
                    Console.WriteLine("Shuffle opening book");
                    book.Shuffle();
                    Console.WriteLine("done");
                }

                await MainloopAsync(numGames, players, book);

            }
            finally
            {
                foreach (var p in players ?? [])
                    p.Dispose();
            }
        }

        public void RequestStop() => _cts?.Cancel();

        async Task MainloopAsync(int numGames, Player[] players, OpeningBook book)
        {
            const int SaveChunk = 100;

            _cts = new CancellationTokenSource();
            var games = new List<Task<GameInfo?>>();
            Position? pos = null;
            using var gameRecordsSw = string.IsNullOrEmpty(_gameRecordPath) ? StreamWriter.Null : new StreamWriter(_gameRecordPath, File.Exists(_gameRecordPath));
            var serializerOptions = new JsonSerializerOptions { WriteIndented = true };

            try
            {
                int firstIdx = 0, secondIdx = 1;
                for (var i = 0; i < numGames; i++)
                {
                    if (games.Count == SaveChunk)
                    {
                        SaveGameRecords(gameRecordsSw, await Task.WhenAll(games));
                        File.WriteAllText(_playerStatsPath, JsonSerializer.Serialize(from p in players select p.Stats, serializerOptions));
                        games.Clear();
                    }

                    if (i % 2 == 0 || !_config.UseSamePositionWhenSwapPlayer)
                        pos = book.NumPositions != 0 ? book.GetPosition() : new Position();

                    games.Add(StartSession(i, new Position(pos!), players, players[firstIdx], players[secondIdx], _cts.Token));

                    if (_config.SwapPlayer)
                        (firstIdx, secondIdx) = (secondIdx, firstIdx);
                }

                SaveGameRecords(gameRecordsSw, await Task.WhenAll(games));
                File.WriteAllText(_playerStatsPath, JsonSerializer.Serialize(from p in players select p.Stats, serializerOptions));
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("Info: Game sessions were canceled by user interruption.");
            }
        }

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

                var gameInfo = new GameInfo(blackPlayer.Name, whitePlayer.Name, pos);
                session = new GameSession(_config.SessionMode, blackEngine, whiteEngine, gameInfo);

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
