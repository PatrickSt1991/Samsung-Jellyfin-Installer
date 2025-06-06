﻿namespace Samsung_Jellyfin_Installer.Models;

public class NetworkDevice
{
    public required string IpAddress { get; set; }
    public string? Manufacturer { get; set; }
    public string? DeviceName { get; set; }

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