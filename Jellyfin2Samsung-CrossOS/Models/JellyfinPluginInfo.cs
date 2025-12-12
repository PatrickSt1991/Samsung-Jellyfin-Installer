using System;
using System.Collections.Generic;

namespace Jellyfin2Samsung.Models
{
    public class JellyfinPluginInfo
    {
        public string Name { get; set; }
        public string Id { get; set; }
    }

    public class PluginMatrixEntry
    {
        public string Name { get; set; }
        public string IdContains { get; set; }
        public string ServerPath { get; set; }
        public List<string> ExplicitServerFiles { get; set; }
        public List<string> FallbackUrls { get; set; }
        public bool UseBabel { get; set; }
        public bool RequiresModuleBundle { get; set; }
        public string ModuleRepoApiRoot { get; set; }
        public string ModuleBundleFileName { get; set; }
    }
    public class ExtractedDomBlocks
    {
        public List<string> HeadInjectBlocks { get; set; } = new();
        public List<string> BodyInjectBlocks { get; set; } = new();
    }
}