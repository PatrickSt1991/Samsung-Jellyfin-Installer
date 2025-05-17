using Samsung_Jellyfin_Installer.Models;

namespace Samsung_Jellyfin_Installer.Services;

public interface INetworkService
{
    public Task<IEnumerable<NetworkDevice>> GetLocalTizenAddresses();
}