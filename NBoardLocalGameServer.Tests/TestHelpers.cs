using System;
using System.IO;
using System.Runtime.InteropServices;

namespace NBoardLocalGameServer.Tests
{
    internal static class TestHelpers
    {
        static readonly Lazy<string> DotnetMuxerPath = new(ResolveDotnetMuxerPath);

        public static PlayerConfig DummyEngineConfig(string name, int minDelayMs = 0, int maxDelayMs = 0, int chaosPercent = 0)
        {
            var dllPath = Path.Combine(AppContext.BaseDirectory, "DummyEngine.dll");
            var args = chaosPercent > 0 ? $"\"{dllPath}\" {name} {minDelayMs} {maxDelayMs} {chaosPercent}"
                : maxDelayMs > 0 ? $"\"{dllPath}\" {name} {minDelayMs} {maxDelayMs}"
                : $"\"{dllPath}\" {name}";
            return new PlayerConfig(DotnetMuxerPath.Value, args, string.Empty, []);
        }

        // Under "dotnet test" the current process is "testhost", not the dotnet muxer, so
        // Environment.ProcessPath can't be reused directly to launch an arbitrary managed dll.
        // The shared runtime directory always lives under the real dotnet install root though
        // (<root>/shared/Microsoft.NETCore.App/<version>/), regardless of how this process itself
        // was launched, so that's used to find the muxer instead.
        static string ResolveDotnetMuxerPath()
        {
            var runtimeDir = RuntimeEnvironment.GetRuntimeDirectory();
            var root = Path.GetFullPath(Path.Combine(runtimeDir, "..", "..", ".."));
            var candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");

            if (File.Exists(candidate))
                return candidate;

            if (Environment.ProcessPath is { } procPath &&
                Path.GetFileNameWithoutExtension(procPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                return procPath;

            throw new InvalidOperationException($"Could not resolve the dotnet muxer path (tried \"{candidate}\").");
        }

        public static GameServerConfig NewConfig(MatchMode mode) => new()
        {
            SessionMode = GameSessionMode.StatefulEngine,
            MatchMode = mode,
            SwapPlayer = true,
            UseSamePositionWhenSwapPlayer = true,
            OpeningBookPath = string.Empty,
            ShuffleBook = false
        };

        public static string NewTempFile(string extension)
            => Path.Combine(Path.GetTempPath(), $"nboard-tests-{Guid.NewGuid():N}.{extension}");
    }
}
