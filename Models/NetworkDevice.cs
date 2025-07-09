namespace Samsung_Jellyfin_Installer.Models;

public class NetworkDevice
{
    public required string IpAddress { get; set; }
    public string? Manufacturer { get; set; }
    public string? DeviceName { get; set; }
    public string? DeveloperMode { get; set; }
    public string? DeveloperIP { get; set; }

    public string DisplayText
    {
        get
        {
            if (DeviceName is not null)
            {
                return $"{IpAddress} ({DeviceName})";
            }

            if (Manufacturer is not null)
            {
                return $"{IpAddress} ({Manufacturer})";
            }

            return IpAddress;
        }
    }
}
public class ExtensionEntry
{
    public int Index;
    public string Name = "";
    public bool Activated;
}