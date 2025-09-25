using Avalonia.Controls;
using Jellyfin2SamsungCrossOS.Services;
using Jellyfin2SamsungCrossOS.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS;

public partial class IpInputDialog : Window
{
    public IpInputDialogViewModel ViewModel { get; }

    public IpInputDialog()
    {
        InitializeComponent();

        var loc = App.Services.GetRequiredService<ILocalizationService>();

        ViewModel = new IpInputDialogViewModel(loc, confirmed => Close());
        DataContext = ViewModel;
    }

    public async Task<string?> ShowDialogAsync(Window parent)
    {
        await ShowDialog(parent);
        return ViewModel.EnteredIp;
    }
}
