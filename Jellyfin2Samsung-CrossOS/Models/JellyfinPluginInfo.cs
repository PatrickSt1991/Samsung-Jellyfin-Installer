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
        public string Name { get; set; }               // Display name from /Plugins
        public string IdContains { get; set; }         // Lowercase substring of plugin Id
        public string ServerPath { get; set; }         // e.g. "/JellyfinEnhanced/script"
        public List<string> FallbackUrls { get; set; } // CDN / GitHub raw URLs
        public bool UseBabel { get; set; }             // Wrap with Babel + WaitForApiClient
        public bool RequiresModuleBundle { get; set; }
        public string ModuleRepoApiRoot { get; set; }      // GitHub API "contents" root for js tree
        public string ModuleBundleFileName { get; set; }   // e.g. "enhanced.modules.bundle.js"

    }
}