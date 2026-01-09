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
        public List<string> ExplicitServerFiles { get; set; }
        public List<string> FallbackUrls { get; set; }
        public bool UseBabel { get; set; }
        public string RawRoot { get; set; }
    }
    public class ExtractedDomBlocks
    {
        public List<string> HeadInjectBlocks { get; set; } = new();
        public List<string> BodyInjectBlocks { get; set; } = new();
    }
}