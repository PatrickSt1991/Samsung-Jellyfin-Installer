using Samsung_Jellyfin_Installer.Views;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace Samsung_Jellyfin_Installer.Converters
{
    public class ElevatedCommands
    {
        public static async Task<string?> RunElevatedAndCaptureOutputAsync(
            string filePath,
            string arguments,
            string workingDir,
            InstallingWindow installingWindow,
            string actionDescription)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("filePath cannot be null or empty", nameof(filePath));
            }

            if (string.IsNullOrEmpty(workingDir))
            {
                workingDir = Environment.CurrentDirectory;
            }

            var tempFile = Path.Combine(Path.GetTempPath(), $"tizen_ext_{Guid.NewGuid():N}.txt");

            // Safely encode message for echo

            // Construct full command: show message, then run tool, capture output to temp file
            var fullCommand = $"echo  === Checking Tizen Packages activation status === && \"{filePath}\" {arguments} > \"{tempFile}\" 2>&1";

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {fullCommand}",
                WorkingDirectory = workingDir,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = false // Set to false so the user sees the echo
            };

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    installingWindow.SetStatusText($"Failed to start elevated process: {actionDescription}");
                    return null;
                }

                await process.WaitForExitAsync();

                if (File.Exists(tempFile))
                {
                    var output = await File.ReadAllTextAsync(tempFile);
                    File.Delete(tempFile);
                    return output;
                }

                installingWindow.SetStatusText("Output file not found after elevation.");
                return null;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                installingWindow.SetStatusText("Operation cancelled by user.");
                return null;
            }
            catch (Exception ex)
            {
                installingWindow.SetStatusText($"Error during elevated run: {ex.Message}");
                return null;
            }
        }
    }
}
