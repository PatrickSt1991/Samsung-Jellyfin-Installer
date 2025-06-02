using Samsung_Jellyfin_Installer.Localization;
using Samsung_Jellyfin_Installer.ViewModels;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;

namespace Samsung_Jellyfin_Installer.Services
{
    public class LocalizedStrings : ViewModelBase
    {
        private static LocalizedStrings _instance;
        public static LocalizedStrings Instance => _instance ??= new LocalizedStrings();

        public event PropertyChangedEventHandler PropertyChanged;

        private LocalizedStrings() { }

        public string this[string key]
        {
            get
            {
                try
                {
                    return Strings.ResourceManager.GetString(key) ?? key;
                }
                catch
                {
                    return key;
                }
            }
        }

        public void ChangeLanguage(string cultureCode)
        {
            try
            {
                var culture = new CultureInfo(cultureCode);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;

                ClearResourceCache();

                OnPropertyChanged("Item[]");
            }
            catch (CultureNotFoundException)
            {
                ChangeLanguage("en");
            }
        }

        private void ClearResourceCache()
        {
            try
            {
                var resourceSetsField = typeof(ResourceManager).GetField("_resourceSets",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (resourceSetsField?.GetValue(Strings.ResourceManager) is System.Collections.Hashtable resourceSets)
                {
                    resourceSets.Clear();
                }
            }
            catch
            {
                //return nothing
            }
        }
    }
}
