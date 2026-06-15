using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace NBoardLocalGameServer
{
    internal enum GameClockStatus
    {
        UsingInitialTime = 1 << 0,   // 初期の持ち時間を使用中
        UsingExtraTime = 1 << 1,     // 延長時間 (e.g. 秒読み) を使用中
        SoftTimeout = 1 << 2,        // 時間切れ即負けの場合のタイムアウト
        HardTimeout = 1 << 3,         // 延長時間まで使い果たした場合のタイムアウト
        Timeout = SoftTimeout | HardTimeout
    }

    internal record GameClockConfig
    {
        public int InitialTimeMs { get; init; }
        public int NumMovesInInitialTime { get; init; }
        public bool NotLossOnTime { get; init; } = false;

        public int IncrementTimeMs { get; init; } = 0;
        public int NumMovesInIncrementTime { get; init; } = 1;
        public bool NotAdditiveIncrementTime { get; init; } = false;

        public int ExtraTimeMs { get; init; } = 0;
        public int NumMovesInExtraTime { get; init; } = 0;
        public bool ExtraTimeNotBeAdded { get; init; } = false;

        public override string ToString()
        {
            var iniStr = FormatTimeParam(InitialTimeMs, NotLossOnTime, NumMovesInInitialTime, defaultNumMoves: 0);
            var incStr = FormatTimeParam(IncrementTimeMs, NotAdditiveIncrementTime, NumMovesInIncrementTime, defaultNumMoves: 1);
            var extraStr = FormatTimeParam(ExtraTimeMs, ExtraTimeNotBeAdded, NumMovesInExtraTime, defaultNumMoves: 0);
            return $"{iniStr}/{incStr}/{extraStr}";
        }

        private static string FormatTimeParam(int timeMs, bool hasN, int numMoves, int defaultNumMoves)
        {
            var ts = TimeSpan.FromMilliseconds(timeMs);

            var timeStr = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";

            if (!hasN && numMoves == defaultNumMoves)
                return timeStr;

            return $"{timeStr},{(hasN ? "n" : "")}{numMoves}";
        }

        public static bool TryParseTime(string str, [NotNullWhen(true)] out GameClockConfig? clockConfig)
        {
            clockConfig = null;

            var strParts = str.Trim().Split('/');

            if (strParts.Length == 0 || strParts.Length > 3)
                return false;

            if (!TryParseTimeParam(strParts[0], out var ini))
                return false;

            if (strParts.Length < 2 || !TryParseTimeParam(strParts[1], out var inc))
                return false;

            if (strParts.Length < 3 || !TryParseTimeParam(strParts[2], out var extra))
                return false;

            clockConfig = new GameClockConfig
            {
                InitialTimeMs = ini.TimeMs,
                NumMovesInInitialTime = ini.NumMoves ?? 0,
                NotLossOnTime = ini.HasN,

                IncrementTimeMs = inc.TimeMs,
                NumMovesInIncrementTime = inc.NumMoves ?? 1,
                NotAdditiveIncrementTime = inc.HasN,

                ExtraTimeMs = extra.TimeMs,
                NumMovesInExtraTime = extra.NumMoves ?? 0,
                ExtraTimeNotBeAdded = extra.HasN
            };

            return clockConfig.NumMovesInIncrementTime > 0;
        }

        static bool TryParseTimeParam(string str, out (int TimeMs, bool HasN, int? NumMoves) timeParam)
        {
            timeParam = (0, false, null);

            var trimed = str.Trim();

            if (string.IsNullOrEmpty(trimed))
                return false;

            var commaIdx = trimed.IndexOf(',');
            var hhmmss = (commaIdx == -1) ? trimed : trimed[..commaIdx];

            if (!TryParseHHMMSS(hhmmss, out timeParam.TimeMs))
                return false;

            if (commaIdx == -1)
                return true;

            var restStr = trimed[(commaIdx + 1)..];

            if (string.IsNullOrEmpty(restStr))
                return true;

            if (restStr[0] == 'N' || restStr[0] == 'n')
            {
                timeParam.HasN = true;
                restStr = restStr.Substring(1).Trim();
            }

            if (!string.IsNullOrEmpty(restStr))
            {
                if (!int.TryParse(restStr, out var numMoves) || numMoves < 0)
                    return false;
                timeParam.NumMoves = numMoves;
            }

            return true;
        }

        static bool TryParseHHMMSS(string str, out int timeMs)
        {
            timeMs = 0;

            var trimed = str.Trim();
            if (string.IsNullOrEmpty(trimed))
                return true;

            var strParts = trimed.Split(':');

            if (strParts.Length == 0 || strParts.Length > 3)
                return false;

            int hh = 0, mm = 0, ss;
            if (strParts.Length == 3)
            {
                if (!int.TryParse(strParts[0], out hh) || !int.TryParse(strParts[1], out mm) || !int.TryParse(strParts[2], out ss))
                    return false;
            }
            else if (strParts.Length == 2)
            {
                if (!int.TryParse(strParts[1], out mm) || !int.TryParse(strParts[2], out ss))
                    return false;
            }
            else
            {
                if (!int.TryParse(strParts[2], out ss))
                    return false;
            }

            if (hh < 0 || mm < 0 || ss < 0)
                return false;

            timeMs = (int)new TimeSpan(hh, mm, ss).TotalMilliseconds;
            return true;
        }
    }

    internal class GameClock(GameClockConfig config) : IDisposable
    {
        public GameClockConfig Config { get; } = config;
        public GameClockStatus Status { get; private set; } = GameClockStatus.UsingInitialTime;
        public bool IsRunning { get; private set; }

        public event EventHandler<GameClockStatus>? StatusChanged;

        int _timeRemainMs = config.InitialTimeMs;
        long _startTimeMs;
        int _numTotalMoves = 0;
        int _numCurrentPhaseMoves = 0;
        int _timeConsumedMsInIncTime = 0;

        readonly object _syncLock = new();
        Timer? _timer;
        bool _disposed;

        /// <summary>
        /// 現在の時計の状態から、残りの持ち時間を表すGameClockConfigオブジェクトを生成する．
        /// </summary>
        public GameClockConfig GetTimeLeft()
        {
            lock (_syncLock)
            {
                // タイマー稼働中なら、現在の残り時間を計算
                int actualRemainMs = _timeRemainMs;
                if (IsRunning)
                {
                    long elapsedMs = Environment.TickCount64 - _startTimeMs;
                    actualRemainMs = Math.Max(0, _timeRemainMs - (int)elapsedMs);
                }

                // 既にタイムアウトしている場合
                if (Status == GameClockStatus.SoftTimeout || Status == GameClockStatus.HardTimeout || actualRemainMs <= 0)
                {
                    return Config with
                    {
                        InitialTimeMs = 0,
                        NumMovesInInitialTime = 0,
                        NotLossOnTime = false
                    };
                }

                if (Status == GameClockStatus.UsingInitialTime)
                {
                    // 初期の持ち時間を使用中の場合
                    int remainingMoves = Config.NumMovesInInitialTime > 0
                        ? Math.Max(1, Config.NumMovesInInitialTime - _numCurrentPhaseMoves)
                        : 0;

                    return Config with
                    {
                        InitialTimeMs = actualRemainMs,
                        NumMovesInInitialTime = remainingMoves
                    };
                }
                else
                {
                    // 延長時間（秒読み）を使用中の場合
                    // 現在の延長時間の残りを初期時間として登録．
                    int remainingMoves = Config.NumMovesInExtraTime > 0
                        ? Math.Max(1, Config.NumMovesInExtraTime - _numCurrentPhaseMoves)
                        : 0;

                    return Config with
                    {
                        InitialTimeMs = actualRemainMs,
                        NumMovesInInitialTime = remainingMoves,
                        NotLossOnTime = false
                    };
                }
            }
        }

        public void Start()
        {
            lock (_syncLock)
            {
                if (_disposed || IsRunning || Status is GameClockStatus.SoftTimeout or GameClockStatus.HardTimeout)
                    return;

                _startTimeMs = Environment.TickCount64;
                IsRunning = true;

                // 残り時間でタイマーを起動
                _timer ??= new Timer(OnTimerFired, null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(Math.Max(0, _timeRemainMs), Timeout.Infinite);
            }
        }

        public GameClockStatus Stop()
        {
            GameClockStatus? newStatusToNotify = null;

            lock (_syncLock)
            {
                if (_disposed || !IsRunning)
                    return Status;

                IsRunning = false;

                // タイマーを一時停止
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);

                long elapsedMs = Environment.TickCount64 - _startTimeMs;
                _timeRemainMs -= (int)elapsedMs;
                _timeConsumedMsInIncTime += (int)elapsedMs;

                if (_timeRemainMs <= 0)
                {
                    // タイムアウト処理（イベント通知用のステータスを受け取る）
                    newStatusToNotify = ProcessTimeoutLogic();
                }
                else
                {
                    // 以降，タイムアウトしなかった場合
                    _numTotalMoves++;
                    _numCurrentPhaseMoves++;

                    if (Config.IncrementTimeMs > 0 && Config.NumMovesInIncrementTime > 0)
                    {
                        if (_numTotalMoves % Config.NumMovesInIncrementTime == 0)
                        {
                            if (Config.NotAdditiveIncrementTime)
                            {
                                // ブロンシュタイン方式
                                _timeRemainMs += Math.Min(_timeConsumedMsInIncTime, Config.IncrementTimeMs);
                            }
                            else
                            {
                                // フィッシャー方式
                                _timeRemainMs += Config.IncrementTimeMs;
                            }
                            _timeConsumedMsInIncTime = 0;
                        }
                    }

                    // フェーズ移行判定処理
                    newStatusToNotify = ProcessPhaseTransitionLogic();
                }
            }

            if (newStatusToNotify.HasValue)
                StatusChanged?.Invoke(this, newStatusToNotify.Value);

            return Status;
        }

        /// <summary>
        /// 別スレッドのタイマーから呼び出されるコールバック
        /// </summary>
        void OnTimerFired(object? state)
        {
            GameClockStatus? newStatusToNotify = null;

            lock (_syncLock)
            {
                if (_disposed || !IsRunning)
                    return;

                long elapsedMs = Environment.TickCount64 - _startTimeMs;
                _timeRemainMs -= (int)elapsedMs;
                _timeConsumedMsInIncTime += (int)elapsedMs;

                newStatusToNotify = ProcessTimeoutLogic();

                // 延長時間に突入してまだ時計が動いている場合、新しい残り時間でタイマーを再開する
                if (IsRunning)
                {
                    _startTimeMs = Environment.TickCount64;
                    _timer?.Change(Math.Max(0, _timeRemainMs), Timeout.Infinite);
                }
                else
                {
                    _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }

            if (newStatusToNotify.HasValue)
                StatusChanged?.Invoke(this, newStatusToNotify.Value);
        }

        /// <summary>
        /// タイムアウト時のロジックを実行し、ステータス変更があれば新ステータスを返す
        /// </summary>
        GameClockStatus? ProcessTimeoutLogic()
        {
            var oldStatus = Status;

            if (Status == GameClockStatus.UsingInitialTime)
            {
                if (Config.NotLossOnTime)
                {
                    TransitionToOvertime();

                    if (_timeRemainMs <= 0)
                    {
                        Status = GameClockStatus.HardTimeout;
                        IsRunning = false;
                    }
                }
                else
                {
                    Status = GameClockStatus.SoftTimeout;
                    IsRunning = false;
                }
            }
            else if (Status == GameClockStatus.UsingExtraTime)
            {
                Status = GameClockStatus.HardTimeout;
                IsRunning = false;
            }

            return oldStatus != Status ? Status : null;
        }

        /// <summary>
        /// 規定手数に達した際のフェーズ移行ロジックを実行し、ステータス変更があれば新ステータスを返す 
        /// </summary>
        GameClockStatus? ProcessPhaseTransitionLogic()
        {
            var oldStatus = Status;

            if (Status == GameClockStatus.UsingInitialTime)
            {
                if (Config.NumMovesInInitialTime > 0 && _numCurrentPhaseMoves >= Config.NumMovesInInitialTime)
                    TransitionToOvertime();
            }
            else if (Status == GameClockStatus.UsingExtraTime)
            {
                if (Config.NumMovesInExtraTime > 0 && _numCurrentPhaseMoves >= Config.NumMovesInExtraTime)
                    ApplyOvertime();
            }

            return oldStatus != Status ? Status : null;
        }

        void ApplyOvertime()
        {
            if (Config.ExtraTimeNotBeAdded)
                _timeRemainMs = Config.ExtraTimeMs;
            else
                _timeRemainMs += Config.ExtraTimeMs;

            _numCurrentPhaseMoves = 0;
        }

        void TransitionToOvertime()
        {
            Status = GameClockStatus.UsingExtraTime;
            ApplyOvertime();
        }

        public void Dispose()
        {
            lock (_syncLock)
            {
                if (_disposed) return;
                _disposed = true;
                _timer?.Dispose();
            }
        }
    }
}