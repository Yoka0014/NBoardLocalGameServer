using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NBoardLocalGameServer.Reversi;

namespace NBoardLocalGameServer.Engine
{
    internal partial class NBoardEngine
    {
        const int NBoardVersion = 1;
        const int PingTimeoutMs = 10000;

        [GeneratedRegex(@"^\s*===")]
        private static partial Regex GoResponseRegex();

        public string? Name { get; private set; }
        public EngineProcessInfo ProcessInfo => _process.Info;

        EngineProcess _process;
        int _pingCount = 0;
        bool _quit = false;
        CancellationTokenSource? _thinkCts;

        NBoardEngine(EngineProcess process) 
        { 
            _process = process;
            _process.Exited += Process_Exited;
            _process.OnNonResponceTextRecieved += Process_OnNonResponceTextRecieved;
        }

        public static async Task<NBoardEngine> RunAsync(string path, string args, string workDir, IEnumerable<string> initalCommands)
        {
            EngineProcess process;

            try
            {
                process = EngineProcess.Start(path, args, workDir);
            }
            catch (Exception ex)
            {
                throw new EngineStartException(path, ex);
            }

            var engine = new NBoardEngine(process);
            engine.SendCommand($"nboard {NBoardVersion}");

            if(!await engine.CheckConnectionAsync())
                throw new EngineConnectionException(process.Info);

            foreach (var cmd in initalCommands)
                engine.SendCommand(cmd);

            return engine;
        }

        public bool TryQuit(int timeoutMs)
        {
            _quit = true;
            _thinkCts?.Cancel();
            SendCommand("quit");
            _process.WaitForExit(timeoutMs);
            return _process.HasExited;
        }

        public bool TryQuitForcely(int timeoutMs)
        {
            _thinkCts?.Cancel();
            _process.Kill();
            _process.WaitForExit(timeoutMs);
            return _process.HasExited;
        }

        public void SetDepth(int depth) => SendCommand($"set depth {depth}");

        public void SetGameInfo(GameInfo gameInfo) => SendCommand($"set game {gameInfo.ToGGFString()}");

        public void SendMove(BoardCoordinate move) => SendCommand($"move {move}");

        public async Task<BoardCoordinate> ThinkAsync()
        {
            var cts = new CancellationTokenSource();

            // 呼び出し側が正しく実装されていれば思考中に呼び出されることはない．
            if (Interlocked.CompareExchange(ref _thinkCts, cts, null) is not null)
            {
                cts.Dispose();
                throw new InvalidOperationException("Cannot execute multiple thinking task.");
            }

            try
            {
                var response = SendCommand("go", GoResponseRegex());

                try
                {
                    await response.Task.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // quitコマンドが呼ばれるなどして思考を止めた場合
                    return BoardCoordinate.Null;
                }

                var moveStr = response.Task.Result.Trim().Replace("===", string.Empty);
                var move = ReversiTypes.ParseCoordinate(moveStr);

                if (move == BoardCoordinate.Null)
                    throw new EngineReturnedInvalidMoveException(_process.Info, moveStr);

                return move;
            }
            finally
            {
                var usedCts = Interlocked.Exchange(ref _thinkCts, null);
                usedCts.Dispose();
            }
        }

        public async Task StopThinkingAsync()
        {
            if (!await CheckConnectionAsync())
                throw new EngineConnectionException(_process.Info);

            _thinkCts?.Cancel();
        }

        EngineResponse SendCommand(string cmd)
            => _process.SendCommand(cmd, (string?)null);

        EngineResponse SendCommand(string cmd, string? responsePattern)
            => _process.SendCommand(cmd, responsePattern);

        EngineResponse SendCommand(string cmd, Regex? responseRegex)
            => _process.SendCommand(cmd, responseRegex);

        async Task<bool> CheckConnectionAsync(int timeoutMs = PingTimeoutMs)
        {
            var pingID = _pingCount++;
            var response = SendCommand($"ping {pingID}", $"^\\s*pong\\s+{pingID}");

            try
            {
                await response.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        void Process_OnNonResponceTextRecieved(object? sender, string e)
        {
            // Non responseテキストとは，サーバーが送ったコマンドに対するレスポンスではなく，
            // 思考エンジンが任意のタイミングで自発的に送ってくるテキストのこと．
            // NBoard protocolではいくつかのNon responseテキストがあるが，ここでは"set myname"だけ解析し，
            // 他は無視する．

            ReadOnlySpan<char> span = e.AsSpan().Trim();

            if (span.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
            {
                span = span[4..].TrimStart();
                if (span.StartsWith("myname ", StringComparison.OrdinalIgnoreCase))
                    Name = span[7..].TrimStart().ToString();
            }
        }

        void Process_Exited(object? sender, EventArgs e)
        {
            _thinkCts?.Cancel();

            if (!_quit)
                throw new EngineStoppedUnexpectedlyException(_process.Info);
        }
    }
}
