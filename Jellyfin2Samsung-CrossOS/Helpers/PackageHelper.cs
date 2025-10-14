using Jellyfin2Samsung.Extensions;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Models;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class PackageHelper
    {
        private readonly ITizenInstallerService _tizenInstaller;
        private readonly IDialogService _dialogService;
        private readonly INetworkService _networkService;

        public PackageHelper(
            ITizenInstallerService tizenInstaller,
            IDialogService dialogService,
            INetworkService networkService)
        {
            _tizenInstaller = tizenInstaller;
            _dialogService = dialogService;
            _networkService = networkService;
        }

        public async Task<string?> DownloadReleaseAsync(GitHubRelease release, Asset selectedAsset, ProgressCallback? progress = null)
        {
            if (release?.PrimaryDownloadUrl == null) return null;

            try
            {
                string downloadPath = await _tizenInstaller.DownloadPackageAsync(selectedAsset.DownloadUrl);
                progress?.Invoke("DownloadCompleted".Localized());
                return downloadPath;
            }
            catch (Exception ex)
            {
                progress?.Invoke("DownloadFailed".Localized());
                await _dialogService.ShowErrorAsync($"{"DownloadFailed".Localized()} {ex.Message}");
                return null;
            }
        }

        public async Task<bool> InstallPackageAsync(string packagePath, NetworkDevice selectedDevice, ProgressCallback? progress = null)
        {
            var localIps = _networkService.GetRelevantLocalIPs()
                              .Select(ip => ip.ToString())
                              .ToList();

            bool ipMismatch = !localIps.Contains(selectedDevice.DeveloperIP);

            if (string.IsNullOrEmpty(packagePath) || !File.Exists(packagePath))
            {
                progress?.Invoke("NoPackageToInstall".Localized());
                await _dialogService.ShowErrorAsync("NoPackageToInstall".Localized());
                return false;
            }

            if (string.IsNullOrWhiteSpace(selectedDevice?.IpAddress))
            {
                progress?.Invoke("NoDeviceSelected".Localized());
                await _dialogService.ShowErrorAsync("NoDeviceSelected".Localized());
                return false;
            }

            if (selectedDevice.DeveloperMode == "0")
            {
                bool devmodeExecution = await _dialogService.ShowConfirmationAsync("Developer Disabled", "DeveloperModeRequired".Localized(), "keyContinue".Localized(), "keyStop".Localized());
                if (!devmodeExecution)
                    return false;
            }

            if (ipMismatch && AppSettings.Default.RTLReading)
            {
                ipMismatch = !localIps
                    .Select(ip => _networkService.InvertIPAddress(ip))
                    .Contains(selectedDevice.DeveloperIP);

                if (!ipMismatch)
                    selectedDevice.IpAddress = selectedDevice.DeveloperIP;
            }

            if (ipMismatch)
            {
                bool continueExecution = await _dialogService.ShowConfirmationAsync("IP Mismatch","DeveloperIPMismatch".Localized(), "keyContinue".Localized(), "keyStop".Localized());
                if (!continueExecution)
                    return false;
            }

            try
            {
                var result = await _tizenInstaller.InstallPackageAsync(
                    packagePath,
                    selectedDevice.IpAddress,
                    progress);

                if (result.Success)
                {
                    var win = App.Services.GetRequiredService<InstallationCompleteWindow>();

                    if (Avalonia.Application.Current?.ApplicationLifetime is
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        win.ShowDialog(desktop.MainWindow);
                    }
                    return true;
                }
                else
                {
                    progress?.Invoke("InstallationFailed".Localized());
                    await _dialogService.ShowErrorAsync($"{"InstallationFailed".Localized()}: {result.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                progress?.Invoke("InstallationFailed".Localized());
                await _dialogService.ShowErrorAsync($"{"InstallationFailed".Localized()}: {ex.Message}");
                return false;
            }
        }
        public async Task<bool> InstallCustomPackagesAsync(
            string[] packagePaths,
            NetworkDevice device,
            Action<string> onProgress)
        {
            onProgress("UsingCustomWGT".Localized());

            var allSuccessful = true;

            foreach (var packagePath in packagePaths)
            {
                var filePath = packagePath.Trim();
                if (!File.Exists(filePath))
                {
                    await _dialogService.ShowErrorAsync($"Package not found: {filePath}");
                    allSuccessful = false;
                    break;
                }

                var success = await InstallPackageAsync(filePath, device);
                if (!success)
                {
                    allSuccessful = false;
                    break;
                }
            }

            return allSuccessful;
        }
        public void CleanupDownloadedPackage(string? downloadedPackagePath)
        {
            try
            {
                if (downloadedPackagePath != null && File.Exists(downloadedPackagePath))
                {
                    File.Delete(downloadedPackagePath);
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
    }
}
