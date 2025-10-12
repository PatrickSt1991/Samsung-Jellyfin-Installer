using Avalonia.Controls;
using Jellyfin2Samsung.ViewModels;

namespace Jellyfin2Samsung;

public partial class InstallationCompleteWindow : Window
{
    public InstallationCompleteWindow(InstallationCompleteViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}