using Jellyfin2SamsungCrossOS.Extensions;
using Jellyfin2SamsungCrossOS.Helpers;
using Jellyfin2SamsungCrossOS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Jellyfin2SamsungCrossOS.Services
{
    public class TizenInstallerService : ITizenInstallerService
    {
        private static readonly string[] PossibleTizenPaths = GetPossibleTizenPaths();

        private static string[] GetPossibleTizenPaths()
        {
            var paths = new List<string>();

            if (OperatingSystem.IsWindows())
            {
                paths.Add(@"C:\TizenStudioCli");
                paths.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "TizenStudioCli"));
            }
            else if (OperatingSystem.IsMacOS())
            {
                paths.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "TizenStudioCli"));
            }
            else if (OperatingSystem.IsLinux())
            {
                paths.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".tizen-studio-cli"));
            }

            return paths.ToArray();
        }

        private readonly HttpClient _httpClient;
        private readonly IDialogService _dialogService;
        private readonly AppSettings _appSettings;
        private readonly JellyfinHelper _jellyfinHelper;
        private readonly OperatingSystemHelper _osHelper;
        private readonly ProcessHelper _processHelper;
        private readonly FileHelper _fileHelper;
        private readonly string _downloadDirectory;
        private string _installPath;
        private const int MaxSafePathLength = 240;

        public string? TizenRootPath { get; private set; }
        public string? TizenCliPath { get; private set; }
        public string? TizenSdbPath { get; private set; }
        public string? TizenCypto { get; private set; }
        public string? TizenPluginPath { get; private set; }
        public string? TizenDataPath { get; private set; }
        public string? PackageCertificate { get; set; }

        public TizenInstallerService(
            HttpClient httpClient, 
            IDialogService dialogService, 
            AppSettings appSettings,
            JellyfinHelper jellyfinHelper,
            OperatingSystemHelper osHelper,
            ProcessHelper processHelper,
            FileHelper fileHelper)
        {
            _httpClient = httpClient;
            _dialogService = dialogService;
            _appSettings = appSettings;
            _jellyfinHelper = jellyfinHelper;
            _osHelper = osHelper;
            _processHelper = processHelper;
            _fileHelper = fileHelper;

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");

            _downloadDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SamsungJellyfinInstaller",
                "Downloads");

            Directory.CreateDirectory(_downloadDirectory);

            DetermineInstallPath();
            InitializeTizenPaths();
        }

        private void InitializeTizenPaths()
        {
            string? tizenRoot = FindTizenRoot();

            if (tizenRoot is not null)
            {
                TizenRootPath = tizenRoot;

                // CLI launcher
                TizenCliPath = Path.Combine(
                    tizenRoot, "tools", "ide", "bin",
                    OperatingSystem.IsWindows() ? "tizen.bat" : "tizen"
                );

                // SDB
                TizenSdbPath = Path.Combine(
                    tizenRoot, "tools",
                    OperatingSystem.IsWindows() ? "sdb.exe" : "sdb"
                );

                // Crypto tool (Windows only)
                TizenCypto = OperatingSystem.IsWindows()
                    ? Path.Combine(tizenRoot, "tools", "certificate-encryptor", "wincrypt.exe")
                    : null; // no wincrypt on Linux/macOS

                // Plugins
                TizenPluginPath = Path.Combine(tizenRoot, "ide", "plugins");

                // Data path (profiles.xml)
                string tizenDataRoot = Path.Combine(
                    Path.GetDirectoryName(tizenRoot) ?? tizenRoot,
                    Path.GetFileName(tizenRoot) + "-data"
                );
                TizenDataPath = Path.Combine(tizenDataRoot, "profile", "profiles.xml");
            }
            else
            {
                TizenRootPath = null;
                TizenCliPath = null;
                TizenSdbPath = null;
                TizenCypto = null;
                TizenPluginPath = null;
                TizenDataPath = null;
            }
        }
        private void DetermineInstallPath()
        {
            string defaultPath;

            if (OperatingSystem.IsWindows())
            {
                defaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "TizenStudioCli"
                );
            }
            else if (OperatingSystem.IsMacOS())
            {
                defaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "TizenStudioCli"
                );
            }
            else // Linux
            {
                defaultPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".tizen-studio-cli"
                );
            }

            // Fallback only for Windows (path length issues)
            var fallbackPath = OperatingSystem.IsWindows()
                ? "C:\\TizenStudioCli"
                : defaultPath;

            if (defaultPath.Length > MaxSafePathLength)
            {
                _dialogService.ShowMessageAsync("Path length exceeded the safe limit. Using fallback path.").Wait();
                _installPath = fallbackPath;
            }
            else
            {
                _installPath = defaultPath;
            }
        }
        public async Task<(string?, string?)> EnsureTizenCliAvailable()
        {
            if (!string.IsNullOrWhiteSpace(TizenRootPath))
            {
                bool cliOk = File.Exists(TizenCliPath) && File.Exists(TizenSdbPath);

                // crypto tool only matters on Windows
                bool cryptoOk = OperatingSystem.IsWindows()
                    ? File.Exists(TizenCypto)
                    : true;

                string certManagerExe = OperatingSystem.IsWindows()
                    ? "certificate-manager.exe"
                    : "certificate-manager";

                string[] certManagerPaths = {
            Path.Combine(TizenRootPath, "certificate-manager", certManagerExe),
            Path.Combine(TizenRootPath, "tools", "certificate-manager", certManagerExe)
        };

                bool certManagerOk = certManagerPaths.Any(File.Exists);

                string certManagerPluginsPath = Path.Combine(TizenRootPath, "tools", "certificate-manager", "plugins");
                string idePluginsPath = Path.Combine(TizenRootPath, "ide", "plugins");

                bool certExtensionOk =
                    FolderHasCertJar(certManagerPluginsPath) ||
                    FolderHasCertJar(idePluginsPath);

                if (cliOk && cryptoOk && certManagerOk && certExtensionOk)
                    return (TizenDataPath, TizenCypto);
            }

            string tizenInstallationPath = await InstallMinimalCli();
            InitializeTizenPaths();
            return (tizenInstallationPath, TizenCypto);
        }
        private static bool FolderHasCertJar(string path)
        {
            if (!Directory.Exists(path))
                return false;

            return Directory.EnumerateFiles(path, "*.jar")
                .Any(file => Path.GetFileName(file)
                    .StartsWith("org.tizen.common.cert_", StringComparison.OrdinalIgnoreCase));
        }
        public async Task<bool> ConnectToTvAsync(string tvIpAddress)
        {
            if (TizenSdbPath is null)
                return false;

            try
            {
                var result = await _processHelper.RunCommandAsync(TizenSdbPath, $"connect {tvIpAddress}");
                return result.Contains($"connected to {tvIpAddress}");
            }
            catch
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
        
        public async Task<InstallResult> InstallPackageAsync(string packageUrl, string tvIpAddress, ProgressCallback? progress = null)
        {
            if (TizenCliPath is null || TizenSdbPath is null)
            {
                progress?.Invoke("PleaseInstallTizen".Localized());
                await _dialogService.ShowErrorAsync("PleaseInstallTizen".Localized());
                return InstallResult.FailureResult("PleaseInstallTizen".Localized());
            }

            try
            {
                progress?.Invoke("ConnectingToDevice".Localized());
                string tvName = await GetTvNameAsync(tvIpAddress);
                if (string.IsNullOrEmpty(tvName))
                {
                    progress?.Invoke("TvNameNotFound".Localized());
                    return InstallResult.FailureResult("TvNameNotFound".Localized());
                }

                string tvDuid = await GetTvDuidAsync();
                if (string.IsNullOrEmpty(tvDuid))
                {
                    progress?.Invoke("TvDuidNotFound".Localized());
                    return InstallResult.FailureResult("TvDuidNotFound".Localized());
                }

                string tizenOs = await FetchTizenOsAsync();
                if (string.IsNullOrEmpty(tizenOs))
                    tizenOs = "7.0";

                tizenOs = "7.0";
                if (new Version(tizenOs) >= new Version("7.0") || AppSettings.Default.ConfigUpdateMode != "None" || AppSettings.Default.ForceSamsungLogin)
                {
                    string selectedCertificate = _appSettings.Certificate;
                    
                    if (string.IsNullOrEmpty(selectedCertificate) || selectedCertificate == "Jelly2Sams (default)")
                    {
                        progress?.Invoke("SamsungLogin".Localized());;
                        SamsungAuth auth = await SamsungLoginService.PerformSamsungLoginAsync();
                        if (!string.IsNullOrEmpty(auth.access_token))
                        {
                            progress?.Invoke("CreatingCertificateProfile".Localized());
                            var certificateService = new TizenCertificateService(_httpClient, _dialogService);
                            (string p12Location, string p12Password) = await certificateService.GenerateProfileAsync(
                                duid: tvDuid,
                                accessToken: auth.access_token,
                                userId: auth.userId,
                                userEmail: auth.inputEmailID,
                                outputPath: Path.Combine(Environment.CurrentDirectory, "TizenProfile"),
                                TizenPluginPath ?? string.Empty,
                                progress
                            );

                            PackageCertificate = "Jelly2Sams";
                            _appSettings.Certificate = PackageCertificate;
                            _appSettings.Save();

                            UpdateCertificateManager(p12Location, p12Password, "Jelly2Sams");
                        }
                        else
                        {
                            await _dialogService.ShowErrorAsync("Failed to authenticate with Samsung account.");
                            return InstallResult.FailureResult("Auth failed.");
                        }
                    }
                    else
                    {
                        PackageCertificate = selectedCertificate;
                    }
                }
                else
                {
                    progress?.Invoke("UpdatingCertificateProfile".Localized());
                    UpdateCertificateManager("custom", "custom", "custom_jelly");
                    PackageCertificate = "custom_jelly";
                }

                if (!string.IsNullOrEmpty(AppSettings.Default.JellyfinIP) && !AppSettings.Default.ConfigUpdateMode.Contains("None"))
                {
                    string[] userIds = [];

                    if (AppSettings.Default.JellyfinUserId == "everyone" && (AppSettings.Default.ConfigUpdateMode != "Server Settings"))
                        userIds = [.. (await _jellyfinHelper.LoadJellyfinUsersAsync()).Select(u => u.Id)];
                    else
                        userIds = [AppSettings.Default.JellyfinUserId];

                    if (AppSettings.Default.ConfigUpdateMode.Contains("Server") ||
                        AppSettings.Default.ConfigUpdateMode.Contains("Browser") ||
                        AppSettings.Default.ConfigUpdateMode.Contains("All"))
                    {
                        await _jellyfinHelper.ApplyConfigAndResignPackageAsync(TizenCliPath, packageUrl, PackageCertificate, userIds);
                    }


                    if (AppSettings.Default.ConfigUpdateMode.Contains("User") ||
                        AppSettings.Default.ConfigUpdateMode.Contains("All"))
                    {
                        await _jellyfinHelper.UpdateJellyfinUsersAsync(userIds);
                    }

                }

                progress?.Invoke("packageAndSign".Localized());
                string packageExt = Path.GetExtension(packageUrl).TrimStart('.').ToLowerInvariant();
                await _processHelper.RunCommandAsync(TizenCliPath, $"package -t {packageExt} -s {PackageCertificate} -- \"{packageUrl}\"");

                progress?.Invoke("InstallingPackage".Localized());
                string installOutput = await _processHelper.RunCommandAsync(TizenCliPath, $"install -n \"{packageUrl}\" -t {tvName}");

                if (File.Exists(packageUrl) && !installOutput.Contains("Failed"))
                {
                    progress?.Invoke("InstallationSuccessful".Localized());
                    return InstallResult.SuccessResult();
                }

                progress?.Invoke("InstallationFailed".Localized());
                return InstallResult.FailureResult($"Output: {installOutput}");
            }
            catch (Exception ex)
            {
                progress?.Invoke($"Installation error: {ex.Message}");
                return InstallResult.FailureResult(ex.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(tvIpAddress))
                    await _processHelper.RunCommandAsync(TizenSdbPath, $"disconnect {tvIpAddress}");
            }
        }
        public async Task<string> GetTvNameAsync(string tvIpAddress)
        {
            if (TizenSdbPath is null)
                return string.Empty;

            await ConnectToTvAsync(tvIpAddress);
            var output = await _processHelper.RunCommandAsync(TizenSdbPath, "devices");
            var match = Regex.Match(output, @"(?<=\n)([^\s]+)\s+device\s+(?<name>[^\s]+)");
            return match.Success ? match.Groups["name"].Value.Trim() : string.Empty;
        }
        private async Task<string> FetchTizenOsAsync()
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath, "capability");
            var match = Regex.Match(output, @"platform_version:([\d.]+)");
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
        private async Task<string> GetTvDuidAsync()
        {
            if (TizenSdbPath is null) return string.Empty;
            string output = await _processHelper.RunCommandAsync(TizenSdbPath, "shell \"0 getduid\"");
            return string.IsNullOrWhiteSpace(output)
                ? (await _processHelper.RunCommandAsync(TizenSdbPath, "shell \"/opt/etc/duid-gadget 2 2> /dev/null\"")).Trim()
                : output.Trim();
        }
        private void UpdateCertificateManager(string p12Location, string p12Password, string profileName)
        {
            if (string.IsNullOrEmpty(TizenDataPath))
                throw new Exception("Tizen data path is not set.");

            XElement profile = new XElement("profile",
                new XAttribute("name", profileName),
                new XElement("profileitem",
                    new XAttribute("ca", ""),
                    new XAttribute("distributor", "0"),
                    new XAttribute("key", Path.Combine(p12Location, "author.p12")),
                    new XAttribute("password", p12Password),
                    new XAttribute("rootca", "")
                ),
                new XElement("profileitem",
                    new XAttribute("ca", ""),
                    new XAttribute("distributor", "1"),
                    new XAttribute("key", Path.Combine(p12Location, "distributor.p12")),
                    new XAttribute("password", p12Password),
                    new XAttribute("rootca", "")
                )
            );

            XDocument doc;
            string dir = Path.GetDirectoryName(TizenDataPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(TizenDataPath))
            {
                doc = new XDocument(new XElement("profiles", profile));
            }
            else
            {
                doc = XDocument.Load(TizenDataPath);
                var root = doc.Element("profiles")!;
                var existing = root.Elements("profile").FirstOrDefault(p => p.Attribute("name")?.Value == profileName);
                if (existing == null) root.Add(profile);
                else existing.ReplaceWith(profile);
            }

            doc.Save(TizenDataPath);
        }
        private static string? FindTizenRoot()
        {
            foreach (var basePath in PossibleTizenPaths)
            {
                if (string.IsNullOrEmpty(basePath))
                    continue;

                string tizenExecutable = OperatingSystem.IsWindows() ? "tizen.bat" : "tizen";

                var possiblePath = Path.Combine(basePath, "tools", "ide", "bin", tizenExecutable);
                if (File.Exists(possiblePath))
                    return basePath;
            }

            return null;
        }
        private async Task<string> InstallMinimalCli()
        {
            string installerPath = null;
            InstallingWindow installingWindow = null;

            try
            {
                string cliUrl = OperatingSystem.IsWindows() ? AppSettings.Default.TizenCliWindows :
                                OperatingSystem.IsLinux() ? AppSettings.Default.TizenCliLinux :
                                OperatingSystem.IsMacOS() ? AppSettings.Default.TizenCliMac :
                                throw new PlatformNotSupportedException("Unsupported OS");

                string installPath = _osHelper.GetInstallPath();

                var result = await _dialogService.ShowConfirmationAsync(
                    "minimalCliTitle".Localized(),
                    "minimalCliMessage".Localized(),
                    "keyContinue".Localized(),
                    "keyStop".Localized());

                if (!result)
                    return "minimalCliStop".Localized();

                installingWindow = new InstallingWindow
                {
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen
                };
                installingWindow.Show();

                
                installingWindow.ViewModel.StatusText = "Downloading Tizen CLI...";
                installerPath = await DownloadPackageAsync(cliUrl);

                if (!Directory.Exists(installPath))
                    Directory.CreateDirectory(installPath);

                installingWindow.ViewModel.StatusText = "Installing Tizen CLI...";

                if (OperatingSystem.IsWindows())
                {
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
                }
                else
                {
                    await _processHelper.RunCommandAsync("chmod", $"+x \"{installerPath}\"");
                    await _processHelper.RunCommandAsync("bash", $"\"{installerPath}\" --accept-license \"{installPath}\"");
                }

                installingWindow.ViewModel.StatusText = "Installing Tizen Certificate tooling...";
                bool certInstalled = await InstallSamsungCertificateExtensionAsync(installPath, installingWindow);

                if (!certInstalled)
                {
                    bool retry = await _dialogService.ShowConfirmationAsync(
                        "retryCertTitle".Localized(),
                        "retryCertMessage".Localized(),
                        "keyYes".Localized(),
                        "keyNo".Localized());

                    if (!retry)
                        return "certFailed".Localized();

                    certInstalled = await InstallSamsungCertificateExtensionAsync(installPath, installingWindow);
                    if (!certInstalled)
                        return "certRetryFailed".Localized();
                }

                var tizenRoot = FindTizenRoot() ?? string.Empty;
                TizenCliPath = OperatingSystem.IsWindows()
                    ? Path.Combine(tizenRoot, "tools", "ide", "bin", "tizen.bat")
                    : Path.Combine(tizenRoot, "tools", "ide", "bin", "tizen");

                TizenSdbPath = OperatingSystem.IsWindows()
                    ? Path.Combine(tizenRoot, "tools", "sdb.exe")
                    : Path.Combine(tizenRoot, "tools", "sdb");

                return !string.IsNullOrEmpty(tizenRoot) ? TizenCliPath : "Tizen root folder not found after installation.";
            }
            catch (Exception ex)
            {
                return $"An error occurred during installation: {ex.Message}";
            }
            finally
            {
                if (installingWindow != null)
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => installingWindow.Close());
            }
        }
        public async Task<bool> InstallSamsungCertificateExtensionAsync(string installPath, InstallingWindow installingWindow)
        {
            string certManagerExecutable = OperatingSystem.IsWindows()
                ? "certificate-manager.exe"
                : "certificate-manager";

            string[] possiblePaths = {
                Path.Combine(installPath, "tools", "certificate-manager", certManagerExecutable),
                Path.Combine(installPath, "certificate-manager", certManagerExecutable)
            };

            // Already installed?
            if (possiblePaths.Any(File.Exists))
                return true;

            string packageManagerExecutable = OperatingSystem.IsWindows()
                ? "package-manager-cli.exe"
                : "package-manager-cli";

            string packageManagerPath = Path.Combine(installPath, "package-manager", packageManagerExecutable);
            if (!File.Exists(packageManagerPath))
            {
                await _dialogService.ShowErrorAsync("Package manager CLI not found. Please ensure Tizen Studio is properly installed.");
                return false;
            }

            await EnsureTizenExtensionsEnabledAsync(installPath, packageManagerPath, installingWindow);

            try
            {
                // Install Certificate-Manager
                installingWindow.ViewModel.SetStatusText("Installing Certificate Manager...");
                string certArgs = OperatingSystem.IsWindows()
                    ? "install \"Certificate-Manager\" --accept-license"
                    : "install 'Certificate-Manager' --accept-license";

                await _processHelper.RunCommandAsync(packageManagerPath, certArgs, installPath);

                // Install cert-add-on package
                installingWindow.ViewModel.SetStatusText("Installing Certificate Add-On...");
                string addOnArgs = OperatingSystem.IsWindows()
                    ? "install \"cert-add-on\" --accept-license"
                    : "install 'cert-add-on' --accept-license";

                await _processHelper.RunCommandAsync(packageManagerPath, addOnArgs, installPath);

                // Verify installation
                if (possiblePaths.Any(File.Exists))
                    return true;

                await _dialogService.ShowErrorAsync("Installation completed but certificate manager executable not found.");
                return false;
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync($"Samsung Certificate Extension installation failed: {ex.Message}");
                return false;
            }
        }
        public async Task EnsureTizenExtensionsEnabledAsync(string installPath, string packageManagerPath, InstallingWindow installingWindow)
        {
            installingWindow.ViewModel.SetStatusText("CheckingPackageManagerList".Localized());

            var output = await _processHelper.RunCommandAsync(
                packageManagerPath,
                "extra --list --detail",
                installPath
            );

            if (string.IsNullOrWhiteSpace(output))
            {
                installingWindow.ViewModel.SetStatusText("Failed to retrieve extension list.");
                throw new InvalidOperationException("Failed to get extension output.");
            }

            var extensions = _fileHelper.ParseExtensions(output);
            var targets = new[] { "Samsung Certificate Extension", "Samsung Tizen TV SDK" };

            foreach (var target in targets)
            {
                var ext = extensions.FirstOrDefault(e => e.Name.Equals(target, StringComparison.OrdinalIgnoreCase));
                if (ext == null)
                {
                    installingWindow.ViewModel.SetStatusText($"Extension '{target}' not found.");
                    continue;
                }

                if (ext.Activated)
                {
                    installingWindow.ViewModel.SetStatusText($"Extension '{target}' already active.");
                }
                else
                {
                    installingWindow.ViewModel.SetStatusText($"Activating extension: {target}...");

                    var args = $"extra -act {ext.Index}";

                    var result = await _processHelper.RunCommandAsync(packageManagerPath, args, installPath);

                    if (result.Contains("activated", StringComparison.OrdinalIgnoreCase) ||
                        result.Contains("success", StringComparison.OrdinalIgnoreCase))
                    {
                        installingWindow.ViewModel.SetStatusText($"Activated: {target}");
                    }
                    else
                    {
                        installingWindow.ViewModel.SetStatusText($"Failed to activate {target}.");
                        throw new InvalidOperationException($"Failed to activate extension {target}. Output: {result}");
                    }
                }
            }
        }

    }
}
