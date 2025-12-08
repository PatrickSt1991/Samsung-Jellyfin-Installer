using System;

namespace Jellyfin2Samsung.Models
{
    public class JellyfinPluginInfo
    {
        public string Name { get; set; }
        public string Version { get; set; }
        public string ConfigurationFileName { get; set; }
        public string Description { get; set; }
        public string Id { get; set; }
        public bool CanUninstall { get; set; }
        public bool HasImage { get; set; }
        public string Status { get; set; }
    }
}