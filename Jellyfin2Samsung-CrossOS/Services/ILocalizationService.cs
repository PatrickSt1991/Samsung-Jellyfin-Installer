using System;
using System.Collections.Generic;

namespace Jellyfin2SamsungCrossOS.Services
{
    public interface ILocalizationService
    {
        string GetString(string key);
        void SetLanguage(string languageCode);
        string CurrentLanguage { get; }
        IEnumerable<string> AvailableLanguages { get; }
        event EventHandler? LanguageChanged;
    }
}
