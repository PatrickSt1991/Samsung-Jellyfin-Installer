using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Models;
using System;

namespace Jellyfin2Samsung.ViewModels
{
    public partial class UpdateDialogViewModel : ViewModelBase
    {
        private readonly ILocalizationService _localizationService;

        [ObservableProperty]
        private string currentVersion = string.Empty;

        [ObservableProperty]
        private string latestVersion = string.Empty;

        [ObservableProperty]
        private string releaseTitle = string.Empty;

        [ObservableProperty]
        private string releaseNotes = string.Empty;

        [ObservableProperty]
        private DateTime? publishedAt;

        [ObservableProperty]
        private bool hasDownloadUrl;

        [ObservableProperty]
        private bool isDownloading;

        [ObservableProperty]
        private int downloadProgress;

        [ObservableProperty]
        private string downloadStatus = string.Empty;

        /// <summary>
        /// The user's choice after interacting with the dialog.
        /// </summary>
        public UpdateDialogChoice? DialogResult { get; private set; }

        /// <summary>
        /// Event raised when the dialog should be closed.
        /// </summary>
        public event EventHandler? RequestClose;

        // Localized strings
        public string L(string key) => _localizationService.GetString(key);
        public string DialogTitle => L("UpdateAvailable");
        public string CurrentVersionLabel => L("UpdateCurrentVersion");
        public string LatestVersionLabel => L("UpdateLatestVersion");
        public string ReleaseNotesLabel => L("UpdateReleaseNotes");
        public string ManualButtonText => L("UpdateManual");
        public string AutomaticButtonText => L("UpdateAutomatic");
        public string SkipButtonText => L("UpdateSkip");
        public string DownloadingText => L("UpdateDownloading");

        public UpdateDialogViewModel(ILocalizationService localizationService)
        {
            _localizationService = localizationService;
        }

        public void Initialize(UpdateCheckResult updateInfo)
        {
            CurrentVersion = updateInfo.CurrentVersion;
            LatestVersion = updateInfo.LatestVersion;
            ReleaseTitle = updateInfo.ReleaseTitle;
            ReleaseNotes = updateInfo.ReleaseNotes;
            PublishedAt = updateInfo.PublishedAt;
            HasDownloadUrl = !string.IsNullOrEmpty(updateInfo.DownloadUrl);
        }

        [RelayCommand]
        private void SelectManual()
        {
            DialogResult = UpdateDialogChoice.Manual;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand(CanExecute = nameof(CanSelectAutomatic))]
        private void SelectAutomatic()
        {
            DialogResult = UpdateDialogChoice.Automatic;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void SelectSkip()
        {
            DialogResult = UpdateDialogChoice.Skip;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void Cancel()
        {
            DialogResult = UpdateDialogChoice.Cancel;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        private bool CanSelectAutomatic() => HasDownloadUrl && !IsDownloading;

        public void UpdateDownloadProgress(int progress, string status)
        {
            DownloadProgress = progress;
            DownloadStatus = status;
        }
    }
}
