using Microsoft.Win32;
using Samsung_Jellyfin_Installer.Commands;
using Samsung_Jellyfin_Installer.Converters;
using Samsung_Jellyfin_Installer.Models;
using Samsung_Jellyfin_Installer.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Input;
using System.Xml.Linq;

namespace Samsung_Jellyfin_Installer.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private LanguageOption _selectedLanguage;
        private ExistingCertificates _selectedCertificateObject;
        private string _selectedCertificate;
        private string _customWgtPath;
        private bool _rememberCustomIP;
        private bool _deletePreviousInstall;

        public LanguageOption SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (_selectedLanguage?.Code != value?.Code)
                {
                    _selectedLanguage = value;
                    OnPropertyChanged();

                    if (value != null)
                    {
                        LocalizedStrings.Instance.ChangeLanguage(value.Code);

                        Settings.Default.Language = value.Code;
                        Settings.Default.Save();
                    }
                }
            }
        }
        public ExistingCertificates SelectedCertificateObject
        {
            get => _selectedCertificateObject;
            set
            {
                if (_selectedCertificateObject != value)
                {
                    _selectedCertificateObject = value;
                    OnPropertyChanged(nameof(SelectedCertificateObject));

                    if (value != null)
                    {
                        _selectedCertificate = value.Name;
                        OnPropertyChanged(nameof(SelectedCertificate));

                        Settings.Default.Certificate = value.Name;
                        Settings.Default.Save();
                    }
                }
            }
        }
        public string SelectedCertificate
        {
            get => _selectedCertificate;
            set
            {
                if (_selectedCertificate != value)
                {
                    _selectedCertificate = value;
                    OnPropertyChanged(nameof(SelectedCertificate));

                    Settings.Default.Certificate = value;
                    Settings.Default.Save();

                    SelectedCertificateObject = AvailableCertificates.FirstOrDefault(c => c.Name == value);
                }
            }
        }
        public string CustomWgtPath
        {
            get => _customWgtPath;
            set
            {
                if (_customWgtPath != value)
                {
                    _customWgtPath = value;
                    OnPropertyChanged(nameof(CustomWgtPath));
                    Settings.Default.CustomWgtPath = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool RememberCustomIP
        {
            get => _rememberCustomIP;
            set
            {
                if(_rememberCustomIP != value)
                {
                    _rememberCustomIP = value;
                    OnPropertyChanged(nameof(RememberCustomIP));

                    Settings.Default.RememberCustomIP = value;
                    Settings.Default.Save();
                }
            }
        }
        public bool DeletePreviousInstall
        {
            get => _deletePreviousInstall;
            set
            {
                if(_deletePreviousInstall = value)
                {
                    _deletePreviousInstall = value;
                    OnPropertyChanged(nameof(DeletePreviousInstall));

                    Settings.Default.DeletePreviousInstall = value;
                    Settings.Default.Save();
                }
            }
        }

        public ObservableCollection<LanguageOption> AvailableLanguages { get; }
        public ObservableCollection<ExistingCertificates> AvailableCertificates { get; } = new();
        public ICommand BrowseWgtCommand { get; }
        public SettingsViewModel(ITizenInstallerService tizenService)
        {
            AvailableLanguages = new ObservableCollection<LanguageOption>(GetAvailableLanguages());
            BrowseWgtCommand = new RelayCommand(BrowseWgtFile);
            Task.Run(async () => await InitializeCertificates(tizenService));

            var savedLangCode = Settings.Default.Language ?? "en";
            SelectedLanguage = AvailableLanguages.FirstOrDefault(lang => lang.Code == savedLangCode)
                              ?? AvailableLanguages.FirstOrDefault(lang => lang.Code == "en");
            CustomWgtPath = Settings.Default.CustomWgtPath ?? "";
            RememberCustomIP = Settings.Default.RememberCustomIP;
            DeletePreviousInstall = Settings.Default.DeletePreviousInstall;
        }
        private IEnumerable<LanguageOption> GetAvailableLanguages()
        {
            // Manually define supported languages
            var supportedLanguages = new List<LanguageOption>
            {
                new() { Code = "en", Name = "English" },
                new() { Code = "da", Name = "Dansk" },
                new() { Code = "nl", Name = "Nederlands" }
            };

            // Verify resources actually exist
            var assembly = Assembly.GetExecutingAssembly();
            var existingLanguages = supportedLanguages
                .Where(lang => assembly.GetManifestResourceNames()
                    .Contains($"Samsung_Jellyfin_Installer.Localization.Strings.{lang.Code}.resources"))
                .ToList();

            return existingLanguages.Any() ? existingLanguages : supportedLanguages;
        }
        private async Task InitializeCertificates(ITizenInstallerService tizenService)
        {
            var (profilePath, tizenCrypto) = await tizenService.EnsureTizenCliAvailable();

            var certificates = GetAvailableCertificates(profilePath, tizenCrypto);
            Application.Current.Dispatcher.Invoke(() =>
            {
                foreach (var cert in certificates)
                    AvailableCertificates.Add(cert);

                var savedCertName = Settings.Default.Certificate;
                ExistingCertificates selectedCert = null;

                if (!string.IsNullOrEmpty(savedCertName))
                    selectedCert = AvailableCertificates.FirstOrDefault(c => c.Name == savedCertName);

                if (selectedCert == null)
                    selectedCert = AvailableCertificates.FirstOrDefault(c => c.Name == "Jelly2Sams (default)")
                                ?? AvailableCertificates.FirstOrDefault();

                if (selectedCert != null)
                    SelectedCertificate = selectedCert.Name;
            });
        }
        private List<ExistingCertificates> GetAvailableCertificates(string profilePath, string tizenCrypto)
        {
            var certificates = new List<ExistingCertificates>();
            var cipherUtil = new CipherUtil();

            if (!File.Exists(profilePath))
                return certificates;

            try
            {
                var doc = XDocument.Load(profilePath);
                var profiles = doc.Root?.Elements("profile");

                if (profiles == null)
                    return certificates;

                foreach (var profile in profiles)
                {
                    string? name = profile.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    var authorItem = profile.Elements("profileitem")
                        .FirstOrDefault(p => p.Attribute("distributor")?.Value == "0");

                    string? keyPath = authorItem?.Attribute("key")?.Value;
                    string? encryptedPassword = authorItem.Attribute("password")?.Value;
                    DateTime? expireDate = null;
                    string? decryptedPassword = null;

                    if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath) && (!string.IsNullOrEmpty(encryptedPassword)))
                    {
                        if (File.Exists(encryptedPassword))
                            decryptedPassword = cipherUtil.RunWincryptDecrypt(encryptedPassword, tizenCrypto);
                        else
                            decryptedPassword = cipherUtil.GetDecryptedString(encryptedPassword);

                        try
                        {
                            var cert = new X509Certificate2(keyPath, decryptedPassword, X509KeyStorageFlags.Exportable);
                            expireDate = cert.NotAfter;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to read certificate '{keyPath}': {ex.Message}");
                        }

                        if(expireDate.HasValue && expireDate.Value.Date >= DateTime.Today)
                        {
                            certificates.Add(new ExistingCertificates
                            {
                                Name = name,
                                File = keyPath,
                                ExpireDate = expireDate
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading profile XML: {ex.Message}");
            }

            certificates.Add(new ExistingCertificates
            {
                Name = "Jelly2Sams (default)",
                File = null, // null indicates this needs to be created
                ExpireDate = null
            });

            return certificates;
        }
        private void BrowseWgtFile()
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select WGT File",
                Filter = "WGT Files (*.wgt)|*.wgt",
                FilterIndex = 1,
                Multiselect = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var originalPath = openFileDialog.FileName;
                var directory = Path.GetDirectoryName(originalPath);
                var baseName = Path.GetFileNameWithoutExtension(originalPath);
                var extension = Path.GetExtension(originalPath);

                var random = new Random();
                const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
                var randomSuffix = new string(Enumerable.Range(0, 4).Select(_ => chars[random.Next(chars.Length)]).ToArray());

                var newFileName = $"{baseName}{randomSuffix}{extension}";
                var newFilePath = Path.Combine(directory, newFileName);

                File.Copy(originalPath, newFilePath, overwrite: true);
                CustomWgtPath = newFilePath;
            }
        }
    }
}