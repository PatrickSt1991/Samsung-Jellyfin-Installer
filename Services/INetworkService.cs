using Samsung_Jellyfin_Installer.Models;

namespace Samsung_Jellyfin_Installer.Services;

public interface INetworkService
{
    Task<IEnumerable<NetworkDevice>> GetLocalTizenAddresses(CancellationToken cancellationToken = default);
    Task<NetworkDevice?> ValidateManualTizenAddress(string ip, CancellationToken cancellationToken = default);
    Task<bool> IsPortOpenAsync(string ip, int port, CancellationToken ct);
    string GetLocalIPAddress();
    string InvertIPAddress(string ipAddress);
}