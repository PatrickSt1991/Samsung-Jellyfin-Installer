using Jellyfin2Samsung.Extensions;
using Jellyfin2Samsung.Helpers;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Services
{
    public class TizenInstallerService : ITizenInstallerService
    {
        private readonly HttpClient _httpClient;
        private readonly IDialogService _dialogService;
        private readonly AppSettings _appSettings;
        private readonly JellyfinHelper _jellyfinHelper;
        private readonly ProcessHelper _processHelper;
        private readonly FileHelper _fileHelper;

        public string? TizenSdbPath { get; private set; }
        public string? PackageCertificate { get; set; }

        public TizenInstallerService(
            HttpClient httpClient, 
            IDialogService dialogService, 
            AppSettings appSettings,
            JellyfinHelper jellyfinHelper,
            ProcessHelper processHelper,
            FileHelper fileHelper)
        {
            _httpClient = httpClient;
            _dialogService = dialogService;
            _appSettings = appSettings;
            _jellyfinHelper = jellyfinHelper;
            _processHelper = processHelper;
            _fileHelper = fileHelper;

            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
        }

        public async Task<string> EnsureTizenSdbAvailable()
        {
            string tizenSdbPath = AppSettings.TizenSdbPath;

            var existingFile = Directory.GetFiles(tizenSdbPath, GetSearchPattern())
                .FirstOrDefault();

            var latestVersion = await GetLatestTizenSdbVersionAsync();

            if (existingFile != null && !ShouldUpdateBinary(existingFile, latestVersion))
            {
                TizenSdbPath = existingFile;
                return TizenSdbPath;
            }

            string downloadedFile = await DownloadTizenSdbAsync(latestVersion);

            if (existingFile != null && File.Exists(existingFile))
            {
                await _processHelper.MakeExecutableAsync(existingFile);
                File.Delete(existingFile);
            }
                

            string finalPath = Path.Combine(tizenSdbPath, GetFinalFileName(latestVersion));
            File.Move(downloadedFile, finalPath, true);

            await _processHelper.MakeExecutableAsync(finalPath);

            TizenSdbPath = finalPath;
            return TizenSdbPath;
        }

        private string GetSearchPattern()
        {
            if (OperatingSystem.IsWindows()) return "TizenSdb*.exe";
            if (OperatingSystem.IsLinux()) return "TizenSdb*_linux";
            if (OperatingSystem.IsMacOS()) return "TizenSdb*_macos";
            throw new PlatformNotSupportedException("Unsupported OS");
        }

        private string GetFinalFileName(string version)
        {
            return OperatingSystem.IsWindows() ? $"TizenSdb_{version}.exe" :
                   OperatingSystem.IsLinux() ? $"TizenSdb_{version}_linux" :
                   OperatingSystem.IsMacOS() ? $"TizenSdb_{version}_macos" :
                   throw new PlatformNotSupportedException("Unsupported OS");
        }

        private bool ShouldUpdateBinary(string existingFilePath, string latestVersion)
        {
            try
            {
                // Extract version from filename (e.g., "TizenSdb_v1.0.1.exe" -> "v1.0.1")
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(existingFilePath);
                var match = System.Text.RegularExpressions.Regex.Match(fileNameWithoutExtension, @"_([v]?\d+\.\d+\.\d+)");

                if (!match.Success)
                    return true; // If we can't parse version, update to be safe

                string currentVersion = match.Groups[1].Value;
                return IsVersionGreater(latestVersion, currentVersion);
            }
            catch
            {
                return true; // If anything fails, update to be safe
            }
        }

        private bool IsVersionGreater(string latestVersion, string currentVersion)
        {
            // Remove 'v' prefix if present for comparison
            var latest = Version.TryParse(latestVersion.TrimStart('v'), out var latestVer) ? latestVer : null;
            var current = Version.TryParse(currentVersion.TrimStart('v'), out var currentVer) ? currentVer : null;

            if (latest == null || current == null)
                return false;

            return latest > current;
        }

        private async Task<string> GetLatestTizenSdbVersionAsync()
        {
            var json = await _httpClient.GetStringAsync(AppSettings.Default.TizenSdb);
            var releases = JsonConvert.DeserializeObject<List<GitHubRelease>>(json);
            var firstRelease = releases.FirstOrDefault();

            if (firstRelease == null)
                throw new InvalidOperationException("No releases found");

            return firstRelease.TagName ?? "v1.0.0";
        }

        public async Task<string> DownloadTizenSdbAsync(string version = null)
        {
            var json = await _httpClient.GetStringAsync(AppSettings.Default.TizenSdb);
            var releases = JsonConvert.DeserializeObject<List<GitHubRelease>>(json);
            var firstRelease = releases.FirstOrDefault();

            if (firstRelease == null)
                throw new InvalidOperationException("No releases found");

            string nameMatch =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "exe" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
                throw new PlatformNotSupportedException();

            var matchedAsset = firstRelease.Assets
                .FirstOrDefault(a => !string.IsNullOrEmpty(a.FileName) &&
                                     a.FileName.Contains(nameMatch, StringComparison.OrdinalIgnoreCase));

            if (matchedAsset == null)
                throw new InvalidOperationException($"No matching asset found for {nameMatch}");

            return await DownloadPackageAsync(matchedAsset.DownloadUrl);
        }

        public async Task<string> DownloadPackageAsync(string downloadUrl)
        {
            var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
            var localPath = Path.Combine(AppSettings.DownloadPath, fileName);

            if (File.Exists(localPath))
                return localPath;

            Directory.CreateDirectory(AppSettings.DownloadPath);

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);

            await contentStream.CopyToAsync(fileStream);

            return localPath;
        }
        public async Task<InstallResult> InstallPackageAsync(string packageUrl, string tvIpAddress, ProgressCallback? progress = null)
        {
            if (TizenSdbPath is null)
            {
                progress?.Invoke("InstallTizenSdb".Localized());
                await EnsureTizenSdbAvailable();

                if(TizenSdbPath is null)
                { 
                    await _dialogService.ShowErrorAsync("FailedTizenSdb".Localized());
                    return InstallResult.FailureResult("InstallTizenSdb".Localized());
                }
            }

            try
            {

                progress?.Invoke("diagnoseTv".Localized());
                bool canDelete = await GetTvDiagnoseAsync(tvIpAddress);

                if (!canDelete)
                {
                    progress?.Invoke("diagnoseTv".Localized());
                    string appId = await CheckForInstalledApp(tvIpAddress, Path.GetFileNameWithoutExtension(packageUrl));

                    if (!string.IsNullOrEmpty(appId))
                    {
                        progress?.Invoke("InstallationFailed".Localized());
                        return InstallResult.FailureResult($"Installation failed: {"alreadyInstalled".Localized()}");
                    }
                }
                
                progress?.Invoke("ConnectingToDevice".Localized());
                string tvName = await GetTvNameAsync(tvIpAddress);
                if (string.IsNullOrEmpty(tvName))
                {
                    progress?.Invoke("TvNameNotFound".Localized());
                    return InstallResult.FailureResult("TvNameNotFound".Localized());
                }

                string tvDuid = await GetTvDuidAsync(tvIpAddress);
                if (string.IsNullOrEmpty(tvDuid))
                {
                    progress?.Invoke("TvDuidNotFound".Localized());
                    return InstallResult.FailureResult("TvDuidNotFound".Localized());
                }

                string tizenOs = await FetchTizenOsAsync(tvIpAddress);

                if (string.IsNullOrEmpty(tizenOs))
                    tizenOs = "7.0";

                Version tizenVersion = new(tizenOs);
                Version certVersion = new("7.0");
                Version oldVersion = new("4.0");

                string authorp12 = string.Empty;
                string distributorp12 = string.Empty;
                string p12Password = string.Empty;
                string deviceXml = string.Empty;

                bool packageResign = false;
                
                if (tizenVersion >= certVersion || tizenVersion <= oldVersion || AppSettings.Default.ConfigUpdateMode != "None" || AppSettings.Default.ForceSamsungLogin)
                {
                    packageResign = true;
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
                            (authorp12, distributorp12, p12Password) = await certificateService.GenerateProfileAsync(
                                duid: tvDuid,
                                accessToken: auth.access_token,
                                userId: auth.userId,
                                userEmail: auth.inputEmailID,
                                outputPath: Path.Combine(AppSettings.CertificatePath, "Jelly2Sams"),
                                progress
                            );
                            
                            PackageCertificate = "Jelly2Sams";
                            _appSettings.Certificate = PackageCertificate;
                            _appSettings.Save();
                        }
                        else
                        {
                            await _dialogService.ShowErrorAsync("Failed to authenticate with Samsung account.");
                            return InstallResult.FailureResult("Auth failed.");
                        }
                    }
                    else
                    {
                        authorp12 = Path.Combine(Path.GetDirectoryName(_appSettings.ChosenCertificates.File),"author.p12");
                        distributorp12 = Path.Combine(Path.GetDirectoryName(_appSettings.ChosenCertificates.File), "distributor.p12");
                        p12Password = File.ReadAllText(Path.Combine(Path.GetDirectoryName(_appSettings.ChosenCertificates.File), "password.txt")).Trim();
                        
                        PackageCertificate = selectedCertificate;
                    }

                    if (tizenVersion <= oldVersion)
                        await AllowPermitInstall(tvIpAddress, Path.Combine(Path.GetDirectoryName(authorp12), "device-profile.xml"));
                }

                if (!string.IsNullOrEmpty(AppSettings.Default.JellyfinIP) && !AppSettings.Default.ConfigUpdateMode.Contains("None"))
                {
                    string[] userIds = [];

                    if (AppSettings.Default.JellyfinUserId == "everyone" && AppSettings.Default.ConfigUpdateMode != "Server Settings")
                        userIds = [.. (await _jellyfinHelper.LoadJellyfinUsersAsync()).Select(u => u.Id)];
                    else
                        userIds = [AppSettings.Default.JellyfinUserId];

                    if (AppSettings.Default.ConfigUpdateMode.Contains("Server") ||
                        AppSettings.Default.ConfigUpdateMode.Contains("Browser") ||
                        AppSettings.Default.ConfigUpdateMode.Contains("All"))
                    {
                        await _jellyfinHelper.ApplyJellyfinConfigAsync(packageUrl, userIds);
                    }


                    if (AppSettings.Default.ConfigUpdateMode.Contains("User") ||
                        AppSettings.Default.ConfigUpdateMode.Contains("All"))
                    {
                        await _jellyfinHelper.UpdateJellyfinUsersAsync(userIds);
                    }

                }

                if (packageResign)
                {
                    progress?.Invoke("packageAndSign".Localized());
                    var resignResults = await ResignPackageAsync(packageUrl, authorp12, distributorp12, p12Password);

                    if (resignResults.ExitCode != 0 || resignResults.Output.Contains("Re-sign failed"))
                    {
                        progress?.Invoke("InstallationFailed".Localized());
                        return InstallResult.FailureResult($"Package resigning failed: {resignResults.Output}");
                    }
                }
                
                progress?.Invoke("InstallingPackage".Localized());
                var installResults = await InstallPackageAsync(tvIpAddress, packageUrl);

                if (installResults.Output.Contains("download failed[116]"))
                {
                    progress?.Invoke("InstallationFailed".Localized());
                    return InstallResult.FailureResult($"Installation failed: {"alreadyInstalled".Localized()}");
                }

                if(installResults.Output.Contains("install failed[118, -22]"))
                {
                    bool modAppId = await FileHelper.ModifyWgtPackageId(packageUrl);

                    var installResultsRetry = await InstallPackageAsync(tvIpAddress, packageUrl);

                    progress?.Invoke("InstallationFailed".Localized());
                    return InstallResult.FailureResult($"Installation failed: {"modiyConfigRequired".Localized()}");
                }

                if (installResults.Output.Contains("failed"))
                {
                    progress?.Invoke("InstallationFailed".Localized());
                    return InstallResult.FailureResult($"Installation failed: {installResults.Output}");
                }

                if (installResults.Output.Contains("installing[100]"))
                {
                    progress?.Invoke("InstallationSuccessful".Localized());
                    return InstallResult.SuccessResult();
                }

                progress?.Invoke("InstallationFailed".Localized());
                return InstallResult.FailureResult($"Installation failed: {installResults.Output}");
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
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"devices {tvIpAddress}");
            var deviceName = output.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim() ?? string.Empty;

            return deviceName;
        }
        private async Task<string> FetchTizenOsAsync(string tvIpAddress)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"capability {tvIpAddress}");;
            var match = Regex.Match(output.Output, @"platform_version:\s*([\d.]+)");
            return match.Success ? match.Groups[1].Value.Trim() : "";
        }
        private async Task<string> GetTvDuidAsync(string tvIpAddress)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"duid {tvIpAddress}");
            var duid = output.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()?.Trim() ?? string.Empty;

            return duid;
        }
        private async Task<bool> GetTvDiagnoseAsync(string tvIpAddress)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"diagnose {tvIpAddress}");
            string text = output.Output;

            var match = Regex.Match(text,@"Testing '0 vd_appuninstall test':\s*FAILED",RegexOptions.IgnoreCase);

            return !match.Success;
        }
        private async Task<string> CheckForInstalledApp(string tvIpAddress, string searchTerm)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"apps {tvIpAddress}");

            var baseSearch = searchTerm.Split('-')[0];

            var blockRegex = new Regex(
                $@"(^-+app_title\s*=\s*{Regex.Escape(baseSearch)}.*?)(?=^-+app_title|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline
            );


            var blockMatch = blockRegex.Match(output.Output);

            if (!blockMatch.Success)
                return "";

            var block = blockMatch.Value;

            var appIdRegex = new Regex(@"app_id\s*=\s*([^\s-]+)", RegexOptions.IgnoreCase);
            var appIdMatch = appIdRegex.Match(block);

            return appIdMatch.Success ? appIdMatch.Groups[1].Value.Trim() : "";
        }

        private async Task<ProcessResult> ResignPackageAsync(string packagePath, string authorP12, string distributorP12, string certPass)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"resign \"{packagePath}\" \"{authorP12}\" \"{distributorP12}\" {certPass}");
            return output;
        }
        private async Task<ProcessResult> InstallPackageAsync(string tvIpAddress, string packagePath)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"install {tvIpAddress} \"{packagePath}\"");
            return output;
        }
        
        private async Task AllowPermitInstall(string tvIpAddress, string deviceXml)
        {
            await _processHelper.RunCommandAsync(TizenSdbPath!, $"permit-install {tvIpAddress} {deviceXml}");
            return;
        }
    }
}
