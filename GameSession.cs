using NBoardLocalGameServer.Engine;
using NBoardLocalGameServer.Reversi;
using System;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace NBoardLocalGameServer
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    internal enum GameSessionMode
    {
        // サーバー側が常に現在の局面と残り時間をGGF形式で送信するモード
        StatelessEngine,

        // サーバー側が対局開始時のみ初期局面と持ち時間を送信するモード
        StatefulEngine
    }

    internal enum GameSessionState
    {
        NotStarted,
        Playing,
        SuspendedWithError,
        Cancelled,
        GameOver
    }

    internal class GameSession(GameSessionMode mode, NBoardEngine blackEngine, NBoardEngine whiteEngine, GameInfo initialGameInfo) : IDisposable
    {
        GameSessionMode _mode = mode;
        NBoardEngine _blackEngine = blackEngine, _whiteEngine = whiteEngine;
        NBoardEngine? _currentEngine, _opponentEngine;
        GameClock? _blackClock, _whiteClock;
        GameInfo _initialGameInfo = new (initialGameInfo);
        GameInfo _currentGameInfo = new(initialGameInfo);
        GameSessionState _state = GameSessionState.NotStarted;
        EngineConnectionException? _lastConnectionException;

        public GameSessionState State => _state;
        public GameInfo CurrentGameInfo => new(_currentGameInfo);

        public async Task<GameInfo?> Start(CancellationToken ct)
        {
            if (Interlocked.CompareExchange(ref _state, GameSessionState.Playing, GameSessionState.NotStarted) != GameSessionState.NotStarted)
                throw new InvalidOperationException("Session has already started or terminated");

            var pos = new Position(_currentGameInfo.RootPosition);
            _blackEngine.SetGameInfo(_currentGameInfo);
            _whiteEngine.SetGameInfo(_currentGameInfo);

            _blackClock = _currentGameInfo.BlackGameClock is not null ? new GameClock(_currentGameInfo.BlackGameClock) : null;
            _whiteClock = _currentGameInfo.WhiteGameClock is not null ? new GameClock(_currentGameInfo.WhiteGameClock) : null;
            _blackClock?.StatusChanged += GameClock_StatusChanged;
            _whiteClock?.StatusChanged += GameClock_StatusChanged;

            GameClock? currentClock, opponentClock;

            if (pos.SideToMove == DiscColor.Black)
            {
                (_currentEngine, _opponentEngine) = (_blackEngine, _whiteEngine);
                (currentClock, opponentClock) = (_blackClock, _whiteClock);
            }
            else
            {
                (_currentEngine, _opponentEngine) = (_whiteEngine, _blackEngine);
                (currentClock, opponentClock) = (_whiteClock, _blackClock);
            }

            while (!ct.IsCancellationRequested && !pos.IsGameOver)
            {
                BoardCoordinate moveCoord;
                var sideToMove = pos.SideToMove;

                try
                {
                    currentClock?.Start();
                    moveCoord = await _currentEngine.ThinkAsync();
                }
                catch (OperationCanceledException)
                {
                    if (currentClock?.Status != GameClockStatus.Timeout)
                    {
                        Interlocked.Exchange(ref _state, GameSessionState.Cancelled);
                        return null;
                    }
                    moveCoord = BoardCoordinate.Null;
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref _state, GameSessionState.SuspendedWithError);
                    throw;  // 細かいハンドリングは呼び出し元に任せる．
                }
                finally
                {
                    currentClock?.Stop();
                }

                if (_lastConnectionException is not null)
                {
                    Interlocked.Exchange(ref _state, GameSessionState.SuspendedWithError);
                    throw _lastConnectionException;
                }

                if (currentClock?.Status == GameClockStatus.Timeout)
                    break;

                if (!pos.Update(moveCoord))
                {
                    Interlocked.Exchange(ref _state, GameSessionState.SuspendedWithError);
                    throw new EngineReturnedIllegalMoveException(_currentEngine.ProcessInfo, moveCoord);
                }

                ApplyMove(new Move(sideToMove, moveCoord), currentClock);

                (_currentEngine, _opponentEngine) = (_opponentEngine, _currentEngine);
                (currentClock, opponentClock) = (opponentClock, currentClock);
            }

            if (ct.IsCancellationRequested)
                return null;

            if (currentClock?.Status != GameClockStatus.Timeout)
                _currentGameInfo.Result = new GameResult(pos.Winner, pos.Winner != DiscColor.Null ? pos.GetScoreFrom(pos.Winner)!.Value : 0);
            else
                _currentGameInfo.Result = new GameResult(ReversiTypes.ToOpponent(pos.SideToMove), Constants.MaxScore, GameEndStatus.Timeout);

            Interlocked.Exchange(ref _state, GameSessionState.GameOver);

            // _currentGameInfoがもつGameCLockConfigは残り時間に応じて値を変えている（StatelessEngineモードのとき）
            // 呼び出し元に返す時は，元々どれだけ持ち時間が与えられていたのかという情報を含めたいので，
            // 元の持ち時間に書き換えたオブジェクトを返す.
            return new GameInfo(_currentGameInfo)
            {
                BlackGameClock = _initialGameInfo.BlackGameClock,
                WhiteGameClock = _initialGameInfo.WhiteGameClock
            };
        }

        async void GameClock_StatusChanged(object? sender, GameClockStatus e)
        {
            if (e != GameClockStatus.Timeout)
                return;

            try
            {
                var clock = (GameClock)sender!;
                var engine = (clock == _blackClock) ? _blackEngine : _whiteEngine;
                await engine.StopThinkingAsync();
            }
            catch (EngineConnectionException ex)
            {
                Interlocked.CompareExchange(ref _lastConnectionException, ex, null);
            }
            catch (Exception ex)
            {
                // 非同期イベントハンドラの例外は外部でキャッチできないので，とりあえずエラー出力する．
                Console.Error.WriteLine("Error: An exception was thrown in the event handler.");
                Console.Error.WriteLine(ex.StackTrace);
            }
        }

        void ApplyMove(Move move, GameClock? currentClock)
        {
            _currentGameInfo.Moves.Add(move);

            switch (_mode)
            {
                // StatelessEngineモード，現在の局面と残り時間を逐一思考エンジンに通知する．
                // そのため，思考エンジンが残り時間や現在の局面を管理する必要がない（ステートレス）．
                // ただし，ステートフルモードより通信のデータ量が増える．
                case GameSessionMode.StatelessEngine:
                    if (move.Color == DiscColor.Black)
                        _currentGameInfo.BlackGameClock = currentClock?.GetTimeLeft();
                    else
                        _currentGameInfo.WhiteGameClock = currentClock?.GetTimeLeft();

                    _blackEngine.SetGameInfo(_currentGameInfo);
                    _whiteEngine.SetGameInfo(_currentGameInfo);
                    break;

                // StatefulEngineモードでは，着手のみを思考エンジンに通知する．
                // そのため，思考エンジンは適切な局面と持ち時間を管理しなければならない（ステートフル）．
                // ただし，ステートレスモードより通信のデータ量は削減される．
                case GameSessionMode.StatefulEngine:
                    _blackEngine.SendMove(move.Coord);
                    _whiteEngine.SendMove(move.Coord);
                    break;

                default:
                    throw new UnreachableException($"Session is unknown mode \"{_mode}\".");
            }
        }

        public void Dispose()
        {
            _blackClock?.Dispose();
            _whiteClock?.Dispose();
        }
    }
}
