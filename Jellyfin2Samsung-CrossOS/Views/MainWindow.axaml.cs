using Avalonia.Controls;
using Jellyfin2SamsungCrossOS.ViewModels;

namespace Jellyfin2SamsungCrossOS.Views
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