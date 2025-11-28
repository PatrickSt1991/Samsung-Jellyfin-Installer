using CommunityToolkit.Mvvm.ComponentModel;

namespace Jellyfin2Samsung.Models
{
    public partial class BuildVersion : ObservableObject
    {
        [ObservableProperty] private string fileName = string.Empty;
        [ObservableProperty] private string description = string.Empty;
    }
}
