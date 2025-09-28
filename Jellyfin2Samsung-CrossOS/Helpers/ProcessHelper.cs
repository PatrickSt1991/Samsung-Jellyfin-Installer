using Jellyfin2SamsungCrossOS.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Helpers
{
    public class ProcessHelper
    {
        public static void KillSdbServers()
        {
            try
            {
                Process[] sdbProcesses = Process.GetProcessesByName("sdb");

                if (sdbProcesses.Length == 0)
                    return;

                foreach (Process proc in sdbProcesses)
                {
                    proc.Kill();
                    proc.WaitForExit();
                    Debug.WriteLine($"Killed SDB {proc.Id} - {proc.ProcessName}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to stop SDB server: {ex.Message}");
            }
        }
        public Task<ProcessResult> RunCommandCmdAsync(string fileName, string arguments, string? workingDirectory = null)
        {
            var cmdArgs = $"/c \"\"{fileName}\" {arguments}\"";
            return RunCommandAsync("cmd.exe", cmdArgs, workingDirectory);
        }
        public async Task<ProcessResult> RunCommandAsync(string fileName, string arguments, string? workingDirectory = null)
        {
            var tcs = new TaskCompletionSource<ProcessResult>();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory ?? ""
                },
                EnableRaisingEvents = true
            };

            var output = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

            process.Exited += (_, _) =>
            {
                tcs.SetResult(new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    Output = output.ToString()
                });
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return await tcs.Task;
        }
        public async Task<ProcessResult?> RunElevatedAndCaptureOutputAsync(string filePath, string arguments, string workingDir)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("filePath cannot be null or empty", nameof(filePath));

            if (string.IsNullOrEmpty(workingDir))
                workingDir = Environment.CurrentDirectory;

            var tempFile = Path.Combine(Path.GetTempPath(), $"tizen_ext_{Guid.NewGuid():N}.txt");

            try
            {
                ProcessStartInfo startInfo;

                if (OperatingSystem.IsWindows())
                {
                    var fullCommand = $"echo === Checking Tizen Packages activation status === && \"{filePath}\" {arguments} > \"{tempFile}\" 2>&1";
                    startInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c {fullCommand}",
                        WorkingDirectory = workingDir,
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = false
                    };
                }
                else
                {
                    startInfo = new ProcessStartInfo
                    {
                        FileName = filePath,
                        Arguments = arguments,
                        WorkingDirectory = workingDir,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                }

                using var process = Process.Start(startInfo);
                if (process == null)
                    return null;

                string output;

                if (OperatingSystem.IsWindows())
                {
                    await process.WaitForExitAsync();
                    if (!File.Exists(tempFile))
                        return null;

                    output = await File.ReadAllTextAsync(tempFile);
                    File.Delete(tempFile);
                }
                else
                {
                    var outputBuilder = new StringBuilder();
                    process.OutputDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
                    process.ErrorDataReceived += (_, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };

                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync();
                    output = outputBuilder.ToString();
                }

                return new ProcessResult
                {
                    ExitCode = process.ExitCode,
                    Output = output
                };
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return null; // user cancelled
            }
            catch
            {
                return null;
            }
        }
        public async Task<ProcessResult> RunPrivilegedCommandAsync(string programPath, string[] arguments, string? workingDirectory = null)
        {
            if (OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("RunPrivilegedCommandAsync only supports Linux/macOS currently.");
            }

            string fileName;
            string args;

            if (OperatingSystem.IsMacOS())
            {
                // Use osascript for macOS GUI privilege escalation
                fileName = "osascript";
                var command = $"{programPath} {string.Join(" ", arguments.Select(EscapeShellArgument))}";
                args = $"-e \"do shell script \\\"{EscapeAppleScriptString(command)}\\\" with administrator privileges\"";
            }
            else if (OperatingSystem.IsLinux())
            {
                // Try pkexec first (GUI prompt), fallback to gksu/kdesu
                var escalationTool = await GetLinuxPrivilegeEscalationTool();
                fileName = escalationTool;

                if (escalationTool == "pkexec")
                {
                    args = $"{programPath} {string.Join(" ", arguments.Select(EscapeShellArgument))}";
                }
                else if (escalationTool == "gksu" || escalationTool == "kdesu")
                {
                    var command = $"{programPath} {string.Join(" ", arguments.Select(EscapeShellArgument))}";
                    args = $"\"{command}\"";
                }
                else
                {
                    throw new InvalidOperationException("No GUI privilege escalation tool found. Please install pkexec, gksu, or kdesu.");
                }
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported OS.");
            }

            return await RunCommandAsync(fileName, args, workingDirectory);
        }
        private static string EscapeShellArgument(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return "\"\"";

            // Escape single quotes and wrap in single quotes for shell safety
            return $"'{arg.Replace("'", "'\"'\"'")}'";
        }
        private static string EscapeAppleScriptString(string str)
        {
            // Escape quotes and backslashes for AppleScript
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
        private async Task<string> GetLinuxPrivilegeEscalationTool()
        {
            // Check which GUI privilege escalation tools are available
            var tools = new[] { "pkexec", "gksu", "kdesu" };

            foreach (var tool in tools)
            {
                try
                {
                    var result = await RunCommandAsync("which", tool);
                    if (result.ExitCode == 0)
                        return tool;
                }
                catch
                {
                    // Continue to next tool
                }
            }

            return "pkexec"; // Default fallback
        }
    }
}