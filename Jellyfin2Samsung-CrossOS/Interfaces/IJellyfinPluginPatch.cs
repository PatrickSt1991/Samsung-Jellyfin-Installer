using Jellyfin2Samsung.Helpers.Jellyfin.Plugins;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Interfaces
{
    public interface IJellyfinPluginPatch
    {
        //string PluginName { get; }
        Task ApplyAsync(PluginPatchContext ctx);
    }

}
