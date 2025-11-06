using Avalonia.Platform;
using Jellyfin2Samsung.Helpers;
using Jellyfin2Samsung.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace Jellyfin2Samsung.Services
{
    public class LocalizationService : ILocalizationService
    {
        private Dictionary<string, string> _currentStrings = new();
        private readonly Dictionary<string, Dictionary<string, string>> _allStrings = new();
        private string _currentLanguage = "en";

        public string CurrentLanguage => _currentLanguage;
        public IEnumerable<string> AvailableLanguages => _allStrings.Keys;
        public event EventHandler? LanguageChanged;

        public LocalizationService()
        {
            LoadLanguagesAsync();
        }

        private void LoadLanguagesAsync()
        {
            var languages = new[] { "en", "da", "nl", "fr", "de" };

            foreach (var lang in languages)
            {
                try
                {
                    var uri = new Uri($"avares://Jellyfin2Samsung/Assets/Localization/{lang}.json");
                    var asset = AssetLoader.Open(uri);

                    using var reader = new StreamReader(asset);
                    var json = reader.ReadToEnd();
                    var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

                    if (strings != null)
                    {
                        _allStrings[lang] = strings;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load language {lang}: {ex.Message}");
                }
            }

            // Set initial language based on system culture
            var systemLang = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
            string configLang = AppSettings.Default.Language;

            if (string.IsNullOrEmpty(configLang))
            {
                if (_allStrings.ContainsKey(systemLang))
                {
                    SetLanguage(systemLang);
                }
                else
                {
                    SetLanguage("en");
                }
            }
            else
            {
                SetLanguage(configLang);
            }
        }

        public string GetString(string key)
        {
            if (_currentStrings.TryGetValue(key, out var value))
            {
                return value;
            }

            // Fallback to English if key not found in current language
            if (_currentLanguage != "en" && _allStrings.TryGetValue("en", out var englishStrings))
            {
                if (englishStrings.TryGetValue(key, out var englishValue))
                {
                    return englishValue;
                }
            }

            // Return the key itself if no translation found
            return key;
        }

        public void SetLanguage(string languageCode)
        {
            if (_allStrings.TryGetValue(languageCode, out var strings))
            {
                _currentLanguage = languageCode;
                _currentStrings = strings;
                LanguageChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}