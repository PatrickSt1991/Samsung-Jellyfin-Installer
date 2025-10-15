using Avalonia.Controls;
using Jellyfin2Samsung.ViewModels;

namespace Jellyfin2Samsung;

public partial class JellyfinConfigView : Window
{
    public JellyfinConfigView(JellyfinConfigViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}