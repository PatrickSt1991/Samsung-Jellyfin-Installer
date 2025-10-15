using Avalonia.Controls;
using Jellyfin2Samsung.ViewModels;

namespace Jellyfin2Samsung.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            this.Opened += async (_, __) =>
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    await vm.InitializeAsync();
                }
            };
        }
    }
}