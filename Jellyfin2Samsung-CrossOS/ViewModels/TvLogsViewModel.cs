using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Services;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.ViewModels;

public partial class TvLogsViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    private readonly TvLogService _logService;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private string logs = string.Empty;

    public string IpAddress { get; }
    public string StartLog => _localizationService.GetString("lblStartLog");
    public string StopLog => _localizationService.GetString("lblStopLog");
    public string SaveLogFile => _localizationService.GetString("lblSaveLogs");
    public string Close => _localizationService.GetString("btn_Close");
    public TvLogsViewModel(
        TvLogService logService,
        string ipAddress,
        ILocalizationService localizationService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        IpAddress = ipAddress ?? throw new ArgumentNullException(nameof(ipAddress));
        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshLocalizedProperties();
    }

    private void RefreshLocalizedProperties()
    {
        OnPropertyChanged(nameof(StartLog));
        OnPropertyChanged(nameof(StopLog));
        OnPropertyChanged(nameof(SaveLogFile));
        OnPropertyChanged(nameof(Close));
    }

    [RelayCommand]
    private void Start()
    {
        Logs = "Waiting for TV connection...\n";

        _logService.StartLogServer(5001, line =>
        {
            Dispatcher.UIThread.Post(() => Logs += line);
        });
    }

    [RelayCommand]
    private async Task SaveLogs()
    {
        try
        {
            var dialog = new Avalonia.Controls.SaveFileDialog
            {
                Title = "Save TV Logs",
                Filters =
            {
                new Avalonia.Controls.FileDialogFilter
                {
                    Name = "Text Files",
                    Extensions = { "txt" }
                }
            },
                DefaultExtension = "txt",
                InitialFileName = $"tv_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            var window = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;

            if (window == null)
                return;

            var result = await dialog.ShowAsync(window);
            if (string.IsNullOrWhiteSpace(result))
                return;

            await File.WriteAllTextAsync(result, Logs ?? string.Empty);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save logs: {ex.Message}");
        }
    }
    [RelayCommand]
    private void Stop()
    {
        _cts?.Cancel();
        Logs += "\n--- Log stream stopped ---\n";
    }
}