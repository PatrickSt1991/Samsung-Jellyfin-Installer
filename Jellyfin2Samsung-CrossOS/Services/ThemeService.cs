using Avalonia;
using Avalonia.Styling;
using Jellyfin2Samsung.Helpers;
using Jellyfin2Samsung.Interfaces;
using System;

namespace Jellyfin2Samsung.Services
{
    public class ThemeService : IThemeService
    {
        public bool IsDarkMode => AppSettings.Default.DarkMode;

        public event EventHandler<bool>? ThemeChanged;

        public void SetTheme(bool isDarkMode)
        {
            if (AppSettings.Default.DarkMode == isDarkMode)
                return;

            AppSettings.Default.DarkMode = isDarkMode;
            AppSettings.Default.Save();

            ApplyTheme();
            ThemeChanged?.Invoke(this, isDarkMode);
        }

        public void ApplyTheme()
        {
            if (Application.Current is null)
                return;

            Application.Current.RequestedThemeVariant = AppSettings.Default.DarkMode
                ? ThemeVariant.Dark
                : ThemeVariant.Light;
        }
    }
}
