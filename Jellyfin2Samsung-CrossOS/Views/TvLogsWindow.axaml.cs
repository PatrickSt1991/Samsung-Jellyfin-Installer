using Avalonia.Controls;

namespace Jellyfin2Samsung.Views;

public partial class TvLogsWindow : Window
{
    public TvLogsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close();
    }
}
