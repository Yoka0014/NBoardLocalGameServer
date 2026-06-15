using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NBoardLocalGameServer.Engine
{
    internal class EngineResponse(string cmd)
    {
        public string Command { get; private set; } = cmd;

        public TaskCompletionSource<string> CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> Task => CompletionSource.Task;
    }

    record EngineProcessInfo(string Path, string Name, int Pid);

    internal class EngineProcess : IDisposable
    {
        public EngineProcessInfo Info => new(Path, Name, Id);
        public string Path => _process.StartInfo.FileName;
        public string Name => _process.ProcessName;
        public int Id => _process.Id;
        public bool HasExited => _process.HasExited;
        public event EventHandler Exited { add => _process.Exited += value; remove => _process.Exited -= value; }

        public event EventHandler<string>? OnNonResponceTextRecieved;

        readonly Process _process;

        // コマンドに対するレスポンスの待機リスト．
        readonly LinkedList<(Regex Pattern, EngineResponse Response)> _waitingResponseList = new();

        EngineProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += Process_OutputDataReceived;
            _process.ErrorDataReceived += Process_ErrorDataReceived;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public static EngineProcess Start(string path, string args = "", string workDir = "")
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            if (workDir != string.Empty)
                psi.WorkingDirectory = workDir;

            var process = Process.Start(psi) ?? throw new UnreachableException("Process.Start returned null despite UseShellExecute being false.");
            return new EngineProcess(process);
        }

        public void WaitForExit(int timeoutMs) => _process.WaitForExit(timeoutMs);

        public void Kill() => _process.Kill();

        public EngineResponse SendCommand(string cmd, string? responseRegex = null)
        {
            var regex = (responseRegex is not null) ? new Regex(responseRegex, RegexOptions.None) : null;
            return SendCommand(cmd, regex);
        }

        public EngineResponse SendCommand(string cmd, Regex? responseRegex)
        {
            var response = new EngineResponse(cmd);

            if (responseRegex is null)
            {
                response.CompletionSource.SetResult(string.Empty);
                _process.StandardInput.WriteLine(cmd);

                Debug.WriteLine($"Server -> {Name}(PID: {Id}): {cmd}");

                return response;
            }

            lock (_waitingResponseList)
                _waitingResponseList.AddLast((responseRegex, response));

            _process.StandardInput.WriteLine(cmd);

            Debug.WriteLine($"Server -> {Name}(PID: {Id}): {cmd}");

            return response;
        }

        void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is null) return;

            Debug.WriteLine($"{Name}(PID: {Id}) -> Server: {e.Data}");

            EngineResponse? response = null;
            lock (_waitingResponseList)
            {
                LinkedListNode<(Regex Pattern, EngineResponse Response)>? matchedNode = null;
                int matchCount = 0;

                var currentNode = _waitingResponseList.First;
                while (currentNode != null)
                {
                    if (currentNode.Value.Pattern.IsMatch(e.Data))
                    {
                        if (matchCount == 0) 
                            matchedNode = currentNode;
                        matchCount++;
                    }
                    currentNode = currentNode.Next;
                }

                if (matchCount == 0)
                {
                    OnNonResponceTextRecieved?.Invoke(this, e.Data);
                    return; 
                }

                if (matchCount > 1)
                {
                    Console.Write($"Info: {matchCount} waiting responses were found which match to regex \"{matchedNode!.Value.Pattern}\".");
                    Console.WriteLine($"Interpret as the response for the oldest command.");
                }

                _waitingResponseList.Remove(matchedNode!);
                response = matchedNode!.Value.Response;
            }

            response?.CompletionSource.TrySetResult(e.Data);
        }

        void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
            => Console.Error.WriteLine($"Engine Error Info: {e.Data} (Name: {Name}, PID: {Id})");

        public void Dispose() => _process?.Dispose();
    }
}