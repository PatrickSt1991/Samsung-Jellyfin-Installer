using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jellyfin2Samsung.Helpers;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Models;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        private readonly ITizenInstallerService _tizenInstaller;
        private readonly IDialogService _dialogService;
        private readonly INetworkService _networkService;
        private readonly ILocalizationService _localizationService;
        private readonly HttpClient _httpClient;
        private readonly FileHelper _fileHelper;
        private readonly DeviceHelper _deviceHelper;
        private readonly PackageHelper _packageHelper;
        private readonly SettingsViewModel _settingsViewModel;
        private CancellationTokenSource? _samsungLoginCts;

        [ObservableProperty]
        private ObservableCollection<GitHubRelease> releases = new ObservableCollection<GitHubRelease>();

        [ObservableProperty]
        private ObservableCollection<Asset> availableAssets = new ObservableCollection<Asset>();

        [ObservableProperty]
        private ObservableCollection<NetworkDevice> availableDevices = new ObservableCollection<NetworkDevice>();

        [ObservableProperty]
        private GitHubRelease? selectedRelease;

        [ObservableProperty]
        private Asset? selectedAsset;

        [ObservableProperty]
        private string customWgtPath = string.Empty;

        [ObservableProperty]
        private NetworkDevice? selectedDevice;

        [ObservableProperty]
        private bool isLoading;

        [ObservableProperty]
        private bool isLoadingDevices;

        [ObservableProperty]
        private string statusBar = string.Empty;

        [ObservableProperty]
        private bool isSamsungLoginActive;

        private string _currentStatusKey = string.Empty;

        private string? _downloadedPackagePath;
        private string L(string key) => _localizationService.GetString(key);

        public bool EnableDevicesInput => !IsLoadingDevices;
        public string LblRelease =>  _localizationService.GetString("lblRelease");
        public string LblVersion => _localizationService.GetString("lblVersion");
        public string LblSelectTv => _localizationService.GetString("lblSelectTv");
        public string DownloadAndInstall => _localizationService.GetString("DownloadAndInstall");
        public string lblCustomWgt => _localizationService.GetString("lblCustomWgt");
        public string SelectWGT => _localizationService.GetString("SelectWGT");
        public static string FooterText =>
            $"{AppSettings.Default.AppVersion} " +
            $"- Copyright (c) {DateTime.Now.Year} - MIT License - Patrick Stel";

        public MainWindowViewModel(
            ITizenInstallerService tizenInstaller,
            IDialogService dialogService,
            INetworkService networkService,
            ILocalizationService localizationService,
            HttpClient httpClient,
            DeviceHelper deviceHelper,
            PackageHelper packageHelper,
            FileHelper fileHelper
            )
        {
            _tizenInstaller = tizenInstaller;
            _dialogService = dialogService;
            _networkService = networkService;
            _httpClient = httpClient;
            _deviceHelper = deviceHelper;
            _packageHelper = packageHelper;
            _localizationService = localizationService;
            _fileHelper = fileHelper;
            _settingsViewModel = App.Services.GetRequiredService<SettingsViewModel>();

            _localizationService.LanguageChanged += OnLanguageChanged;
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            RefreshLocalizedProperties();

            if (!string.IsNullOrEmpty(_currentStatusKey))
                StatusBar = L(_currentStatusKey);
        }
        private void SetStatus(string key)
        {
            _currentStatusKey = key;
            StatusBar = L(key);
        }

        private void RefreshLocalizedProperties()
        {
            OnPropertyChanged(nameof(LblRelease));
            OnPropertyChanged(nameof(LblVersion));
            OnPropertyChanged(nameof(LblSelectTv));
            OnPropertyChanged(nameof(DownloadAndInstall));
            OnPropertyChanged(nameof(FooterText));
            OnPropertyChanged(nameof(StatusBar));
            OnPropertyChanged(nameof(lblCustomWgt));
            OnPropertyChanged(nameof(SelectWGT));
        }

        partial void OnSelectedReleaseChanged(GitHubRelease? value)
        {
            AvailableAssets = value != null
                ? new ObservableCollection<Asset>(value.Assets)
                : new ObservableCollection<Asset>();
            SelectedAsset = AvailableAssets.FirstOrDefault();

            RefreshCanExecuteChanged();
        }
        partial void OnSelectedAssetChanged(Asset? value)
        {
            RefreshCanExecuteChanged();
        }
        partial void OnCustomWgtPathChanged(string value)
        {
            AppSettings.Default.CustomWgtPath = value;
            AppSettings.Default.Save();

            DownloadAndInstallCommand?.NotifyCanExecuteChanged();
            DownloadCommand?.NotifyCanExecuteChanged();
        }

        partial void OnSelectedDeviceChanged(NetworkDevice? value)
        {
            if (value?.IpAddress == L("lblOther"))
                _ = PromptForManualIpAsync();

            RefreshCanExecuteChanged();
            AppSettings.Default.TvIp = value?.IpAddress ?? string.Empty;
            AppSettings.Default.Save();
        }

        partial void OnIsLoadingChanged(bool value)
        {
            RefreshCanExecuteChanged();
        }

        partial void OnIsLoadingDevicesChanged(bool value)
        {
            OnPropertyChanged(nameof(EnableDevicesInput));
            RefreshCanExecuteChanged();
        }

        private void RefreshCanExecuteChanged()
        {
            RefreshCommand.NotifyCanExecuteChanged();
            RefreshDevicesCommand.NotifyCanExecuteChanged();
            DownloadCommand.NotifyCanExecuteChanged();
            InstallCommand.NotifyCanExecuteChanged();
            DownloadAndInstallCommand.NotifyCanExecuteChanged();
            OpenSettingsCommand.NotifyCanExecuteChanged();
            CancelSamsungLoginCommand.NotifyCanExecuteChanged();
        }

        public async Task InitializeAsync()
        {
            try
            {
                SetStatus("CheckingTizenSdb");

                string tizenSdb = await _tizenInstaller.EnsureTizenSdbAvailable();

                if (string.IsNullOrEmpty(tizenSdb))
                {
                    SetStatus("FailedTizenSdb");
                    return;
                }
                
                ProcessHelper.KillSdbServers();

                await LoadReleasesAsync();
                SetStatus("ScanningNetwork");
                await LoadDevicesAsync();
                CustomWgtPath = AppSettings.Default.CustomWgtPath ?? "";
            }
            catch (Exception ex)
            {
                SetStatus("InitializationFailed");
                await _dialogService.ShowErrorAsync($"{L("InitializationFailed")} {ex}");
            }
        }

        [RelayCommand(CanExecute = nameof(CanRefresh))]
        private async Task RefreshAsync()
        {
            await LoadReleasesAsync();
        }

        [RelayCommand(CanExecute = nameof(CanRefreshDevices))]
        private async Task RefreshDevicesAsync()
        {
            await LoadDevicesAsync();
        }

        [RelayCommand(CanExecute = nameof(CanDownload))]
        private async Task DownloadAsync()
        {
            if (SelectedRelease != null)
            {
                await _packageHelper.DownloadReleaseAsync(
                    SelectedRelease,
                    SelectedAsset);
            }
        }

        [RelayCommand(CanExecute = nameof(CanInstall))]
        private async Task InstallAsync()
        {
            _samsungLoginCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            if (SelectedDevice != null)
            {
                await _packageHelper.InstallPackageAsync(
                    _downloadedPackagePath,
                    SelectedDevice,
                    _samsungLoginCts.Token);
            }
        }

        [RelayCommand(CanExecute = nameof(CanDownload))]
        private async Task DownloadAndInstallAsync()
        {
            _samsungLoginCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

            if ((SelectedRelease != null && SelectedDevice != null) || (!string.IsNullOrEmpty(AppSettings.Default.CustomWgtPath)))
            {
                var customPaths = AppSettings.Default.CustomWgtPath?.Split(';', StringSplitOptions.RemoveEmptyEntries);

                if (customPaths?.Length > 0)
                {
                    await _packageHelper.InstallCustomPackagesAsync(
                        customPaths,
                        SelectedDevice,
                        _samsungLoginCts.Token,
                        progress => Dispatcher.UIThread.Post(() => StatusBar = progress),
                        onSamsungLoginStarted: OnSamsungLoginStarted);

                    AppSettings.Default.CustomWgtPath = null;
                    AppSettings.Default.Save();
                }
                else
                {

                    string? downloadPath = await _packageHelper.DownloadReleaseAsync(
                        SelectedRelease,
                        SelectedAsset,
                        message => Dispatcher.UIThread.Post(() => StatusBar = message));

                    if (!string.IsNullOrEmpty(downloadPath))
                    {
                        try
                        {
                            await _packageHelper.InstallPackageAsync(
                                downloadPath,
                                SelectedDevice,
                                _samsungLoginCts.Token,
                                message => Dispatcher.UIThread.Post(() => StatusBar = message),
                                onSamsungLoginStarted: OnSamsungLoginStarted);
                        }
                        finally
                        {
                            IsSamsungLoginActive = false;
                            _samsungLoginCts.Dispose();
                            _samsungLoginCts = null;
                        }

                        if(!AppSettings.Default.KeepWGTFile)
                            _packageHelper.CleanupDownloadedPackage(downloadPath);
                    }
                }
            }
        }

        [RelayCommand(CanExecute = nameof(CanOpenSettings))]
        private void OpenSettings()
        {
            var settingsWindow = new SettingsView
            {
                DataContext = _settingsViewModel
            };

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                settingsWindow.ShowDialog(desktop.MainWindow);
            }
        }
        [RelayCommand]
        private async Task ShowBuildInfoAsync()
        {
            try
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                    return;

                var buildInfoWindow = new Views.BuildInfoWindow();

                // Show as modal dialog centered on MainWindow
                await buildInfoWindow.ShowDialog(desktop.MainWindow);
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync($"Failed to open build info window: {ex}");
            }
        }
        [RelayCommand]
        private async Task BrowseWgtAsync()
        {
            var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow : null;

            if (mainWindow?.StorageProvider != null)
            {
                var result = await _fileHelper.BrowseWgtFilesAsync(mainWindow.StorageProvider);
                if (!string.IsNullOrEmpty(result))
                    CustomWgtPath = result;
            }
        }
        [RelayCommand(CanExecute = nameof(IsSamsungLoginActive))]
        private void CancelSamsungLogin()
        {
            _samsungLoginCts?.Cancel();
        }

        private bool CanRefresh() => !IsLoading;
        private bool CanRefreshDevices() => !IsLoadingDevices;
        private bool CanOpenSettings() => !IsLoadingDevices;

        private bool CanDownload()
        {
            if (!string.IsNullOrEmpty(AppSettings.Default.CustomWgtPath))
            {
                var files = AppSettings.Default.CustomWgtPath.Split(';', StringSplitOptions.RemoveEmptyEntries);
                return files.All(File.Exists) &&
                       !IsLoading &&
                       SelectedDevice != null &&
                       !string.IsNullOrWhiteSpace(SelectedDevice.IpAddress);
            }

            return !IsLoading &&
                   SelectedRelease != null &&
                   SelectedAsset != null &&
                   SelectedDevice != null &&
                   !string.IsNullOrWhiteSpace(SelectedDevice.IpAddress);
        }


        private bool CanInstall()
        {
            // If custom wgt path is set, allow install without _downloadedPackagePath
            if (!string.IsNullOrEmpty(AppSettings.Default.CustomWgtPath))
            {
                var files = AppSettings.Default.CustomWgtPath.Split(';');
                return files.All(File.Exists) &&
                       SelectedDevice != null &&
                       !string.IsNullOrWhiteSpace(SelectedDevice.IpAddress);
            }

            // Otherwise fallback to _downloadedPackagePath logic
            return !string.IsNullOrEmpty(_downloadedPackagePath) &&
                   File.Exists(_downloadedPackagePath) &&
                   SelectedDevice != null &&
                   !string.IsNullOrWhiteSpace(SelectedDevice.IpAddress);
        }


        private async Task LoadReleasesAsync()
        {
            IsLoading = true;
            Releases.Clear();

            try
            {
                var settings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };

                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.1");
                var response = await _httpClient.GetStringAsync(AppSettings.Default.ReleasesUrl);

                var allReleases = JsonConvert.DeserializeObject<List<GitHubRelease>>(response, settings)?
                    .OrderByDescending(r => r.PublishedAt)
                    .Take(5);

                if (allReleases != null)
                    foreach (var release in allReleases)
                        Releases.Add(release);

                var moonfinResponse = await _httpClient.GetStringAsync(AppSettings.Default.MoonfinRelease);
                List<GitHubRelease> moonfinReleases;

                if (moonfinResponse.TrimStart().StartsWith("["))
                {
                    moonfinReleases = JsonConvert.DeserializeObject<List<GitHubRelease>>(moonfinResponse, settings);
                }
                else if (moonfinResponse.TrimStart().StartsWith("{"))
                {
                    var single = JsonConvert.DeserializeObject<GitHubRelease>(moonfinResponse, settings);
                    moonfinReleases = single != null ? new List<GitHubRelease> { single } : new List<GitHubRelease>();
                }
                else
                {
                    moonfinReleases = new List<GitHubRelease>();
                }

                moonfinReleases = moonfinReleases.OrderByDescending(r => r.PublishedAt).Take(1).ToList();
                foreach (var moonfinRelease in moonfinReleases)
                    Releases.Add(new GitHubRelease
                    {
                        Name = $"Moonfin",
                        Assets = moonfinRelease.Assets,
                        PublishedAt = moonfinRelease.PublishedAt,
                        TagName = moonfinRelease.TagName,
                        Url = moonfinRelease.Url
                    });

                /* av release */
                var avResponse = await _httpClient.GetStringAsync(AppSettings.Default.JellyfinAvRelease);

                List<GitHubRelease> avReleases;
                if (avResponse.TrimStart().StartsWith("["))
                {
                    avReleases = JsonConvert.DeserializeObject<List<GitHubRelease>>(avResponse, settings);
                }
                else if (avResponse.TrimStart().StartsWith("{"))
                {
                    var single = JsonConvert.DeserializeObject<GitHubRelease>(avResponse, settings);
                    avReleases = single != null ? new List<GitHubRelease> { single } : new List<GitHubRelease>();
                }
                else
                {
                    avReleases = new List<GitHubRelease>();
                }

                avReleases = avReleases.OrderByDescending(r => r.PublishedAt).Take(1).ToList();

                foreach (var avRelease in avReleases)
                    Releases.Add(new GitHubRelease
                    {
                        Name = $"Jellyfin AVPlay",
                        Assets = avRelease.Assets,
                        PublishedAt = avRelease.PublishedAt,
                        TagName = avRelease.TagName,
                        Url = avRelease.Url
                    });

                /* Legacy */
                var legacyResponse = await _httpClient.GetStringAsync(AppSettings.Default.JellyfinLegacy);

                List<GitHubRelease> legacyReleases;
                if (legacyResponse.TrimStart().StartsWith("["))
                {
                    legacyReleases = JsonConvert.DeserializeObject<List<GitHubRelease>>(legacyResponse, settings);
                }
                else if (legacyResponse.TrimStart().StartsWith("{"))
                {
                    var single = JsonConvert.DeserializeObject<GitHubRelease>(legacyResponse, settings);
                    legacyReleases = single != null ? new List<GitHubRelease> { single } : new List<GitHubRelease>();
                }
                else
                {
                    legacyReleases = new List<GitHubRelease>();
                }

                legacyReleases = legacyReleases.OrderByDescending(r => r.PublishedAt).Take(1).ToList();

                foreach (var legacyRelease in legacyReleases)
                {
                    Releases.Add(new GitHubRelease
                    {
                        Name = $"Jellyfin Legacy",
                        Assets = legacyRelease.Assets,
                        PublishedAt = legacyRelease.PublishedAt,
                        TagName = legacyRelease.TagName,
                        Url = legacyRelease.Url
                    });
                }

                var communityResponse = await _httpClient.GetStringAsync(AppSettings.Default.CommunityRelease);
                List<GitHubRelease> communityReleases;
                if(communityResponse.TrimStart().StartsWith("["))
                {
                    communityReleases = JsonConvert.DeserializeObject<List<GitHubRelease>>(communityResponse, settings);
                }
                else if (communityResponse.TrimStart().StartsWith("{"))
                {
                    var single = JsonConvert.DeserializeObject<GitHubRelease>(communityResponse, settings);
                    communityReleases = single != null ? new List<GitHubRelease> { single } : new List<GitHubRelease>();
                }
                else
                {
                    communityReleases = new List<GitHubRelease>();
                }

                communityReleases = communityReleases.OrderByDescending(r => r.PublishedAt).Take(1).ToList();

                foreach (var communityRelease in communityReleases)
                {
                    Releases.Add(new GitHubRelease
                    {
                        Name = $"Tizen Community",
                        Assets = communityRelease.Assets,
                        PublishedAt = communityRelease.PublishedAt,
                        TagName = communityRelease.TagName,
                        Url = communityRelease.Url
                    });
                }

            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                await _dialogService.ShowErrorAsync($"{L("FailedLoadingReleases")} {ex}");
            }
            finally
            {
                Releases.Add(new GitHubRelease
                {
                    Name = "Custom WGT File",
                    TagName = string.Empty,
                    PublishedAt = string.Empty,
                    Url = string.Empty,
                    Assets = new List<Asset>()
                });

                IsLoading = false;
            }
        }

        private async Task LoadDevicesAsync(CancellationToken cancellationToken = default, bool virtualScan = false)
        {
            IsLoadingDevices = true;
            AvailableDevices.Clear();

            try
            {
                string? selectedIp = SelectedDevice?.IpAddress;

                var devices = await _deviceHelper.ScanForDevicesAsync(cancellationToken, virtualScan);
                foreach (var device in devices)
                    AvailableDevices.Add(device);

                if (AvailableDevices.Count == 0)
                {
                    if (!virtualScan)
                    {
                        SetStatus("NoDevicesFoundRetry");
                        var rescan = await _dialogService.ShowConfirmationAsync(
                            L("NoDevicesFound"),
                            L("RetySearchMsg"),
                            L("keyYes"),
                            L("keyNo"));

                        if (rescan)
                            await LoadDevicesAsync(cancellationToken, true);
                        else
                        {
                            SetStatus("NoDevicesFound");
                            return;
                        }
                    }
                    else
                    {
                        SetStatus("NoDevicesFound");
                    }
                }
                else
                {
                    SetStatus("Ready");
                }

                if (AvailableDevices.Any())
                {
                    if (SelectedDevice == null)
                        SelectedDevice = AvailableDevices[0];
                    else if (!string.IsNullOrEmpty(selectedIp))
                    {
                        var previousDevice = AvailableDevices.FirstOrDefault(it => it.IpAddress == selectedIp);
                        if (previousDevice != null)
                            SelectedDevice = previousDevice;
                    }
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync($"Failed to load devices: {ex}");
            }
            finally
            {
                if (!AvailableDevices.Any(d => d.IpAddress == L("lblOther")))
                {
                    AvailableDevices.Add(new NetworkDevice
                    {
                        IpAddress = L("lblOther"),
                        Manufacturer = null,
                        DeviceName = L("IpNotListed")
                    });
                }

                IsLoadingDevices = false;
            }
        }
        private async Task PromptForManualIpAsync()
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                return;

            var dialog = new IpInputDialog();
            
                
            string? ip = await dialog.ShowDialogAsync(desktop.MainWindow);

            if (string.IsNullOrWhiteSpace(ip))
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IpAddress != "Other");
                return;
            }

            var device = await _networkService.ValidateManualTizenAddress(ip);
            if (device == null)
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IpAddress != "Other");
                await _dialogService.ShowErrorAsync(L("InvalidDeviceIp"));
                return;
            }

            var samsungDevice = await _deviceHelper.GetDeveloperInfoAsync(device);

            if (samsungDevice != null)
            {
                AppSettings.Default.UserCustomIP = samsungDevice.IpAddress;
                AppSettings.Default.Save();

                SelectedDevice = samsungDevice;

                if (!AvailableDevices.Any(d => d.IpAddress == device.IpAddress))
                    AvailableDevices.Add(samsungDevice);
            }
            else
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IpAddress != "Other");
                await _dialogService.ShowErrorAsync(L("InvalidDeviceIp"));
            }
        }
        public void Dispose()
        {
            _localizationService.LanguageChanged -= OnLanguageChanged;
        }
        partial void OnIsSamsungLoginActiveChanged(bool value)
        {
            CancelSamsungLoginCommand.NotifyCanExecuteChanged();
        }
        private void OnSamsungLoginStarted()
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsSamsungLoginActive = true;
            });
        }

    }
}