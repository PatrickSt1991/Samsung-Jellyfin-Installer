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
                Debug.WriteLine("RATE LIMIT");
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
                var (alreadyInstalled, appId) = await CheckForInstalledApp(tvIpAddress, packageUrl);
                if (!canDelete && alreadyInstalled)
                {
                    progress?.Invoke("InstallationFailed".Localized());
                    return InstallResult.FailureResult($"{"alreadyInstalled".Localized()}");
                }
                
                if(canDelete && alreadyInstalled)
                {
                    if (_appSettings.DeletePreviousInstall)
                    {
                        progress?.Invoke("deleteExistingVersion".Localized());
                        await UninstallPackageAsync(tvIpAddress, appId);

                        var (stillInstalled, newAppId) = await CheckForInstalledApp(tvIpAddress, packageUrl);

                        if (stillInstalled)
                        {
                            progress?.Invoke("deleteExistingFailed".Localized());
                            return InstallResult.FailureResult($"{"deleteExistingFailed".Localized()}");
                        }

                        progress?.Invoke("deleteExistingSuccess".Localized());
                    }
                    else
                    {
                        progress?.Invoke("deleteExistingNotAllowed".Localized());
                        return InstallResult.FailureResult($"{"deleteExistingNotAllowed".Localized()}");
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

                string sdkToolPath = await FetchSdkPathAsync(tvIpAddress);

                if (string.IsNullOrEmpty(tizenOs))
                    tizenOs = "7.0";

                Version tizenVersion = new(tizenOs);
                Version certVersion = new("7.0");
                Version pushVersion = new("4.0");
                Version intermediateVersion = new("3.0");

                string authorp12 = string.Empty;
                string distributorp12 = string.Empty;
                string p12Password = string.Empty;
                string deviceXml = string.Empty;

                bool packageResign = false;

                if (tizenVersion >= certVersion || tizenVersion <= pushVersion || _appSettings.ConfigUpdateMode != "None" || _appSettings.ForceSamsungLogin)
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

                    if (tizenVersion <= pushVersion)
                    {
                        if(tizenVersion < intermediateVersion)
                            await AllowPermitInstall(tvIpAddress, Path.Combine(Path.GetDirectoryName(authorp12), "device-profile.xml"), "/home/developer");
                        else
                            await AllowPermitInstall(tvIpAddress, Path.Combine(Path.GetDirectoryName(authorp12), "device-profile.xml"), sdkToolPath);
                    }
                }

                if (!string.IsNullOrEmpty(_appSettings.JellyfinIP) && !_appSettings.ConfigUpdateMode.Contains("None"))
                {
                    string[] userIds = [];

                    if (_appSettings.JellyfinUserId == "everyone" && _appSettings.ConfigUpdateMode != "Server Settings")
                        userIds = [.. (await _jellyfinHelper.LoadJellyfinUsersAsync()).Select(u => u.Id)];
                    else
                        userIds = [_appSettings.JellyfinUserId];

                    if (_appSettings.ConfigUpdateMode.Contains("Server") ||
                        _appSettings.ConfigUpdateMode.Contains("Browser") ||
                        _appSettings.ConfigUpdateMode.Contains("All"))
                    {
                        await _jellyfinHelper.ApplyJellyfinConfigAsync(packageUrl, userIds);
                    }


                    if (_appSettings.ConfigUpdateMode.Contains("User") ||
                        _appSettings.ConfigUpdateMode.Contains("All"))
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
                var installResults = await InstallPackageAsync(tvIpAddress, packageUrl, sdkToolPath);

                if (installResults.Output.Contains("download failed[116]"))
                {
                    progress?.Invoke("InstallationFailed".Localized());
                    return InstallResult.FailureResult($"Installation failed: {"alreadyInstalled".Localized()}");
                }

                if(installResults.Output.Contains("install failed[118]"))
                {
                    await FileHelper.ModifyWgtPackageId(packageUrl);
                    await InstallPackageAsync(tvIpAddress, packageUrl, sdkToolPath);

                    progress?.Invoke("InstallationFailed".Localized());
                    return InstallResult.FailureResult($"Installation failed: {"modiyConfigRequired".Localized()}");
                }

                if (installResults.Output.Contains("failed"))
                {
                    progress?.Invoke("InstallationFailed".Localized());
                    return InstallResult.FailureResult($"Installation failed: {installResults.Output}");
                }

                if (installResults.Output.Contains("installing[100]") || installResults.Output.Contains("install completed"))
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

        private async Task<string> FetchSdkPathAsync(string tvIpAddress)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"capability {tvIpAddress}"); ;
            var match = Regex.Match(output.Output, @"sdk_toolpath:\s*([^\r\n]+)");
            return match.Success ? match.Groups[1].Value.Trim() : "/opt/usr/apps/tmp";
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
        private async Task<(bool isInstalled, string? Message)> CheckForInstalledApp(string tvIpAddress, string packageUrl)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"apps {tvIpAddress}");

            var baseSearch = Path.GetFileNameWithoutExtension(packageUrl).Split('-')[0];

            var blockRegex = new Regex(
                $@"(^\s*-+app_title\s*=\s*{Regex.Escape(baseSearch)}.*?)(?=^\s*-+app_title|\Z)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Multiline
            );

            var blockMatch = blockRegex.Match(output.Output);

            if (!blockMatch.Success)
                return (false, string.Empty);

            var block = blockMatch.Value;
            
            var appIdRegex = new Regex(@"app_id\s*=\s*([A-Za-z0-9._]+)", RegexOptions.IgnoreCase);
            var appIdMatch = appIdRegex.Match(block);

            string TVAppId = appIdMatch.Groups[1].Value.Trim();
            string PackageAppId = await FileHelper.ReadWgtPackageId(packageUrl);
            if (TVAppId == string.Concat(PackageAppId, ".Jellyfin"))
                return (true, TVAppId);
            else
                return (false, string.Empty);
        }

        private async Task<ProcessResult> ResignPackageAsync(string packagePath, string authorP12, string distributorP12, string certPass)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"resign \"{packagePath}\" \"{authorP12}\" \"{distributorP12}\" {certPass}");
            return output;
        }
        private async Task<ProcessResult> InstallPackageAsync(string tvIpAddress, string packagePath, string sdkToolPath)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"install {tvIpAddress} \"{packagePath}\" {sdkToolPath}");
            return output;
        }
        private async Task<ProcessResult> UninstallPackageAsync(string tvIpAddress, string packageId)
        {
            var output = await _processHelper.RunCommandAsync(TizenSdbPath!, $"uninstall {tvIpAddress} {packageId}");
            return output;
        }
        
        private async Task AllowPermitInstall(string tvIpAddress, string deviceXml, string sdkToolPath)
        {
            await _processHelper.RunCommandAsync(TizenSdbPath!, $"permit-install {tvIpAddress} \"{deviceXml}\" {sdkToolPath}");
            return;
        }
    }
}
