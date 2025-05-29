using Samsung_Jellyfin_Installer.Models;
using Samsung_Jellyfin_Installer.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml.Linq;

namespace Samsung_Jellyfin_Installer.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private LanguageOption _selectedLanguage;
        public ObservableCollection<LanguageOption> AvailableLanguages { get; }
        public ObservableCollection<string> AvailableCertificates { get; } = new();
        private string _selectedCertificate;
        public string SelectedCertificate
        {
            get => _selectedCertificate;
            set
            {
                if(_selectedCertificate != value)
                {
                    _selectedCertificate = value;
                    OnPropertyChanged(nameof(SelectedCertificate));

                    var normalized = value?.Replace(" (default)", "");
                    Config.Default.Certificate = normalized;
                    Config.Default.Save();
                }
            }
        }

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

                        Config.Default.Language = value.Code;
                        Config.Default.Save();
                    }
                }
            }
        }

        public SettingsViewModel(string cliPath)
        {
            AvailableLanguages = new ObservableCollection<LanguageOption>(GetAvailableLanguages());
            Debug.WriteLine($"cliPath: {cliPath}");
            foreach (var cert in GetAvailableCertificates(cliPath))
                AvailableCertificates.Add(cert);

            var savedLangCode = Config.Default.Language ?? "en";
            _selectedLanguage = AvailableLanguages.FirstOrDefault(lang => lang.Code == savedLangCode)
                              ?? AvailableLanguages.FirstOrDefault(lang => lang.Code == "en");
            SelectedCertificate = Config.Default.Certificate ?? "Jelly2Sams";

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

        private List<string> GetAvailableCertificates(string profilePath)
        {
            var certificates = new List<string>();
            string defaultCert = "Jelly2Sams";


            List<string> profileNames = new();

            if (File.Exists(profilePath))
            {
                try
                {
                    var doc = XDocument.Load(profilePath);

                    profileNames = doc.Root?
                        .Elements("profile")
                        .Select(p => p.Attribute("name")?.Value)
                        .Where(name => !string.IsNullOrEmpty(name))
                        .ToList() ?? new List<string>();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading profile.xml: {ex.Message}");
                }
            }

            if (!profileNames.Contains(defaultCert))
                certificates.Add($"{defaultCert} (default)");

            certificates.AddRange(profileNames);

            return certificates;
        }
    }
}