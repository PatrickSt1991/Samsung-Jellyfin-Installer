using Samsung_Jellyfin_Installer.Localization;
using Samsung_Jellyfin_Installer.ViewModels;
using System.Globalization;
using System.Reflection;

namespace Samsung_Jellyfin_Installer.Services
{
    public class LocalizedStrings : ViewModelBase
    {
        private static LocalizedStrings _instance;
        private CultureInfo _currentCulture;

        public static LocalizedStrings Instance => _instance ??= new LocalizedStrings();

        public CultureInfo CurrentCulture
        {
            get => _currentCulture;
            private set => SetField(ref _currentCulture, value);
        }

        private LocalizedStrings()
        {
            _currentCulture = CultureInfo.CurrentUICulture;
        }

        public string this[string key]
        {
            get
            {
                // Use explicit culture from our property
                var value = Strings.ResourceManager.GetString(key, CurrentCulture);

                return value ?? $"#{key}#"; // Makes missing keys visible
            }
        }

        public void ChangeLanguage(string cultureCode)
        {
            try
            {
                var newCulture = new CultureInfo(cultureCode);

                // Update static cultures
                CultureInfo.DefaultThreadCurrentCulture = newCulture;
                CultureInfo.DefaultThreadCurrentUICulture = newCulture;

                // Update our instance culture
                CurrentCulture = newCulture;

                // Clear all resource caches
                ClearResourceCache();

                // Notify all listeners
                OnPropertyChanged(string.Empty); // Refresh all bindings
            }
            catch (CultureNotFoundException)
            {
                ChangeLanguage("en");
            }
        }

        private void ClearResourceCache()
        {
            // More reliable cache clearing
            var resourceManager = Strings.ResourceManager;
            var method = resourceManager.GetType().GetMethod("InternalGetResourceSet",
                        BindingFlags.NonPublic | BindingFlags.Instance);

            method?.Invoke(resourceManager, new object[] { CurrentCulture, false, true });
        }
    }
}
