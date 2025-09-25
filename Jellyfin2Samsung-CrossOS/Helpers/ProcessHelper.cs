using Jellyfin2SamsungCrossOS.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    }
}