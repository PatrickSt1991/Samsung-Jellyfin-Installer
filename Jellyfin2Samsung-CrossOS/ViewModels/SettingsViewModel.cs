using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jellyfin2SamsungCrossOS.Helpers;
using Jellyfin2SamsungCrossOS.Models;
using Jellyfin2SamsungCrossOS.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly ITizenInstallerService _tizenService;
        private readonly CertificateHelper _certificateHelper;
        private readonly FileHelper _fileHelper;
        private readonly ILocalizationService _localizationService;

        [ObservableProperty]
        private LanguageOption? selectedLanguage;

        [ObservableProperty]
        private ExistingCertificates? selectedCertificateObject;

        [ObservableProperty]
        private string selectedCertificate = string.Empty;

        [ObservableProperty]
        private string customWgtPath = string.Empty;

        [ObservableProperty]
        private bool permitInstall;

        [ObservableProperty]
        private bool rememberCustomIP;

        [ObservableProperty]
        private bool deletePreviousInstall;

        [ObservableProperty]
        private bool forceSamsungLogin;

        [ObservableProperty]
        private bool rtlReading;

        public ObservableCollection<LanguageOption> AvailableLanguages { get; }
        public ObservableCollection<ExistingCertificates> AvailableCertificates { get; } = new();

        public string lblLanguage => _localizationService.GetString("lblLanguage");
        public string lblCertifcate => _localizationService.GetString("lblCertifcate");
        public string lblCustomWgt => _localizationService.GetString("lblCustomWgt");
        public string lblRememberIp => _localizationService.GetString("lblRememberIp");
        public string lblDeletePrevious => _localizationService.GetString("lblDeletePrevious");
        public string lblForceLogin => _localizationService.GetString("lblForceLogin");
        public string lblRTL => _localizationService.GetString("lblRTL");
        public string lblModifyConfig => _localizationService.GetString("lblModifyConfig");
        public string lblOpenConfig => _localizationService.GetString("lblOpenConfig");
        public string SelectWGT => _localizationService.GetString("SelectWGT");
        public string lblPermitInstall => _localizationService.GetString("lblPermitInstall");


        public SettingsViewModel(
            ITizenInstallerService tizenService,
            CertificateHelper certificateHelper,
            FileHelper fileHelper,
            ILocalizationService localizationService)
        {
            _tizenService = tizenService;
            _certificateHelper = certificateHelper;
            _fileHelper = fileHelper;
            _localizationService = localizationService;
            
            _localizationService.LanguageChanged += OnLanguageChanged;

            // Populate available languages dynamically from LocalizationService
            AvailableLanguages = new ObservableCollection<LanguageOption>(
                _localizationService.AvailableLanguages
                    .Select(code => new LanguageOption
                    {
                        Code = code,
                        Name = GetLanguageDisplayName(code)
                    })
                    .OrderBy(lang => lang.Name)
            );

            // Initialize settings
            InitializeSettings();

            _ = InitializeCertificatesAsync();
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            RefreshLocalizedProperties();
        }

        private void RefreshLocalizedProperties()
        {
            OnPropertyChanged(nameof(lblLanguage));
            OnPropertyChanged(nameof(lblCertifcate));
            OnPropertyChanged(nameof(lblCustomWgt));
            OnPropertyChanged(nameof(lblRememberIp));
            OnPropertyChanged(nameof(lblDeletePrevious));
            OnPropertyChanged(nameof(lblForceLogin));
            OnPropertyChanged(nameof(lblRTL));
            OnPropertyChanged(nameof(lblModifyConfig));
            OnPropertyChanged(nameof(lblOpenConfig));
            OnPropertyChanged(nameof(SelectWGT));
        }

        partial void OnSelectedLanguageChanged(LanguageOption? value)
        {
            if (value is null)
                return;

            AppSettings.Default.Language = value.Code;
            AppSettings.Default.Save();

            // Update the global LocalizationService
            _localizationService.SetLanguage(value.Code);
        }

        partial void OnSelectedCertificateObjectChanged(ExistingCertificates? value)
        {
            if (value != null)
            {
                SelectedCertificate = value.Name;
                AppSettings.Default.Certificate = value.Name;
                AppSettings.Default.Save();
            }
        }

        partial void OnSelectedCertificateChanged(string value)
        {
            AppSettings.Default.Certificate = value;
            AppSettings.Default.Save();

            SelectedCertificateObject = AvailableCertificates.FirstOrDefault(c => c.Name == value);
            AppSettings.Default.ChosenCertificates = SelectedCertificateObject;
        }

        partial void OnCustomWgtPathChanged(string value)
        {
            AppSettings.Default.CustomWgtPath = value;
            AppSettings.Default.Save();
        }

        partial void OnPermitInstallChanged(bool value)
        {
            AppSettings.Default.PermitInstall = value;
            AppSettings.Default.Save();
        }

        partial void OnRememberCustomIPChanged(bool value)
        {
            AppSettings.Default.RememberCustomIP = value;
            AppSettings.Default.Save();
        }

        partial void OnDeletePreviousInstallChanged(bool value)
        {
            AppSettings.Default.DeletePreviousInstall = value;
            AppSettings.Default.Save();
        }

        partial void OnForceSamsungLoginChanged(bool value)
        {
            AppSettings.Default.ForceSamsungLogin = value;
            AppSettings.Default.Save();
        }

        partial void OnRtlReadingChanged(bool value)
        {
            AppSettings.Default.RTLReading = value;
            AppSettings.Default.Save();
        }

        [RelayCommand]
        private void ModifyConfig()
        {
            var configWindow = App.Services.GetRequiredService<JellyfinConfigView>();
            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                configWindow.Show();
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

        private void InitializeSettings()
        {
            // Use current language from LocalizationService or fallback to saved setting
            var currentLangCode = _localizationService.CurrentLanguage ?? AppSettings.Default.Language ?? "en";

            SelectedLanguage = AvailableLanguages
                .FirstOrDefault(lang => string.Equals(lang.Code, currentLangCode, StringComparison.OrdinalIgnoreCase))
                ?? AvailableLanguages.FirstOrDefault();

            CustomWgtPath = AppSettings.Default.CustomWgtPath ?? "";
            PermitInstall = AppSettings.Default.PermitInstall;
            RememberCustomIP = AppSettings.Default.RememberCustomIP;
            DeletePreviousInstall = AppSettings.Default.DeletePreviousInstall;
            ForceSamsungLogin = AppSettings.Default.ForceSamsungLogin;
            RtlReading = AppSettings.Default.RTLReading;
        }

        private static string GetLanguageDisplayName(string code)
        {
            try
            {
                var name = new System.Globalization.CultureInfo(code).NativeName;
                return string.IsNullOrEmpty(name) ? code : char.ToUpper(name[0]) + name.Substring(1);
            }
            catch
            {
                return code;
            }
        }

        private async Task InitializeCertificatesAsync()
        {
            var (profilePath, tizenCrypto) = await _tizenService.EnsureTizenCliAvailable();
            var certificates = _certificateHelper.GetAvailableCertificates(profilePath, tizenCrypto);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var cert in certificates)
                    AvailableCertificates.Add(cert);

                var savedCertName = AppSettings.Default.Certificate;
                ExistingCertificates? selectedCert = null;

                if (!string.IsNullOrEmpty(savedCertName))
                    selectedCert = AvailableCertificates.FirstOrDefault(c => c.Name == savedCertName);

                selectedCert ??= AvailableCertificates.FirstOrDefault(c => c.Name == "Jelly2Sams (default)")
                                ?? AvailableCertificates.FirstOrDefault();

                if (selectedCert != null)
                    SelectedCertificate = selectedCert.Name;

                AppSettings.Default.ChosenCertificates = selectedCert;
            });
        }
        public void Dispose()
        {
            _localizationService.LanguageChanged -= OnLanguageChanged;
        }
    }
}
