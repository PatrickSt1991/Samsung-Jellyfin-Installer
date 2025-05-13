using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Xml.Linq;

namespace Samsung_Jellyfin_Installer.Services
{
    public class TizenInstallerService : ITizenInstallerService
    {
        private static readonly string[] PossibleTizenPaths =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tizen Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tizen Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TizenStudio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "TizenStudio"),
            "C:\\tizen-studio",
            "C:\\TizenStudioCli",
            Environment.GetEnvironmentVariable("TIZEN_STUDIO_HOME") ?? string.Empty
        ];

        private readonly HttpClient _httpClient;
        private readonly string _downloadDirectory;

        public string TizenCliPath { get; private set; }

        public TizenInstallerService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
            _downloadDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SamsungJellyfinInstaller",
                "Downloads");

            Directory.CreateDirectory(_downloadDirectory);
            TizenCliPath = FindTizenCliPath();
        }

        public async Task<bool> EnsureTizenCliAvailable()
        {
            if (File.Exists(TizenCliPath))
                return true;

            return await InstallMinimalCli();
        }

        public async Task<string> DownloadPackageAsync(string downloadUrl)
        {
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            var localPath = Path.Combine(_downloadDirectory, fileName);

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(localPath, FileMode.Create);

            await contentStream.CopyToAsync(fileStream);
            return localPath;
        }

        public async Task<InstallResult> InstallPackageAsync(string packageUrl, string tvIpAddress, Action<string> updateStatus)
        {
            var studioRoot = Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(TizenCliPath)).FullName).FullName;
            var sdbPath = Path.Combine(studioRoot, "sdb.exe");

            try
            {
                updateStatus("Connecting to device...");
                await RunCommandAsync(sdbPath, $"connect {tvIpAddress}");


                updateStatus("Retrieving device adress...");
                string tvName = await GetTvNameAsync(sdbPath);

                if (string.IsNullOrEmpty(tvName))
                    return InstallResult.FailureResult("TV Naam kon niet worden gevonden...");

                updateStatus("Updating certificate profile...");
                UpdateProfileCertificatePaths();

                updateStatus("Packaging the wgt file with certificate...");

                await RunCommandAsync(TizenCliPath, $"package -t wgt -s custom -- {packageUrl}");

                updateStatus("Installing package on device...");

                string installOutput = await RunCommandAsync(TizenCliPath, $"install -n \"{packageUrl}\" -t {tvName}");

                if (File.Exists(packageUrl) && !installOutput.Contains("Failed"))
                {
                    updateStatus("Installation succesful");
                    return InstallResult.SuccessResult();
                }

                updateStatus("Installation failed");
                return InstallResult.FailureResult($"Installation may have failed. Output: {installOutput}");
            }
            catch (Exception ex)
            {
                updateStatus("Installation failed");
                return InstallResult.FailureResult(ex.Message);
            }
            finally
            {
                await RunCommandAsync(sdbPath, $"disconnect {tvIpAddress}");
            }
        }
        private static async Task<string> GetTvNameAsync(string sdbPath)
        {
            var output = await RunCommandAsync(sdbPath, "devices");
            var match = Regex.Match(output, @"(?<=\n)([^\s]+)\s+device\s+(?<name>[^\s]+)");

            return match.Success ? match.Groups["name"].Value.Trim() : "";
        }

        private static void UpdateProfileCertificatePaths()
        {
            string profilePath = Path.GetFullPath("TizenProfile/profiles.xml");

            var xml = XDocument.Load(profilePath);
            var profileItems = xml.Descendants("profileitem").ToList();

            foreach (var item in profileItems)
            {
                string distributor = item.Attribute("distributor")?.Value;

                if (distributor == "0")
                    item.SetAttributeValue("key", Path.GetFullPath("TizenProfile/author.p12"));

                if (distributor == "1")
                    item.SetAttributeValue("key", Path.GetFullPath("TizenProfile/distributor.p12"));
            }

            xml.Save(profilePath);
        }
        private static string FindTizenCliPath()
        {
            foreach (var basePath in PossibleTizenPaths)
            {
                if (string.IsNullOrEmpty(basePath)) continue;

                var possiblePath = Path.Combine(basePath, "tools", "ide", "bin", "tizen.bat");
                if (File.Exists(possiblePath))
                    return possiblePath;
            }
            return null;
        }
        private static async Task<string> RunCommandAsync(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(fileName)
            };

            using var proc = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    outputBuilder.AppendLine(e.Data);
            };

            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    errorBuilder.AppendLine(e.Data);
            };

            proc.Start();

            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
                throw new Exception($"Command failed: {fileName} {arguments}\nOutput: {outputBuilder}\nError: {errorBuilder}");

            return outputBuilder.ToString();
        }

        private async Task<bool> InstallMinimalCli()
        {
            string installerPath = null;
            try
            {
                var InstallCLI = MessageBox.Show("Tizen CLI 5.5 is required to continue.\n\n" +
                                    "We will now download and install Tizen CLI 5.5.\n" +
                                    "This may take a few minutes. Please be patient during the installation process.",
                                    "Tizen CLI 5.5 Required",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Information);

                if (InstallCLI == MessageBoxResult.Yes)
                {
                    const string installerUrl = "https://download.tizen.org/sdk/Installer/tizen-studio_5.5/web-cli_Tizen_Studio_5.5_windows-64.exe";
                    installerPath = await DownloadPackageAsync(installerUrl);
                    string installPath = Path.Combine("C:", "TizenStudioCli");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = installerPath,
                        Arguments = $"--accept-license \"{installPath}\"",
                        UseShellExecute = true,
                        CreateNoWindow = false
                    };

                    using var process = Process.Start(startInfo);
                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        TizenCliPath = FindTizenCliPath();
                        return TizenCliPath != null;
                    }
                    return false;
                }
                else
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (installerPath != null && File.Exists(installerPath))
                        File.Delete(installerPath);
                }
                catch { /* Ignore cleanup errors */ }
            }
        }
    }

    public class InstallResult
    {
        public bool Success { get; init; }
        public string ErrorMessage { get; init; }

        public static InstallResult SuccessResult() => new InstallResult { Success = true };
        public static InstallResult FailureResult(string error) => new InstallResult
        {
            Success = false,
            ErrorMessage = error
        };
    }
}