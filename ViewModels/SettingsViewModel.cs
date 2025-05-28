using Samsung_Jellyfin_Installer.Models;
using Samsung_Jellyfin_Installer.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace Samsung_Jellyfin_Installer.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private LanguageOption _selectedLanguage;

        public ObservableCollection<LanguageOption> AvailableLanguages { get; }

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
                        // Change language in your localization system
                        LocalizedStrings.Instance.ChangeLanguage(value.Code);
                        // Persist the selection
                        Config.Default.Language = value.Code;
                        Config.Default.Save();
                    }
                }
            }
        }

        public SettingsViewModel()
        {
            AvailableLanguages = new ObservableCollection<LanguageOption>(GetAvailableLanguages());

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
    }
}