using Jellyfin2Samsung.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
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
        public async Task<ProcessResult> RunCommandAsync(string fileName, string arguments, string? workingDirectory = null)
        {
            var result = new ProcessResult();

            // Always run hidden (no console)
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? ""
            };

            // Build log file path (next to app .exe)
            string exeDir = AppContext.BaseDirectory;
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string sanitizedArguments = new string(arguments.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            if (string.IsNullOrEmpty(sanitizedArguments))
                sanitizedArguments = "unknown";

            string logFilePath = Path.Combine(exeDir, $"process_{sanitizedArguments}_{timestamp}.log");

            try
            {
                using var process = new Process { StartInfo = startInfo };

                process.Start();

                // Capture all output
                string stdOut = await process.StandardOutput.ReadToEndAsync();
                string stdErr = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                // Combine both
                var sb = new StringBuilder();
                if (!string.IsNullOrEmpty(stdOut))
                    sb.AppendLine(stdOut.TrimEnd());
                if (!string.IsNullOrEmpty(stdErr))
                {
                    sb.AppendLine();
                    sb.AppendLine("--- STDERR ---");
                    sb.AppendLine(stdErr.TrimEnd());
                }

                result.ExitCode = process.ExitCode;
                result.Output = sb.ToString();

                // Always write everything to a log file
                try
                {
                    Directory.CreateDirectory(exeDir);
                    await File.WriteAllTextAsync(logFilePath, result.Output);
                    result.Output += $"\n[Log written to: {logFilePath}]";
                }
                catch (Exception ex)
                {
                    result.Output += $"\n[Log write failed: {ex.Message}]";
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -1;
                result.Output = $"[Process start failed: {ex.Message}]";
            }

            return result;
        }

        public async Task MakeExecutableAsync(string filePath)
        {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                try
                {
                    // Use chmod to make the file executable
                    var output = await RunCommandAsync("chmod", $"+x \"{filePath}\"");
                    Debug.WriteLine($"Set executable permissions on {filePath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting executable permissions: {ex.Message}");
                    throw;
                }
            }
        }
    }
}