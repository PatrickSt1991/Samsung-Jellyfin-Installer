using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jellyfin2Samsung.Helpers;
using Jellyfin2Samsung.Helpers.API;
using Jellyfin2Samsung.Helpers.Core;
using Jellyfin2Samsung.Helpers.Tizen.Devices;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Services;
using Jellyfin2Samsung.Models;
using Jellyfin2Samsung.Views;
using Microsoft.Extensions.DependencyInjection;
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
    public partial class MainWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly ITizenInstallerService _tizenInstaller;
        private readonly IDialogService _dialogService;
        private readonly INetworkService _networkService;
        private readonly ILocalizationService _localizationService;
        private readonly IThemeService _themeService;
        private readonly FileHelper _fileHelper;
        private readonly DeviceHelper _deviceHelper;
        private readonly TizenApiClient _tizenApiClient;
        private readonly PackageHelper _packageHelper;
        private readonly JellyfinConfigViewModel _settingsViewModel;
        private readonly AddLatestRelease _addLatestRelease;
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

        [ObservableProperty]
        private bool darkMode;

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
            IThemeService themeService,
            HttpClient httpClient,
            DeviceHelper deviceHelper,
            TizenApiClient tizenApiClient,
            PackageHelper packageHelper,
            FileHelper fileHelper,
            JellyfinConfigViewModel settingsViewModel
        )
        {
            _tizenInstaller = tizenInstaller;
            _dialogService = dialogService;
            _networkService = networkService;
            _deviceHelper = deviceHelper;
            _tizenApiClient = tizenApiClient;
            _packageHelper = packageHelper;
            _localizationService = localizationService;
            _themeService = themeService;
            _fileHelper = fileHelper;
            _settingsViewModel = settingsViewModel;

            _addLatestRelease = new AddLatestRelease(httpClient);

            _localizationService.LanguageChanged += OnLanguageChanged;
            _themeService.ThemeChanged += OnThemeChanged;

            // Initialize dark mode state from settings
            DarkMode = AppSettings.Default.DarkMode;
        }


        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            RefreshLocalizedProperties();

            if (!string.IsNullOrEmpty(_currentStatusKey))
                StatusBar = L(_currentStatusKey);
        }

        private void OnThemeChanged(object? sender, bool isDarkMode)
        {
            DarkMode = isDarkMode;
        }

        partial void OnDarkModeChanged(bool value)
        {
            _themeService.SetTheme(value);
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


                    foreach (var customPath in customPaths)
                        if (!AppSettings.Default.KeepWGTFile)
                            _packageHelper.CleanupDownloadedPackage(customPath);

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
            var settingsWindow = new JellyfinConfigView(_settingsViewModel);

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

            try
            {
                var list = new List<GitHubRelease>();

                async Task fetch(string url, string name)
                {
                    var release = await _addLatestRelease.GetLatestReleaseAsync(url, name);
                    if (release != null)
                        list.Add(release);
                }

                await fetch(AppSettings.Default.ReleasesUrl, Constants.AppIdentifiers.JellyfinAppName);
                await fetch(AppSettings.Default.MoonfinRelease, "Moonfin");
                await fetch(AppSettings.Default.JellyfinAvRelease, "Jellyfin - AVPlay");
                await fetch(AppSettings.Default.JellyfinAvRelease, "Jellyfin - AVPlay - 10.10z SmartHub");
                await fetch(AppSettings.Default.JellyfinLegacy, "Jellyfin - Legacy");
                await fetch(AppSettings.Default.CommunityRelease, "Tizen Community");

                Releases.Clear();
                foreach (var r in list)
                    Releases.Add(r);

                Releases.Add(new GitHubRelease
                {
                    Name = Constants.AppIdentifiers.CustomWgtFile,
                    TagName = string.Empty,
                    PublishedAt = string.Empty,
                    Url = string.Empty,
                    Assets = new List<Asset>()
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine(ex);
                await _dialogService.ShowErrorAsync($"{L(Constants.LocalizationKeys.FailedLoadingReleases)} {ex}");
            }
            finally
            {
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

            var samsungDevice = await _tizenApiClient.GetDeveloperInfoAsync(device);

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
        private void DisposeSamsungCts()
        {
            _samsungLoginCts?.Cancel();
            _samsungLoginCts?.Dispose();
            _samsungLoginCts = null;
        }
        public void Dispose()
        {
            DisposeSamsungCts();
            _localizationService.LanguageChanged -= OnLanguageChanged;
            _themeService.ThemeChanged -= OnThemeChanged;
        }

    }
}