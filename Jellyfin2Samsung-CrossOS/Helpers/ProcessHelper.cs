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