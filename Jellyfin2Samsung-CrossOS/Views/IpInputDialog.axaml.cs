using Avalonia.Controls;
using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace Jellyfin2Samsung;

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
