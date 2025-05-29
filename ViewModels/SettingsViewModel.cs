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
                _selectedCertificate = value;
                OnPropertyChanged();
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
            var certificates = new List<string>
            {
                "Use application default",
            };

            if (!File.Exists(profilePath))
                return certificates;

            try
            {
                var doc = XDocument.Load(profilePath);

                var profileNames = doc.Root?
                    .Elements("profile")
                    .Select(p => p.Attribute("name")?.Value)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList();

                if (profileNames != null)
                {
                    certificates.AddRange(profileNames);
                }
            }
            catch (Exception ex)
            {
                // Optionally log the error
                Debug.WriteLine($"Error reading profile.xml: {ex.Message}");
            }

            return certificates;
        }

}
}