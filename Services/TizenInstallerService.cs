using Samsung_Jellyfin_Installer.Localization;
using Samsung_Jellyfin_Installer.Models;
using System.Data;
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
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TizenStudioCli"),
            "C:\\tizen-studio",
            Environment.GetEnvironmentVariable("TIZEN_STUDIO_HOME") ?? string.Empty
        ];

        private readonly HttpClient _httpClient;
        private readonly string _downloadDirectory;

        public string? TizenCliPath { get; private set; }
        public string? TizenSdbPath { get; private set; }
        public string? TizenDataPath { get; private set; }
        public string? PackageCertificate { get; set; }

        public TizenInstallerService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
            _downloadDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SamsungJellyfinInstaller",
                "Downloads");

            Directory.CreateDirectory(_downloadDirectory);
            string? tizenRoot = FindTizenRoot();

            if (tizenRoot is not null)
            {
                TizenCliPath = Path.Combine(tizenRoot, "tools", "ide", "bin", "tizen.bat");
                TizenSdbPath = Path.Combine(tizenRoot, "tools", "sdb.exe");

                string tizenDataRoot = Path.Combine(Path.GetDirectoryName(tizenRoot)!, Path.GetFileName(tizenRoot) + "-data");
                TizenDataPath = Path.Combine(tizenDataRoot, "profile", "profiles.xml");
            }
        }

        public async Task<bool> EnsureTizenCliAvailable()
        {
            if (File.Exists(TizenCliPath) && File.Exists(TizenSdbPath))
                return true;

            return await InstallMinimalCli();
        }

        public async Task<bool> ConnectToTvAsync(string tvIpAddress)
        {
            if (TizenSdbPath is null)
            {
                return false;
            }

            try
            {
                var result = await RunCommandAsync(TizenSdbPath, $"connect {tvIpAddress}");
                return result.Contains($"connected to {tvIpAddress}");
            }
            catch (Exception)
            {
                return false;
            }
        }

        public async Task<string?> GetTvNameAsync(string tvIpAddress)
        {
            if (TizenSdbPath is null)
            {
                return null;
            }

            try
            {
                await ConnectToTvAsync(tvIpAddress);

                var output = await RunCommandAsync(TizenSdbPath, "devices");
                var match = Regex.Match(output, @"(?<=\n)([^\s]+)\s+device\s+(?<name>[^\s]+)");

                return match.Success ? match.Groups["name"].Value.Trim() : null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get TV name: {ex}");
            }
            finally
            {
                await RunCommandAsync(TizenSdbPath, $"disconnect {tvIpAddress}");
            }

            return null;
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
            if (TizenCliPath is null || TizenSdbPath is null)
            {
                updateStatus(Strings.PleaseInstallTizen);
                return InstallResult.FailureResult(Strings.PleaseInstallTizen);
            }

            try
            {
                updateStatus(Strings.ConnectingToDevice);
                await RunCommandAsync(TizenSdbPath, $"connect {tvIpAddress}");

                updateStatus(Strings.RetrievingDeviceAddress);
                string tvName = await GetTvNameAsync();

                if (string.IsNullOrEmpty(tvName))
                    return InstallResult.FailureResult(Strings.TvNameNotFound);


                updateStatus(Strings.CheckTizenOS);
                string tizenOs = await FetchTizenOsVersion(TizenSdbPath);
                if (new Version(tizenOs) >= new Version("7.0"))
                {
                    try
                    {
                        SamsungAuth auth = await SamsungLoginService.PerformSamsungLoginAsync();

                        if (!string.IsNullOrEmpty(auth.access_token))
                        {

                            updateStatus(Strings.SuccessAuthCode);

                            var certificateService = new TizenCertificateService(_httpClient);

                            string p12Location = await certificateService.GenerateProfileAsync(
                                duid: tvName,
                                accessToken: auth.access_token,
                                userId: auth.userId,
                                outputPath: Path.Combine(Environment.CurrentDirectory, "TizenProfile"),
                                updateStatus
                            );

                            PackageCertificate = "Jelly2Sams";
                            UpdateCertificateManager(p12Location, updateStatus);
                        }
                        else
                        {
                            updateStatus(Strings.FailedAuthCode);
                        }
                    }
                    catch (Exception ex)
                    {
                        updateStatus($"{Strings.Output}: {ex.Message}");
                        throw;
                    }
                }
                else
                {
                    updateStatus(Strings.UpdatingCertificateProfile);
                    UpdateProfileCertificatePaths();
                    PackageCertificate = "custom";
                }

                updateStatus(Strings.PackagingWgtWithCertificate);

                await RunCommandAsync(TizenCliPath, $"package -t wgt -s {PackageCertificate} -- \"{packageUrl}\"");

                updateStatus(Strings.InstallingPackage);

                string installOutput = await RunCommandAsync(TizenCliPath, $"install -n \"{packageUrl}\" -t {tvName}");

                if (File.Exists(packageUrl) && !installOutput.Contains("Failed"))
                {
                    updateStatus(Strings.InstallationSuccessful);
                    return InstallResult.SuccessResult();
                }

                updateStatus(Strings.InstallationFailed);
                return InstallResult.FailureResult($"{Strings.InstallationMaybeFailed}. {Strings.Output}: {installOutput}");
            }
            catch (Exception ex)
            {
                updateStatus(Strings.InstallationFailed);
                return InstallResult.FailureResult(ex.Message);
            }
            finally
            {
                await RunCommandAsync(TizenSdbPath, $"disconnect {tvIpAddress}");
            }
        }
        private async Task<string> GetTvNameAsync()
        {
            if (TizenSdbPath is null)
            {
                return string.Empty;
            }

            var output = await RunCommandAsync(TizenSdbPath, "devices");
            var match = Regex.Match(output, @"(?<=\n)([^\s]+)\s+device\s+(?<name>[^\s]+)");

            return match.Success ? match.Groups["name"].Value.Trim() : string.Empty;
        }
        private void UpdateCertificateManager(string p12Location, Action<string> updateStatus)
        {
            updateStatus(Strings.SettingCertificateManager);
            string profileName = "Jelly2Sams";
            XElement jelly2SamsProfile = new XElement("profile",
                new XAttribute("name", profileName),
                new XElement("profileitem",
                    new XAttribute("ca", ""),
                    new XAttribute("distributor", "0"),
                    new XAttribute("key", Path.Combine(p12Location, "author.p12")),
                    new XAttribute("password", Path.Combine(p12Location, "author.pwd")),
                    new XAttribute("rootca", "")
                ),
                new XElement("profileitem",
                    new XAttribute("ca", ""),
                    new XAttribute("distributor", "1"),
                    new XAttribute("key", Path.Combine(p12Location, "distributor.p12")),
                    new XAttribute("password", Path.Combine(p12Location, "distributor.pwd")),
                    new XAttribute("rootca", "")
                ),
                new XElement("profileitem",
                    new XAttribute("ca", ""),
                    new XAttribute("distributor", "2"),
                    new XAttribute("key", ""),
                    new XAttribute("password", ""),
                    new XAttribute("rootca", "")
                )
            );

            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(TizenDataPath)!);

            XDocument doc;
            if (!File.Exists(TizenDataPath))
            {
                // Create new XML file
                doc = new XDocument(
                    new XDeclaration("1.0", "UTF-8", "no"),
                    new XElement("profiles",
                        new XAttribute("active", "Jelly2Sams"),
                        new XAttribute("version", "3.1"),
                        jelly2SamsProfile
                    )
                );
            }
            else
            {
                doc = XDocument.Load(TizenDataPath);

                XElement root = doc.Element("profiles")!;
                bool exists = root.Elements("profile")
                    .Any(p => p.Attribute("name")?.Value == profileName);

                if (!exists)
                    root.Add(jelly2SamsProfile);
                else
                    return;
            }

            doc.Save(TizenDataPath);
        }
        private static void UpdateProfileCertificatePaths()
        {
            string profilePath = Path.GetFullPath("TizenProfile/preSign/profiles.xml");

            var xml = XDocument.Load(profilePath);
            var profileItems = xml.Descendants("profileitem").ToList();

            foreach (var item in profileItems)
            {
                string distributor = item.Attribute("distributor")?.Value;

                if (distributor == "0")
                    item.SetAttributeValue("key", Path.GetFullPath("TizenProfile/preSign/author.p12"));

                if (distributor == "1")
                    item.SetAttributeValue("key", Path.GetFullPath("TizenProfile/preSign/distributor.p12"));
            }

            xml.Save(profilePath);
        }
        private static string? FindTizenRoot()
        {
            foreach (var basePath in PossibleTizenPaths)
            {
                if (string.IsNullOrEmpty(basePath)) continue;

                var possiblePath = Path.Combine(basePath, "tools", "ide", "bin", "tizen.bat");
                if (File.Exists(possiblePath))
                    return basePath;
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
        private static async Task<string> FetchTizenOsVersion(string sdbPath)
        {
            var output = await RunCommandAsync(sdbPath, "capability");
            var match = Regex.Match(output, @"platform_version:([\d.]+)");


            return match.Success ? match.Groups[1].Value.Trim() : "";
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
                    string installPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Programs",
                        "TizenStudioCli"
                    );

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
                        var tizenRoot = FindTizenRoot() ?? string.Empty;
                        TizenCliPath = Path.Combine(tizenRoot, "tools", "ide", "bin", "tizen.bat");
                        TizenSdbPath = Path.Combine(tizenRoot, "tools", "sdb.exe");

                        return tizenRoot != string.Empty;
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