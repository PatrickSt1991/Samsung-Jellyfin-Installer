using Avalonia.Threading;
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
                    "TizenStudioCli"));
            }
            else if (OperatingSystem.IsLinux())
            {
                paths.Add(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "tizen-studio-cli"));
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
                    "tizen-studio-cli"
                );
            }

            // Fallback only for Windows (path length issues)
            var fallbackPath = OperatingSystem.IsWindows()
                ? "C:\\TizenStudioCli"
                : defaultPath;

            if (defaultPath.Length > MaxSafePathLength)
            {
                _dialogService.ShowMessageAsync("Path length exceeded", "Path length exceeded the safe limit. Using fallback path.").Wait();
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
                return result.Output.Contains($"connected to {tvIpAddress}");
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

            if (File.Exists(localPath))
                return localPath;

            Directory.CreateDirectory(_downloadDirectory);

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);

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

                Version tizenVersion = new(tizenOs);
                Version certVersion = new("7.0");
                Version oldVersion = new("4.0");

                if (tizenVersion <= oldVersion)
                {
                    if (!string.IsNullOrEmpty(tvName))
                    {
                        AppSettings.Default.PermitInstall = true;
                        allowPermitInstall(tvName);
                    }
                }

                if (tizenVersion >= certVersion || AppSettings.Default.ConfigUpdateMode != "None" || AppSettings.Default.ForceSamsungLogin)
                {
                    string selectedCertificate = _appSettings.Certificate;
                    var certDuid = _appSettings.ChosenCertificates?.Duid;

                    if (string.IsNullOrEmpty(selectedCertificate) || selectedCertificate == "Jelly2Sams (default)" || tvDuid != certDuid)
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
                                outputPath: Path.Combine(Environment.CurrentDirectory, "Assets", "TizenProfile"),
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

                Debug.WriteLine($"Jellyfin IP: {AppSettings.Default.JellyfinIP}");
                Debug.WriteLine($"Update mode: {AppSettings.Default.ConfigUpdateMode}");
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
                if(OperatingSystem.IsWindows())
                    await _processHelper.RunCommandCmdAsync(TizenCliPath, $"package -t {packageExt} -s {PackageCertificate} -- \"{packageUrl}\"");
                else
                    await _processHelper.RunCommandAsync(TizenCliPath, $"package -t {packageExt} -s {PackageCertificate} -- \"{packageUrl}\"");

                progress?.Invoke("InstallingPackage".Localized());
                var installOutput = new ProcessResult();
                if (OperatingSystem.IsWindows())
                    installOutput = await _processHelper.RunCommandCmdAsync(TizenCliPath, $"install -n \"{packageUrl}\" -t {tvName}");
                else
                    installOutput = await _processHelper.RunCommandAsync(TizenCliPath, $"install -n \"{packageUrl}\" -t {tvName}");

                if (File.Exists(packageUrl) && !installOutput.Output.Contains("Failed"))
                {
                    progress?.Invoke("InstallationSuccessful".Localized());
                    return InstallResult.SuccessResult();
                }

                progress?.Invoke("InstallationFailed".Localized());
                return InstallResult.FailureResult($"Installation failed: {installOutput.Output}");
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
            var match = Regex.Match(output.Output, @"(?<=\n)([^\s]+)\s+device\s+(?<name>[^\s]+)");
            return match.Success ? match.Groups["name"].Value.Trim() : string.Empty;
        }
        private async Task<string> FetchTizenOsAsync()
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath, "capability");
            var match = Regex.Match(output.Output, @"platform_version:([\d.]+)");
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
        private async Task<string> GetTvDuidAsync()
        {
            if (TizenSdbPath is null) return string.Empty;
            var output = await _processHelper.RunCommandAsync(TizenSdbPath, "shell \"0 getduid\"");
            var result = string.IsNullOrWhiteSpace(output.Output)
                ? await _processHelper.RunCommandAsync(TizenSdbPath, "shell \"/opt/etc/duid-gadget 2 2> /dev/null\"")
                : output;

            return result.Output.Trim();

        }

        private async Task allowPermitInstall(string tvName)
        {
            await _processHelper.RunCommandAsync(TizenCliPath, $"install-permit -t {tvName}");
            return;

        }

        private void UpdateCertificateManager(string p12Location, string p12Password, string profileName)
        {
            void Trace(string m) => Debug.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [PROFILES] {m}");
            var swTotal = Stopwatch.StartNew();

            Trace($"ENTER name='{profileName}', TizenDataPath='{TizenDataPath}'");
            if (string.IsNullOrEmpty(TizenDataPath))
                throw new Exception("Tizen data path is not set.");


            string dir = Path.GetDirectoryName(TizenDataPath)!;
            Trace($"Ensure dir exists: '{dir}' (exists={Directory.Exists(dir)})");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                Trace("Created dir.");
            }

            XDocument doc;
            XElement root;

            if (!File.Exists(TizenDataPath))
            {
                Trace("profiles.xml NOT found. Creating new XDocument with root + attrs.");
                root = new XElement("profiles",
                    new XAttribute("active", profileName),
                    new XAttribute("version", "3.1"));
                doc = new XDocument(new XDeclaration("1.0", "utf-8", "no"), root);
            }
            else
            {
                Trace("profiles.xml found. Loading XDocument...");
                doc = XDocument.Load(TizenDataPath);
                Trace("Loaded XDocument.");
                root = doc.Element("profiles") ?? new XElement("profiles");
                if (doc.Root == null)
                {
                    Trace("doc.Root was null, adding 'profiles' root.");
                    doc.Add(root);
                }

                if (root.Attribute("version") == null) { Trace("Setting version attr."); root.SetAttributeValue("version", "3.1"); }
                if (root.Attribute("active") == null) { Trace("Setting active attr."); root.SetAttributeValue("active", profileName); }
            }

            // Normalize p12 paths
            Trace($"Normalize p12 paths. p12Location='{p12Location}'");
            string authorP12 = p12Location.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)
                ? p12Location
                : Path.Combine(p12Location, "author.p12");

            string distributorP12 = p12Location.EndsWith(".p12", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(Path.GetDirectoryName(p12Location)!, "distributor.p12")
                : Path.Combine(p12Location, "distributor.p12");

            Trace($"authorP12='{authorP12}', distributorP12='{distributorP12}'");

            Trace("Building <profile> element...");
            var profile = new XElement("profile",
                new XAttribute("name", profileName),
                new XElement("profileitem",
                    new XAttribute("ca", ""),
                    new XAttribute("distributor", "0"),
                    new XAttribute("key", authorP12),
                    new XAttribute("password", p12Password),
                    new XAttribute("rootca", "")
                ),
                new XElement("profileitem",
                    new XAttribute("ca", ""),
                    new XAttribute("distributor", "1"),
                    new XAttribute("key", distributorP12),
                    new XAttribute("password", p12Password),
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
            Trace("Built profile element.");

            // Insert / Replace
            Trace("Searching for existing profile...");
            var existing = root.Elements("profile").FirstOrDefault(p => (string?)p.Attribute("name") == profileName);
            if (existing is null)
            {
                Trace("Existing profile NOT found. Adding new.");
                root.Add(profile);
            }
            else
            {
                Trace("Existing profile found. Replacing.");
                existing.ReplaceWith(profile);
            }

            Trace("Setting 'active' attribute on root...");
            root.SetAttributeValue("active", profileName);

            // Save
            Trace($"Saving XDocument to '{TizenDataPath}'...");
            var swSave = Stopwatch.StartNew();
            doc.Save(TizenDataPath);
            swSave.Stop();
            Trace($"Saved profiles.xml in {swSave.ElapsedMilliseconds} ms.");

            swTotal.Stop();
            Trace($"EXIT after {swTotal.ElapsedMilliseconds} ms.");
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
                // 1️⃣ Determine CLI URL
                string cliUrl = OperatingSystem.IsWindows() ? AppSettings.Default.TizenCliWindows :
                                OperatingSystem.IsLinux() ? AppSettings.Default.TizenCliLinux :
                                OperatingSystem.IsMacOS() ? AppSettings.Default.TizenCliMac :
                                throw new PlatformNotSupportedException("Unsupported OS");

                string installPath = _osHelper.GetInstallPath();

                // 2️⃣ Ask user for confirmation
                bool userConfirmed = await _dialogService.ShowConfirmationAsync(
                    "minimalCliTitle".Localized(),
                    "minimalCliMessage".Localized(),
                    "keyContinue".Localized(),
                    "keyStop".Localized());

                if (!userConfirmed)
                    return "minimalCliStop".Localized();

                // 3️⃣ Show installing window
                installingWindow = new InstallingWindow
                {
                    WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterScreen
                };
                installingWindow.Show();

                // 4️⃣ Download installer
                installingWindow.ViewModel.StatusText = "Downloading Tizen CLI...";
                

                installerPath = await DownloadPackageAsync(cliUrl);

                if (!Directory.Exists(installPath))
                    Directory.CreateDirectory(installPath);

                // 5️⃣ Install Tizen CLI
                
                installingWindow.ViewModel.StatusText = "Installing Tizen CLI...";
                

                bool cliInstalled = false;

                try
                {
                    if (OperatingSystem.IsWindows())
                    {
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = installerPath,
                            Arguments = $"--accept-license \"{installPath}\"",
                            UseShellExecute = true,
                            CreateNoWindow = false,
                            Verb = "runas"
                        };

                        using var process = Process.Start(startInfo);
                        await process.WaitForExitAsync();
                        
                        cliInstalled = process.ExitCode == 0;
                    }
                    else
                    {
                        await _processHelper.RunCommandAsync("chmod", $"+x \"{installerPath}\"");
                        var result = await _processHelper.RunCommandAsync("bash", $"\"{installerPath}\" --accept-license \"{installPath}\"");
                        cliInstalled = result.ExitCode == 0;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Tizen CLI installation failed: {ex}");
                }

                if (!cliInstalled)
                    return "Tizen CLI installation failed.";

                installingWindow.ViewModel.StatusText = "Installing Tizen Certificate tooling...";

                bool certInstalled = await InstallSamsungCertificateExtensionAsync(installPath, installingWindow);

                if (!certInstalled)
                {
                    bool retry = await _dialogService.ShowConfirmationAsync(
                        "InstallationFailed".Localized(),
                        "ReInstallingCertificateManager".Localized(),
                        "keyYes".Localized(),
                        "keyNo".Localized(),
                        owner: installingWindow
                    );

                    if (!retry)
                        return "certFailed".Localized();

                    certInstalled = await InstallSamsungCertificateExtensionAsync(installPath, installingWindow);
                    if (!certInstalled)
                        return "certRetryFailed".Localized();
                }

                // 8️⃣ Set Tizen paths
                var tizenRoot = FindTizenRoot() ?? string.Empty;
                TizenCliPath = OperatingSystem.IsWindows()
                    ? Path.Combine(tizenRoot, "tools", "ide", "bin", "tizen.bat")
                    : Path.Combine(tizenRoot, "tools", "ide", "bin", "tizen");

                TizenSdbPath = OperatingSystem.IsWindows()
                    ? Path.Combine(tizenRoot, "tools", "sdb.exe")
                    : Path.Combine(tizenRoot, "tools", "sdb");

                return !string.IsNullOrEmpty(tizenRoot)
                    ? TizenCliPath
                    : "Tizen root folder not found after installation.";
            }
            catch (Exception ex)
            {
                return $"An error occurred during installation: {ex.Message}";
            }
            finally
            {
                if (installingWindow != null)
                    await Dispatcher.UIThread.InvokeAsync(() => installingWindow.Close());
            }
        }

        public async Task<bool> InstallSamsungCertificateExtensionAsync(string installPath, InstallingWindow installingWindow)
        {
            string certManagerExe = OperatingSystem.IsWindows() ? "certificate-manager.exe" : "certificate-manager.bin";
            string[] possiblePaths = {
                Path.Combine(installPath, "tools", "certificate-manager", certManagerExe),
                Path.Combine(installPath, "certificate-manager", certManagerExe)
            };

            // Already installed?
            if (possiblePaths.Any(File.Exists))
                return true;

            string packageManagerExe = OperatingSystem.IsWindows() ? "package-manager-cli.exe" : "package-manager-cli.bin";
            string packageManagerPath = Path.Combine(installPath, "package-manager", packageManagerExe);

            if (!File.Exists(packageManagerPath))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    installingWindow.ViewModel.SetStatusText("Package manager CLI not found. Please ensure Tizen Studio is properly installed.")
                );
                return false;
            }

            await EnsureTizenExtensionsEnabledAsync(installPath, packageManagerPath, installingWindow);

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // ---- Certificate Manager ----
                    installingWindow.ViewModel.SetStatusText("Installing Certificate Manager...");
                    var certProcessInfo = new ProcessStartInfo
                    {
                        FileName = packageManagerPath,
                        Arguments = "install \"Certificate-Manager\" --accept-license",
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WorkingDirectory = installPath
                    };

                    using var certProcess = Process.Start(certProcessInfo);
                    await certProcess.WaitForExitAsync();
                    if (certProcess.ExitCode != 0)
                        return false;

                    // ---- Cert Add-On ----
                    installingWindow.ViewModel.SetStatusText("Installing Certificate Add-On...");
                    var addOnProcessInfo = new ProcessStartInfo
                    {
                        FileName = packageManagerPath,
                        Arguments = "install \"cert-add-on\" --accept-license",
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WorkingDirectory = installPath
                    };

                    using var addOnProcess = Process.Start(addOnProcessInfo);
                    await addOnProcess.WaitForExitAsync();
                    if (addOnProcess.ExitCode != 0)
                        return false;
                }
                else
                {
                    // Linux/macOS CLI-based installation
                    installingWindow.ViewModel.SetStatusText("Installing Certificate Manager...");
                    await _processHelper.RunCommandAsync(packageManagerPath, "install Certificate-Manager --accept-license");

                    installingWindow.ViewModel.SetStatusText("Installing Certificate Add-On...");
                    await _processHelper.RunCommandAsync(packageManagerPath, "install cert-add-on --accept-license");
                }

                // Verify installation
                if (possiblePaths.Any(File.Exists))
                    return true;

                await Dispatcher.UIThread.InvokeAsync(() =>
                    installingWindow.ViewModel.SetStatusText("Installation completed but certificate manager executable not found.")
                );
                return false;
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                    installingWindow.ViewModel.SetStatusText($"Samsung Certificate Extension installation failed: {ex.Message}")
                );
                return false;
            }
        }
        public async Task EnsureTizenExtensionsEnabledAsync(string installPath, string packageManagerPath, InstallingWindow installingWindow)
        {
            installingWindow.ViewModel.SetStatusText("CheckingPackageManagerList".Localized());

            var result = OperatingSystem.IsWindows()
                ? await _processHelper.RunElevatedAndCaptureOutputAsync(packageManagerPath, "extra --list --detail", installPath)
                : await _processHelper.RunCommandAsync(packageManagerPath, "extra --list --detail", installPath);

            string output = result?.Output ?? string.Empty;

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

                    var activationResult = OperatingSystem.IsWindows()
                        ? await _processHelper.RunElevatedAndCaptureOutputAsync(packageManagerPath, args, installPath)
                        : await _processHelper.RunCommandAsync(packageManagerPath, args, installPath);

                    string activationOutput = activationResult?.Output ?? string.Empty;


                    if (activationOutput.Contains("activated", StringComparison.OrdinalIgnoreCase) ||
                        activationOutput.Contains("success", StringComparison.OrdinalIgnoreCase))
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
