using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NBoardLocalGameServer.Web.Storage;

namespace NBoardLocalGameServer.Web.Services
{
    internal record EngineBuildResult(bool Success, int ExitCode, string Log);

    /// <summary>Runs a registered engine's build command as a shell process and captures its output.</summary>
    internal class EngineBuildService(EngineStore engineStore)
    {
        public async Task<EngineBuildResult> BuildAsync(string engineId, CancellationToken ct = default)
        {
            var engine = engineStore.Load(engineId) ?? throw new KeyNotFoundException($"Engine \"{engineId}\" was not found.");
            var workDir = Path.GetFullPath(Path.Combine(engineStore.GetExtractedRoot(engineId), engine.BuildWorkDir));

            var psi = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
                // On Windows, cmd.exe writes console output in the OS's OEM code page, not UTF-8 —
                // "chcp 65001" switches the child console to UTF-8 first so captured output (including
                // localized tool messages, e.g. Japanese dotnet CLI text) doesn't get mangled.
                Arguments = OperatingSystem.IsWindows()
                    ? $"/c chcp 65001>nul && {engine.BuildCommand}"
                    : $"-c \"{engine.BuildCommand.Replace("\"", "\\\"")}\"",
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var log = new StringBuilder();
            log.Append("$ ").AppendLine(engine.BuildCommand);
            log.Append("(working directory: ").Append(workDir).AppendLine(")");
            log.AppendLine();

            int exitCode;
            using (var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start the build process."))
            {
                var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                log.Append(await stdoutTask);
                var stderr = await stderrTask;
                if (!string.IsNullOrEmpty(stderr))
                    log.AppendLine().Append(stderr);

                exitCode = process.ExitCode;
            }

            var success = exitCode == 0;
            log.AppendLine().Append(success ? "Build succeeded" : $"Build failed (exit code {exitCode})");

            var logText = log.ToString();
            var buildTime = DateTime.Now;
            File.WriteAllText(engineStore.GetBuildLogPath(engineId), logText);
            engineStore.UpdateBuildStatus(engineId, success ? "Success" : $"Failed (exit {exitCode})", buildTime);

            return new EngineBuildResult(success, exitCode, logText);
        }
    }
}
