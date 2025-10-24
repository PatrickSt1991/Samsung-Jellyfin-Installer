using Avalonia.Controls;
using Jellyfin2Samsung.ViewModels;

namespace Jellyfin2Samsung.Views
{
    public partial class BuildInfoWindow : Window
    {
        private readonly BuildInfoViewModel _vm = new();

        public BuildInfoWindow()
        {
            InitializeComponent();
            DataContext = _vm;
            _vm.OnRequestClose += Close;
        }
    }
}
