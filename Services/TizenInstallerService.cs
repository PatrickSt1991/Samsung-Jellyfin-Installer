using Samsung_Jellyfin_Installer.Converters;
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
        public string? TizenCypto { get; private set; }
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
                TizenCypto = Path.Combine(tizenRoot, "tools", "certificate-encryptor","wincrypt.exe");

                string tizenDataRoot = Path.Combine(Path.GetDirectoryName(tizenRoot)!, Path.GetFileName(tizenRoot) + "-data");
                TizenDataPath = Path.Combine(tizenDataRoot, "profile", "profiles.xml");
            }
        }

        public async Task<(string, string)> EnsureTizenCliAvailable()
        {
            if (File.Exists(TizenCliPath) && File.Exists(TizenSdbPath))
                return (TizenDataPath, TizenCypto);

            return (await InstallMinimalCli(), TizenCliPath);
        }
        public async Task<bool> ConnectToTvAsync(string tvIpAddress)
        {
            if (TizenSdbPath is null)
                return false;

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
                updateStatus("PleaseInstallTizen".Localized());
                return InstallResult.FailureResult("PleaseInstallTizen".Localized());
            }

            try
            {
                updateStatus("RetrievingDeviceAddress".Localized());
                string tvName = await GetTvNameAsync(tvIpAddress);

                if (string.IsNullOrEmpty(tvName))
                    return InstallResult.FailureResult(Strings.TvNameNotFound);


                updateStatus("CheckTizenOS".Localized());
                string tizenOs = await FetchTizenOsVersion(TizenSdbPath);

                tizenOs = "8.0";

                if (new Version(tizenOs) >= new Version("7.0"))
                {
                    try
                    {
                        string selectedCertificate = Settings.Default.Certificate;

                        if(string.IsNullOrEmpty(selectedCertificate) || selectedCertificate == "Jelly2Sams (default)")
                        {
                            SamsungAuth auth = await SamsungLoginService.PerformSamsungLoginAsync();
                            if (!string.IsNullOrEmpty(auth.access_token))
                            {

                                updateStatus("SuccessAuthCode".Localized());

                                var certificateService = new TizenCertificateService(_httpClient);
                                (string p12Location, string p12Password) = await certificateService.GenerateProfileAsync(
                                    duid: tvName,
                                    accessToken: auth.access_token,
                                    userId: auth.userId,
                                    outputPath: Path.Combine(Environment.CurrentDirectory, "TizenProfile"),
                                    updateStatus
                                );

                                PackageCertificate = "Jelly2Sams";
                                UpdateCertificateManager(p12Location, p12Password, updateStatus);
                            }
                            else
                            {
                                updateStatus("FailedAuthCode".Localized());
                            }
                        }
                        else
                        {
                            PackageCertificate = selectedCertificate;
                        }
                    }
                    catch (Exception ex)
                    {
                        updateStatus($"Output: {ex.Message}".Localized());
                        throw;
                    }
                }
                else
                {
                    updateStatus("UpdatingCertificateProfile".Localized());
                    UpdateProfileCertificatePaths();
                    PackageCertificate = "custom";
                }
                


                updateStatus("PackagingWgtWithCertificate".Localized());
                await RunCommandAsync(TizenCliPath, $"package -t wgt -s {PackageCertificate} -- \"{packageUrl}\"");

                if (Settings.Default.DeletePreviousInstall)
                {
                    Debug.WriteLine("delete old");
                    updateStatus("Removing old Jellyfin app");
                    await RemoveJellyfinAppByIdAsync(tvName, updateStatus);
                }

                updateStatus("InstallingPackage".Localized());

                return InstallResult.FailureResult($"delete old dev stop");
                string installOutput = await RunCommandAsync(TizenCliPath, $"install -n \"{packageUrl}\" -t {tvName}");

                if (File.Exists(packageUrl) && !installOutput.Contains("Failed"))
                {
                    updateStatus("InstallationSuccessful".Localized());
                    return InstallResult.SuccessResult();
                }

                updateStatus("InstallationFaile".Localized());
                return InstallResult.FailureResult($"{"InstallationMaybeFailed".Localized()}. {"Output".Localized()}: {installOutput}");
            }
            catch (Exception ex)
            {
                updateStatus("InstallationFailed".Localized());
                return InstallResult.FailureResult(ex.Message);
            }
            finally
            {
                await RunCommandAsync(TizenSdbPath, $"disconnect {tvIpAddress}");
            }
        }
        public async Task<string> GetTvNameAsync(string tvIpAddress)
        {
            if (TizenSdbPath is null)
                return string.Empty;

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

            return null;
        }
        private void UpdateCertificateManager(string p12Location, string p12Password, Action<string> updateStatus)
        {
            updateStatus("SettingCertificateManager".Localized());
            string profileName = "Jelly2Sams";
            XElement jelly2SamsProfile = new XElement("profile",
                new XAttribute("name", profileName),
                new XElement("profileitem",
                    new XAttribute("ca", ""),
                    new XAttribute("distributor", "0"),
                    new XAttribute("key", Path.Combine(p12Location, "author.p12")),
                    new XAttribute("password", $"{p12Password}"),
                    new XAttribute("rootca", "")
                ),
                new XElement("profileitem",
                    new XAttribute("ca", ""),
                    new XAttribute("distributor", "1"),
                    new XAttribute("key", Path.Combine(p12Location, "distributor.p12")),
                    new XAttribute("password", $"{p12Password}"),
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
                XElement? existingProfile = root.Elements("profile")
                    .FirstOrDefault(p => string.Equals(p.Attribute("name")?.Value, profileName, StringComparison.OrdinalIgnoreCase));

                if (existingProfile is null)
                {
                    // Add new profile if it doesn't exist
                    root.Add(jelly2SamsProfile);
                }
                else if (string.Equals(profileName, "Jelly2Sams", StringComparison.OrdinalIgnoreCase))
                {
                    // Update password only for specific keys in Jelly2Sams profile
                    foreach (var item in existingProfile.Elements("profileitem"))
                    {
                        string? keyValue = item.Attribute("key")?.Value;
                        if (!string.IsNullOrEmpty(keyValue) &&
                            (keyValue.EndsWith("author.p12", StringComparison.OrdinalIgnoreCase) ||
                             keyValue.EndsWith("distributor.p12", StringComparison.OrdinalIgnoreCase)))
                        {
                            item.SetAttributeValue("password", p12Password);
                        }
                    }
                }

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
        private async Task<string> InstallMinimalCli()
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

                if (InstallCLI != MessageBoxResult.Yes)
                    return "User declined to install Tizen CLI.";

                installerPath = await DownloadPackageAsync(Settings.Default.TizenCliUrl);
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

                if (process.ExitCode != 0)
                    return "Tizen CLI installation failed.";

                var tizenRoot = FindTizenRoot() ?? string.Empty;
                TizenCliPath = Path.Combine(tizenRoot, "tools", "ide", "bin", "tizen.bat");
                TizenSdbPath = Path.Combine(tizenRoot, "tools", "sdb.exe");

                return tizenRoot != string.Empty ? string.Empty : "Tizen root folder not found after installation.";
            }
            catch (Exception ex)
            {
                return $"An error occurred during installation: {ex.Message}";
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
        public async Task RemoveJellyfinAppByIdAsync(string tvName, Action<string> updateStatus)
        {
            try
            {
                string output = await RunCommandAsync(TizenSdbPath, "shell 0 applist");
                var jellyfinPattern = @"'Jellyfin'\s+'([^']+)'";
                var match = Regex.Match(output, jellyfinPattern, RegexOptions.IgnoreCase);

                if (match.Success && match.Groups.Count > 1)
                {
                    string appId = match.Groups[1].Value;
                    if (!string.IsNullOrEmpty(appId))
                    {
                        try
                        {
                            
                            await RunCommandAsync(TizenSdbPath, $"uninstall -t {tvName} -p {appId}");
                        }
                        catch (Exception ex)
                        {
                            updateStatus($"Output: {ex.Message}".Localized());
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                updateStatus($"Output: {ex.Message}".Localized());
            }
        }
    }
}