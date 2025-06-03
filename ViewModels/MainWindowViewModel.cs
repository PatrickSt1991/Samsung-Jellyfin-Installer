using Newtonsoft.Json;
using Samsung_Jellyfin_Installer.Commands;
using Samsung_Jellyfin_Installer.Localization;
using Samsung_Jellyfin_Installer.Models;
using Samsung_Jellyfin_Installer.Services;
using System.Collections.ObjectModel;
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
        private readonly HttpClient _httpClient;
        private readonly INetworkService _networkService;

        private ObservableCollection<GitHubRelease> _releases = new ObservableCollection<GitHubRelease>();
        private GitHubRelease _selectedRelease;
        private bool _isLoading, _isLoadingDevices;
        private ObservableCollection<Asset> _availableAssets = new ObservableCollection<Asset>();
        private Asset _selectedAsset;
        private ObservableCollection<NetworkDevice> _availableDevices = new ObservableCollection<NetworkDevice>();
        private NetworkDevice? _selectedDevice;
        private string _statusBar;

        public ObservableCollection<GitHubRelease> Releases
        {
            get => _releases;
            private set => SetField(ref _releases, value);
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

        public ObservableCollection<Asset> AvailableAssets
        {
            get => _availableAssets;
            private set => SetField(ref _availableAssets, value);
        }

        public Asset SelectedAsset
        {
            get => _selectedAsset;
            set => SetField(ref _selectedAsset, value);
        }

        public ObservableCollection<NetworkDevice> AvailableDevices
        {
            get => _availableDevices;
            private set => SetField(ref _availableDevices, value);
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

        public ICommand RefreshCommand { get; }
        public ICommand RefreshDevicesCommand { get; }
        public ICommand DownloadCommand { get; }

        public MainWindowViewModel(
            ITizenInstallerService tizenInstaller,
            IDialogService dialogService,
            HttpClient httpClient,
            INetworkService networkService)
        {
            _tizenInstaller = tizenInstaller;
            _dialogService = dialogService;
            _httpClient = httpClient;
            _networkService = networkService;

            RefreshCommand = new RelayCommand(async () => await LoadReleasesAsync());
            RefreshDevicesCommand = new RelayCommand(async () => await LoadDevicesAsync());
            DownloadCommand = new RelayCommand<GitHubRelease>(
                async r => await DownloadReleaseAsync(r),
                r => CanExecuteDownloadCommand(r)
            );

            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            if (!await _tizenInstaller.EnsureTizenCliAvailable())
            {
                await _dialogService.ShowErrorAsync(
                    Strings.PleaseInstallTizen);
            }

            await LoadReleasesAsync();
            StatusBar = $"{Strings.ScanningNetwork}";
            await LoadDevicesAsync();
        }

        private bool CanExecuteDownloadCommand(GitHubRelease release)
        {
            return release != null
                && !string.IsNullOrWhiteSpace(SelectedDevice?.IpAddress);
        }
        private async Task DownloadReleaseAsync(GitHubRelease release)
        {
            if (release?.PrimaryDownloadUrl == null) return;

            IsLoading = true;
            string downloadPath = null;
            try
            {
                StatusBar = Strings.DownloadingPackage;
                string downloadDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SamsungJellyfinInstaller",
                    "Downloads");
                downloadPath = await _tizenInstaller.DownloadPackageAsync(SelectedAsset.DownloadUrl, downloadDirectory);

                if (!string.IsNullOrWhiteSpace(SelectedDevice?.IpAddress))
                {
                    var result = await _tizenInstaller.InstallPackageAsync(
                        downloadPath,
                        SelectedDevice.IpAddress,
                        status => Application.Current.Dispatcher.Invoke(() => StatusBar = status));

                    if (result.Success)
                    {
                        await _dialogService.ShowMessageAsync(
                            $"{Strings.InstallationSuccessfulOn} {SelectedDevice.IpAddress}");
                    }
                    else
                    {
                        await _dialogService.ShowErrorAsync(
                            $"{Strings.InstallationFailed}: {result.ErrorMessage}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Download failed: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    $"{Strings.DownloadFailed} {ex.Message}");
            }
            finally
            {
                try
                {
                    if (downloadPath != null && File.Exists(downloadPath))
                        File.Delete(downloadPath);
                }
                catch { /* Ignore cleanup errors */ }

                IsLoading = false;
            }
        }
        private async Task LoadReleasesAsync()
        {
            IsLoading = true;
            Releases.Clear();

            try
            {
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SamsungJellyfinInstaller/1.0");
                var response = await _httpClient.GetStringAsync(
                    "https://api.github.com/repos/jeppevinkel/jellyfin-tizen-builds/releases");

                var allReleases = JsonConvert.DeserializeObject<List<GitHubRelease>>(response)?
                    .OrderByDescending(r => r.PublishedAt)
                    .Take(30);

                if (allReleases != null)
                {
                    foreach (var release in allReleases)
                    {
                        Releases.Add(release);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Release load error: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    $"{Strings.FailedLoadingReleases} {ex.Message}");
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
                    StatusBar = Strings.NoDevicesFound;
                else
                    StatusBar = Strings.Ready;


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
                Debug.WriteLine($"Devices load error: {ex.Message}");
                await _dialogService.ShowErrorAsync(
                    $"Failed to load devices: {ex.Message}");
            }
            finally
            {
                AvailableDevices.Add(new NetworkDevice
                {
                    IpAddress = "Other",
                    Manufacturer = null,
                    DeviceName = "My IP is not listed..."
                });

                IsLoadingDevices = false;
            }
        }
        private async Task PromptForManualIp()
        {
            string? ip = await _dialogService.PromptForIpAsync("Enter Device IP", "Please enter the device's IP address:");

            if (string.IsNullOrWhiteSpace(ip))
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IpAddress != "Other");
                return;
            }

            var device = await _networkService.ValidateManualTizenAddress(ip);

            if (device != null)
            {
                SelectedDevice = device;

                if (!AvailableDevices.Any(d => d.IpAddress == device.IpAddress))
                    AvailableDevices.Add(device);
            }
            else
            {
                SelectedDevice = AvailableDevices.FirstOrDefault(d => d.IpAddress != "Other");
                await _dialogService.ShowErrorAsync("Invalid device IP or device not found.");
            }
        }


    }
}