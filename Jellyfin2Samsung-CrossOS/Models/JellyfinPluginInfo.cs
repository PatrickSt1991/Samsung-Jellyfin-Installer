using System;
using System.Collections.Generic;

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

    public class PluginMatrixEntry
    {
        public string Name { get; set; }               // display name from /Plugins
        public string IdContains { get; set; }         // optional substring in Id
        public string ServerPath { get; set; }         // relative path on server, e.g. "/web/Plugins/CustomTabs/customtabs.js"
        public List<string> FallbackUrls { get; set; } // CDN / GitHub raw URLs
        public bool UseBabel { get; set; }             // whether to wrap in Babel + WaitForApiClient
    }
}