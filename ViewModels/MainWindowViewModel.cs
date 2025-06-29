﻿using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Samsung_Jellyfin_Installer.Commands;
using Samsung_Jellyfin_Installer.Converters;
using Samsung_Jellyfin_Installer.Models;
using Samsung_Jellyfin_Installer.Services;
using Samsung_Jellyfin_Installer.Views;
using Samsung_Jellyfin_Installer.Localization;
using System.Collections.ObjectModel;
﻿using Newtonsoft.Json;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

namespace Samsung_Jellyfin_Installer.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private readonly ITizenInstallerService _tizenInstaller;
        private readonly IDialogService _dialogService;
        private readonly INetworkService _networkService;
        private readonly IServiceProvider _serviceProvider;
        private readonly HttpClient _httpClient;

        private ObservableCollection<GitHubRelease> _releases = new ObservableCollection<GitHubRelease>();
        private ObservableCollection<Asset> _availableAssets = new ObservableCollection<Asset>();
        private ObservableCollection<NetworkDevice> _availableDevices = new ObservableCollection<NetworkDevice>();
        private GitHubRelease _selectedRelease;
        private Asset _selectedAsset;
        private NetworkDevice? _selectedDevice;
        private bool _isLoading, _isLoadingDevices;

        private string _statusBar;
        private string _tizenProfilePath;
        private string _downloadedPackagePath;

        public ObservableCollection<GitHubRelease> Releases
        {
            get => _releases;
            private set => SetField(ref _releases, value);
        }
        public ObservableCollection<Asset> AvailableAssets
        {
            get => _availableAssets;
            private set => SetField(ref _availableAssets, value);
        }
        public ObservableCollection<NetworkDevice> AvailableDevices
        {
            get => _availableDevices;
            private set => SetField(ref _availableDevices, value);
        }
        public GitHubRelease SelectedRelease
        {
            get => _selectedRelease;
            set
            {
                if (SetField(ref _selectedRelease, value))
                {
                    AvailableAssets = value != null
                        ? new ObservableCollection<Asset>(value.Assets)
                        : new ObservableCollection<Asset>();
                    SelectedAsset = AvailableAssets.FirstOrDefault();
                }
            }
        }
        public Asset SelectedAsset
        {
            get => _selectedAsset;
            set => SetField(ref _selectedAsset, value);
        }
        public NetworkDevice? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (SetField(ref _selectedDevice, value) && value?.IpAddress == "Other")
                    _ = PromptForManualIp();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetField(ref _isLoading, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }
        public bool IsLoadingDevices
        {
            get => _isLoadingDevices;
            private set
            {
                if (SetField(ref _isLoadingDevices, value))
                {
                    OnPropertyChanged(nameof(EnableDevicesInput));
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool EnableDevicesInput => !IsLoadingDevices;

        public string StatusBar
        {
            get => _statusBar;
            set => SetField(ref _statusBar, value);
        }
        public string FooterText =>
            $"{Settings.Default.AppVersion} " +
            $"- Copyright (c) {DateTime.Now.Year} - MIT License - Patrick Stel";


        public ICommand RefreshCommand { get; }
        public ICommand RefreshDevicesCommand { get; }
        public ICommand DownloadCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand DownloadAndInstallCommand { get; }
        public ICommand OpenSettingsCommand { get; }

        public MainWindowViewModel(
            ITizenInstallerService tizenInstaller,
            IDialogService dialogService,
            INetworkService networkService,
            IServiceProvider serviceProvider,
            HttpClient httpClient)
        {
            _tizenInstaller = tizenInstaller;
            _dialogService = dialogService;
            _networkService = networkService;
            _serviceProvider = serviceProvider;
            _httpClient = httpClient;

            RefreshCommand = new RelayCommand(async () => await LoadReleasesAsync());
            RefreshDevicesCommand = new RelayCommand(async () => await LoadDevicesAsync());
            DownloadCommand = new RelayCommand<GitHubRelease>(
                async r => await DownloadReleaseAsync(r),
                r => CanExecuteDownloadCommand(r)
            );

            InstallCommand = new RelayCommand(
                async () => await InstallPackageAsync(),
                () => !string.IsNullOrEmpty(_downloadedPackagePath) &&
                      File.Exists(_downloadedPackagePath) &&
                      SelectedDevice != null &&
                      !string.IsNullOrWhiteSpace(SelectedDevice.IpAddress)
            );

            DownloadAndInstallCommand = new RelayCommand<GitHubRelease>(
                async r => await DownloadAndInstallReleaseAsync(r),
                r => CanExecuteDownloadCommand(r)
            );

            OpenSettingsCommand = new RelayCommand(async () => OpenSettings());

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            try
            {
                StatusBar = "CheckingTizenCli".Localized();

                (string TizenDataPath, string TizenCliPath) = await _tizenInstaller.EnsureTizenCliAvailable();

                if (string.IsNullOrEmpty(TizenDataPath))
                {
                    StatusBar = "TizenCliFailed".Localized();
                    return;
                }

                await WebView2Helper.EnsureWebView2RuntimeAsync();

                await LoadReleasesAsync();
                StatusBar = "ScanningNetwork".Localized();
                await LoadDevicesAsync();
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"Initialization failed: {ex}");
                StatusBar = "InitializationFailed".Localized();
            }
        }
        private void OpenSettings()
        {
            var settingsViewModel = _serviceProvider.GetRequiredService<SettingsViewModel>();
            var settingsWindow = new SettingsView();
            settingsWindow.DataContext = settingsViewModel;
            settingsWindow.ShowDialog();
        }
        private bool CanExecuteDownloadCommand(GitHubRelease release)
        {
            if (!string.IsNullOrEmpty(Settings.Default.CustomWgtPath) &&
                File.Exists(Settings.Default.CustomWgtPath))
            {
                return !IsLoading && SelectedDevice != null &&
                       !string.IsNullOrWhiteSpace(SelectedDevice.IpAddress);
            }

            return !IsLoading &&
                   release != null &&
                   SelectedAsset != null &&
                   SelectedDevice != null &&
                   !string.IsNullOrWhiteSpace(SelectedDevice.IpAddress);
        }
        private async Task<string> DownloadReleaseAsync(GitHubRelease release)
        {
            if (release?.PrimaryDownloadUrl == null) return null;

            IsLoading = true;
            try
            {
                StatusBar = "DownloadingPackage".Localized();
                string downloadPath = await _tizenInstaller.DownloadPackageAsync(SelectedAsset.DownloadUrl);
                _downloadedPackagePath = downloadPath;

                StatusBar = "DownloadCompleted".Localized();
                return downloadPath;
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync($"{"DownloadFailed".Localized()} {ex.Message}");
                return null;
            }
            finally
            {
                IsLoading = false;
            }
        }
        private async Task<bool> InstallPackageAsync(string packagePath = null)
        {
            string installPath = packagePath ?? _downloadedPackagePath;

            if(string.IsNullOrEmpty(installPath) || !File.Exists(installPath))
            {
                await _dialogService.ShowErrorAsync("NoPackageToInstall".Localized());
                return false;
            }

            if (string.IsNullOrWhiteSpace(SelectedDevice?.IpAddress))
            {
                await _dialogService.ShowErrorAsync("NoDeviceSelected".Localized());
                return false;
            }

            IsLoading = true;
            try
            {
                StatusBar = "InstallingPackage".Localized();

                var result = await _tizenInstaller.InstallPackageAsync(
                    installPath,
                    SelectedDevice.IpAddress,
                    status => Application.Current.Dispatcher.Invoke(() => StatusBar = status));

                if (result.Success)
                {
                    await _dialogService.ShowMessageAsync($"{"InstallationSuccessfulOn".Localized()} {SelectedDevice.IpAddress}");
                    return true;
                }
                else
                {
                    await _dialogService.ShowErrorAsync($"{"InstallationFailed".Localized()}: {result.ErrorMessage}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync($"{"InstallationFailed".Localized()}: {ex.Message}");
                return false;
            }
            finally
            {
                IsLoading = false;
            }
        }
        private async Task DownloadAndInstallReleaseAsync(GitHubRelease release)
        {
            string installPath = null;
            bool isCustomWgt = false;

            if(!string.IsNullOrEmpty(Settings.Default.CustomWgtPath) &&
                File.Exists(Settings.Default.CustomWgtPath))
            {
                installPath = Settings.Default.CustomWgtPath;
                isCustomWgt = true;
                StatusBar = "UsingCustomWGT".Localized();
            }
            else
            {
                installPath = await DownloadReleaseAsync(release);
                if (string.IsNullOrEmpty(installPath))
                    return;
            }

            bool packageInstalled = await InstallPackageAsync(installPath);

            if (!isCustomWgt)
                CleanupDownloadedPackage();

            if (isCustomWgt && packageInstalled)
            {
                Settings.Default.CustomWgtPath = null;
                Settings.Default.Save();
            }
        }
        private void CleanupDownloadedPackage()
        {
            try
            {
                if (_downloadedPackagePath != null && File.Exists(_downloadedPackagePath))
                {
                    File.Delete(_downloadedPackagePath);
                    _downloadedPackagePath = null;
                }
            }
            catch { /* Ignore cleanup errors */ }
        }
        private async Task LoadReleasesAsync()
        {
            IsLoading = true;
            Releases.Clear();

            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
                var response = await _httpClient.GetStringAsync(Settings.Default.ReleasesUrl);

                var allReleases = JsonConvert.DeserializeObject<List<GitHubRelease>>(response)?
                    .OrderByDescending(r => r.PublishedAt)
                    .Take(30);

                if (allReleases != null)
                {
                    foreach (var release in allReleases)
                        Releases.Add(release);
                }
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync($"{"FailedLoadingReleases".Localized()} {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }
        private async Task LoadDevicesAsync()
        {
            IsLoadingDevices = true;
            AvailableDevices.Clear();
            try
            {
                string? selectedIp = null;
                if (SelectedDevice is not null)
                    selectedIp = SelectedDevice.IpAddress;

                var devices = await _networkService.GetLocalTizenAddresses();

                foreach (NetworkDevice device in devices)
                    AvailableDevices.Add(device);

                if (AvailableDevices.Count == 0)
                    StatusBar = "NoDevicesFound".Localized();
                else
                    StatusBar = "Ready".Localized();


                SelectedDevice = AvailableDevices.Count switch
                {
                    > 0 when SelectedDevice is null => AvailableDevices[0],
                    > 0 when selectedIp is not null =>
                        AvailableDevices.FirstOrDefault(it => it.IpAddress == selectedIp),
                    _ => SelectedDevice
                };
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    $"Failed to load devices: {ex.Message}");
            }
            finally
            {
                AvailableDevices.Add(new NetworkDevice
                {
                    IpAddress = "lbl_Other".Localized(),
                    Manufacturer = null,
                    DeviceName = "IpNotListed".Localized()
                });

                IsLoadingDevices = false;
            }
        }
        private async Task PromptForManualIp()
        {
            string? ip = await _dialogService.PromptForIpAsync("IpWindowTitle".Localized(), "IpWindowDescription".Localized());

            if (string.IsNullOrWhiteSpace(ip))
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IpAddress != "Other");
                return;
            }

            var device = await _networkService.ValidateManualTizenAddress(ip);

            if (device != null)
            {
                Settings.Default.UserCustomIP = device.IpAddress;
                Settings.Default.Save();

                SelectedDevice = device;

                if (!AvailableDevices.Any(d => d.IpAddress == device.IpAddress))
                    AvailableDevices.Add(device);
            }
            else
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IpAddress != "Other");
                await _dialogService.ShowErrorAsync("InvalidDeviceIp".Localized());
            }
        }
    }
}