using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jellyfin2Samsung.Interfaces;
using System;
using System.Diagnostics;


namespace Jellyfin2Samsung.ViewModels
{
    public partial class InstallationCompleteViewModel : ObservableObject
    {
        private readonly ILocalizationService _localization;
        
        public InstallationCompleteViewModel(ILocalizationService localization)
        {
            _localization = localization;
        }

        public string Title => _localization.GetString("InstallationSuccessful");
        public string SuccessMessage => _localization.GetString("InstallationSuccessfulOn");
        public string EasyRight => _localization.GetString("lbleasyRight");
        public string Validation => _localization.GetString("lblValidation");
        public string Close => _localization.GetString("btn_Close");

        [RelayCommand]
        private async void OpenKoFi()
        {
            try
            {
                var url = "https://ko-fi.com/patrickst";
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Failed to open URL: {ex}");
            }
            CloseWindow();
        }

        [RelayCommand]
        private void CloseWindow()
        {
            if (Application.Current.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        public event EventHandler? RequestClose;
    }
}
