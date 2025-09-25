using System;
using System.IO;

namespace Jellyfin2SamsungCrossOS.Helpers
{
    public class OperatingSystemHelper
    {
        public string GetInstallPath()
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (OperatingSystem.IsWindows())
            {
                return Path.Combine(baseDir, "Programs", "TizenStudioCli");
            }
            else if (OperatingSystem.IsLinux())
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "TizenStudioCli");
            }
            else if (OperatingSystem.IsMacOS())
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "TizenStudioCli");
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported OS");
            }
        }

    }
}
