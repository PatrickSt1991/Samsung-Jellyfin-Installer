namespace Samsung_Jellyfin_Installer.Models;

public class NetworkDevice
{
    public required string IpAddress { get; set; }
    public string? Manufacturer { get; set; }

    public string DisplayText => string.IsNullOrEmpty(Manufacturer) ? IpAddress : $"{IpAddress} ({Manufacturer})";
}