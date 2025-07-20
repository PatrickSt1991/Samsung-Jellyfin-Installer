using Samsung_Jellyfin_Installer.Converters;
using Samsung_Jellyfin_Installer.Models;
using Samsung_Jellyfin_Installer.Views;
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
            "C:\\tizen-studio",
            "C:\\TizenStudioCli",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tizen Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tizen Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TizenStudio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "TizenStudio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TizenStudioCli"),
            Environment.GetEnvironmentVariable("TIZEN_STUDIO_HOME") ?? string.Empty
        ];

        private readonly HttpClient _httpClient;
        private readonly string _downloadDirectory;
        private string _installPath;
        private const int MaxSafePathLength = 240;

        public string? TizenCliPath { get; private set; }
        public string? TizenSdbPath { get; private set; }
        public string? TizenDataPath { get; private set; }
        public string? TizenCypto { get; private set; }
        public string? TizenPluginPath { get; private set; }
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

            DetermineInstallPath();

            string? tizenRoot = FindTizenRoot();

            if (tizenRoot is not null)
            {
                TizenCliPath = Path.Combine(tizenRoot, "tools", "ide", "bin", "tizen.bat");
                TizenSdbPath = Path.Combine(tizenRoot, "tools", "sdb.exe");
                TizenCypto = Path.Combine(tizenRoot, "tools", "certificate-encryptor", "wincrypt.exe");
                TizenPluginPath = Path.Combine(tizenRoot, "ide", "plugins");

                string tizenDataRoot = Path.Combine(Path.GetDirectoryName(tizenRoot) ?? tizenRoot, Path.GetFileName(tizenRoot) + "-data");
                TizenDataPath = Path.Combine(tizenDataRoot, "profile", "profiles.xml");
            }
        }
        private void DetermineInstallPath()
        {
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "TizenStudioCli");

            var fallbackPath = "C:\\TizenStudioCli";

            if (defaultPath.Length > MaxSafePathLength)
            {
                var pathChange = MessageBox.Show(
                    "PathLengthExceeded".Localized(),
                    "PathLengthWarning".Localized(),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (pathChange == MessageBoxResult.No)
                {
                    Environment.Exit(1);
                    return;
                }

                // Use fallback path
                if (!Directory.Exists(fallbackPath))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c mkdir \"" + fallbackPath + "\"",
                        Verb = "runas",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };

                    try
                    {
                        Process.Start(psi)?.WaitForExit();
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        if (ex.NativeErrorCode == 1223)
                        {
                            MessageBox.Show(
                                "AdminPrivRequired".Localized(),
                                "PermissionDenied".Localized(),
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            Environment.Exit(1);
                            return;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }

                _installPath = fallbackPath;
            }
            else
            {
                _installPath = defaultPath;
            }
        }

        public async Task<(string, string)> EnsureTizenCliAvailable()
        {
            if (File.Exists(TizenCliPath) && File.Exists(TizenSdbPath))
                return (TizenDataPath, TizenCypto);

            var tizenInstallationPath = await InstallMinimalCli();
            return (tizenInstallationPath, TizenCypto);
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
                    return InstallResult.FailureResult("TvNameNotFound".Localized());

                string tvDuid = await GetTvDuidAsync();

                if (string.IsNullOrEmpty(tvDuid))
                    return InstallResult.FailureResult("TvDuidNotFound".Localized());

                updateStatus("CheckTizenOS".Localized());
                string tizenOs = await FetchTizenOsVersion(TizenSdbPath);

                if (new Version(tizenOs) >= new Version("6.0"))
                {
                    try
                    {
                        string selectedCertificate = Settings.Default.Certificate;

                        if (string.IsNullOrEmpty(selectedCertificate) || selectedCertificate == "Jelly2Sams (default)" || Settings.Default.ForceSamsungLogin)
                        {
                            SamsungAuth auth = await SamsungLoginService.PerformSamsungLoginAsync();
                            if (!string.IsNullOrEmpty(auth.access_token))
                            {

                                updateStatus("SuccessAuthCode".Localized());

                                var certificateService = new TizenCertificateService(_httpClient);
                                (string p12Location, string p12Password) = await certificateService.GenerateProfileAsync(
                                    duid: tvDuid,
                                    accessToken: auth.access_token,
                                    userId: auth.userId,
                                    outputPath: Path.Combine(Environment.CurrentDirectory, "TizenProfile"),
                                    updateStatus,
                                    TizenPluginPath ?? string.Empty
                                );

                                PackageCertificate = "Jelly2Sams";
                                Settings.Default.Certificate = PackageCertificate;
                                Settings.Default.Save();
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

                string packageUrlExtension = Path.GetExtension(packageUrl).TrimStart('.').ToLowerInvariant();

                await RunCommandAsync(TizenCliPath, $"package -t {packageUrlExtension} -s {PackageCertificate} -- \"{packageUrl}\"");

                if (Settings.Default.DeletePreviousInstall)
                {
                    updateStatus("Removing old Jellyfin app");
                    bool removeOldJelly = await RemoveJellyfinAppByIdAsync(tvName, updateStatus);

                    if (!removeOldJelly)
                    {
                        updateStatus("FailedRemoveOld".Localized());
                        return InstallResult.FailureResult("FailedRemoveOldExtra".Localized());
                    }
                }

                updateStatus("InstallingPackage".Localized());

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
        private async Task<string> GetTvDuidAsync()
        {
            if (TizenSdbPath is null)
                return string.Empty;

            var output = await RunCommandAsync(TizenSdbPath, "shell \"0 getduid\"");
            if (!string.IsNullOrWhiteSpace(output))
                return output.Trim();

            output = await RunCommandAsync(TizenSdbPath, "shell \"/opt/etc/duid-gadget 2 2> /dev/null\"");
            return output?.Trim() ?? string.Empty;
        }

        private void UpdateCertificateManager(string p12Location, string p12Password, Action<string> updateStatus)
        {
            if (string.IsNullOrEmpty(p12Location))
            {
                throw new ArgumentException("p12Location cannot be null or empty", nameof(p12Location));
            }

            if (string.IsNullOrEmpty(p12Password))
            {
                throw new ArgumentException("p12Password cannot be null or empty", nameof(p12Password));
            }

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

            string directoryPath = Path.GetDirectoryName(TizenDataPath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

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
            
            // Ensure the directory exists before trying to load the file
            var directoryPath = Path.GetDirectoryName(profilePath);
            if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

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
                WorkingDirectory = Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory
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
            InstallingWindow installingWindow = null;

            try
            {
                var InstallCLI = MessageBox.Show("Tizen CLI & Certificate manager are required to continue.\n\n" +
                                    "We will now download and install Tizen CLI followed by Certificate manager .\n" +
                                    "This may take a few minutes. Please be patient during the installation process.",
                                    "Tizen CLI & Certificate manager required",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Information);

                if (InstallCLI != MessageBoxResult.Yes)
                    return "User declined to install Tizen CLI.";

                installingWindow = new InstallingWindow();
                installingWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                installingWindow.Show();

                installingWindow.SetStatusText("DownloadingSetupFile".Localized());
                installerPath = await DownloadPackageAsync(Settings.Default.TizenCliUrl);
                string installPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "TizenStudioCli"
                );

                var startInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = $"--accept-license \"{_installPath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                installingWindow.SetStatusText("InstallingSetupFile".Localized());
                using var process = Process.Start(startInfo);
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                    return "Tizen CLI installation failed.";

                bool certInstalled = await InstallSamsungCertificateExtensionAsync(_installPath, installingWindow);

                if (!certInstalled)
                {
                    var certCrit = MessageBox.Show("There was a error during the installation of Tizen Certificate tooling! \r\nTry again?","Certificat Tooling Critical Error",MessageBoxButton.YesNo);
                    if (certCrit == MessageBoxResult.No)
                    {
                        Application.Current.Shutdown();
                    }
                    else
                    {
                        bool retryCertInstalled = await InstallSamsungCertificateExtensionAsync(_installPath, installingWindow);
                        if (!retryCertInstalled)
                        {
                            MessageBox.Show("Certificate Tooling installation failed again. Please try to install Tizen Studio manually.");
                            Application.Current.Shutdown();
                        }
                    }
                }
                    

                var tizenRoot = FindTizenRoot() ?? string.Empty;

                TizenCliPath = Path.Combine(tizenRoot, "tools", "ide", "bin", "tizen.bat");
                TizenSdbPath = Path.Combine(tizenRoot, "tools", "sdb.exe");

                return !string.IsNullOrEmpty(tizenRoot) ? TizenCliPath : "Tizen root folder not found after installation.";

            }
            catch (Exception ex)
            {
                return $"An error occurred during installation: {ex.Message}";
            }
            finally
            {
                {
                    // Ensure InstallingWindow is properly closed on UI thread
                    if (installingWindow != null)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            installingWindow.Close();
                        });
                    }

                    try
                    {
                        if (installerPath != null && File.Exists(installerPath))
                            File.Delete(installerPath);
                    }
                    catch { /* Ignore cleanup errors */ }
                }
            }
        }
        private async Task<string> SearchJellyfinApp()
        {
            string output = await RunCommandAsync(TizenSdbPath, "shell 0 vd_applist");

            var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Contains("app_id") && line.Contains("Jellyfin"))
                {
                    var match = Regex.Match(line, @"app_id\s*=\s*(\S+)");
                    if (match.Success)
                    {
                        var appId = match.Groups[1].Value;
                        return appId;
                    }
                }
            }

            return null;
        }


        public async Task<bool> RemoveJellyfinAppByIdAsync(string tvName, Action<string> updateStatus)
        {
            try
            {
                string appId = await SearchJellyfinApp();

                if (string.IsNullOrEmpty(appId))
                    return true;

                await RunCommandAsync(TizenCliPath, $"uninstall -t {tvName} -p {appId}");

                appId = await SearchJellyfinApp();

                if (!string.IsNullOrEmpty(appId))
                    return false;


                return true;
            }
            catch (Exception ex)
            {
                updateStatus($"Output: {ex.Message}".Localized());
                return false;
            }
        }
        public async Task EnsureTizenExtensionsEnabledAsync(string installPath, string packageManagerPath, InstallingWindow installingWindow)
        {
            installingWindow.SetStatusText("CheckingPackageManagerList".Localized());

            var output = await ElevatedCommands.RunElevatedAndCaptureOutputAsync(
                packageManagerPath,
                "extra --list --detail",
                installPath,
                installingWindow,
                "Querying Tizen extensions"
            );

            if (string.IsNullOrWhiteSpace(output))
            {
                installingWindow.SetStatusText("Failed to retrieve extension list.");
                throw new InvalidOperationException("Failed to get extension output.");
            }

            var extensions = ParseExtensions(output);
            var targets = new[] { "Samsung Certificate Extension", "Samsung Tizen TV SDK" };

            foreach (var target in targets)
            {
                var ext = extensions.FirstOrDefault(e => e.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
                if (ext == null)
                {
                    installingWindow.SetStatusText($"Extension '{target}' not found.");
                    continue;
                }

                if (ext.Activated)
                {
                    installingWindow.SetStatusText($"Extension '{target}' already active.");
                }
                else
                {
                    installingWindow.SetStatusText($"Activating extension: {target}...");

                    var activateProcess = new ProcessStartInfo
                    {
                        FileName = packageManagerPath,
                        Arguments = $"extra -act {ext.Index}",
                        UseShellExecute = true,
                        Verb = "runas", // elevation needed for activation
                        CreateNoWindow = false,
                        WorkingDirectory = installPath
                    };

                    using var proc = Process.Start(activateProcess);
                    if (proc == null)
                    {
                        throw new InvalidOperationException("Failed to start activation process");
                    }

                    await proc.WaitForExitAsync();

                    if (proc.ExitCode == 0)
                    {
                        installingWindow.SetStatusText($"Activated: {target}");
                    }
                    else
                    {
                        installingWindow.SetStatusText($"Failed to activate {target}. Exit code: {proc.ExitCode}");
                        throw new InvalidOperationException($"Failed to activate extension {target}. Exit code: {proc.ExitCode}");
                    }
                }
            }
        }
        public async Task<bool> InstallSamsungCertificateExtensionAsync(string installPath, InstallingWindow installingWindow)
        {
            string[] possiblePaths = {
                Path.Combine(installPath, "tools", "certificate-manager", "certificate-manager.exe"),
                Path.Combine(installPath, "certificate-manager", "certificate-manager.exe")
            };

            if (possiblePaths.Any(File.Exists))
                return true;

            string packageManagerPath = Path.Combine(installPath, "package-manager", "package-manager-cli.exe");
            if (!File.Exists(packageManagerPath))
            {
                MessageBox.Show("Package manager CLI not found. Please ensure Tizen Studio is properly installed.");
                return false;
            }

            await EnsureTizenExtensionsEnabledAsync(installPath, packageManagerPath, installingWindow);

            try
            {
                // First install Certificate-Manager
                var certManagerProcessInfo = new ProcessStartInfo
                {
                    FileName = packageManagerPath,
                    Arguments = "install \"Certificate-Manager\" --accept-license",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = installPath
                };

                installingWindow.SetStatusText("InstallingCertificateManager".Localized());

                using var certManagerProcess = Process.Start(certManagerProcessInfo);
                await certManagerProcess.WaitForExitAsync();

                if (certManagerProcess.ExitCode != 0)
                {
                    MessageBox.Show($"Certificate-Manager installation failed with exit code {certManagerProcess.ExitCode}");
                    return false;
                }

                // Then install cert-add-on package
                var processInfo = new ProcessStartInfo
                {
                    FileName = packageManagerPath,
                    Arguments = "install \"cert-add-on\" --accept-license",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = installPath
                };


                installingWindow.SetStatusText("InstallingCertificateAddOn".Localized());

                using var process = Process.Start(processInfo);
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    // Verify installation
                    if (possiblePaths.Any(File.Exists))
                    {
                        return true;
                    }
                    else
                    {
                        MessageBox.Show("Installation completed but certificate manager executable not found.");
                        return false;
                    }
                }
                else
                {
                    MessageBox.Show($"cert-add-on installation failed with exit code {process.ExitCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Samsung Certificate Extension installation failed: {ex.Message}");
                return false;
            }
        }
        public List<ExtensionEntry> ParseExtensions(string output)
        {
            var extensions = new List<ExtensionEntry>();
            var regex = new Regex(
                @"Index\s*:\s*(\d+)\s+Name\s*:\s*(.*?)\s+Repository\s*:\s*.*?\s+Id\s*:\s*.*?\s+Vendor\s*:\s*.*?\s+Description\s*:\s*.*?\s+Default\s*:\s*.*?\s+Activate\s*:\s*(true|false)",
                RegexOptions.Singleline);

            foreach (Match match in regex.Matches(output))
            {
                extensions.Add(new ExtensionEntry
                {
                    Index = int.Parse(match.Groups[1].Value),
                    Name = match.Groups[2].Value.Trim(),
                    Activated = bool.Parse(match.Groups[3].Value)
                });
            }

            return extensions;
        }
    }
}