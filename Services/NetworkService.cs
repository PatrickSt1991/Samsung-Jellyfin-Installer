using Samsung_Jellyfin_Installer.Models;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Samsung_Jellyfin_Installer.Services;

public class NetworkService : INetworkService
{
    private readonly ITizenInstallerService _tizenInstaller;
    private static readonly HttpClient _httpClient = new HttpClient();
    private static readonly HashSet<string> _excludedInterfacePatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "VirtualBox", "Loopback", "Docker", "Hyper-V",
        "vEthernet", "VPN", "Bluetooth", "vSwitch"
    };
    private const int tvPort = 26101;
    private const int scanTimeoutMs = 1000;

    public NetworkService(ITizenInstallerService tizenInstaller)
    {
        _tizenInstaller = tizenInstaller;
    }

    public async Task<IEnumerable<NetworkDevice>> GetLocalTizenAddresses(CancellationToken cancellationToken = default)
    {
        return await FindTizenTvsAsync(cancellationToken);
    }

    public async Task<NetworkDevice?> ValidateManualTizenAddress(string ip, CancellationToken cancellationToken = default)
    {
        try
        {
            using var cts = new CancellationTokenSource(scanTimeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                cts.Token, cancellationToken);

            if (await IsPortOpenAsync(ip, tvPort, linkedCts.Token))
            {
                if (await IsPortOpenAsync(ip, 8001, linkedCts.Token))
                {
                    var manufacturer = await GetManufacturerFromIp(ip);
                    var device = new NetworkDevice
                    {
                        IpAddress = ip,
                        Manufacturer = manufacturer
                    };

                    if (manufacturer?.Contains("Samsung", StringComparison.OrdinalIgnoreCase) == true)
                        device.DeviceName = await _tizenInstaller.GetTvNameAsync(ip);

                    return device;
                }
                return null;
            }

            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ValidateManualTizenAddress] Error validating IP '{ip}': {ex.Message}");
            return null;
        }
    }

    public async Task<IEnumerable<NetworkDevice>> FindTizenTvsAsync(CancellationToken cancellationToken = default)
    {
        var foundDevices = new List<NetworkDevice>();
        var localIps = GetRelevantLocalIPs();
        var lockObject = new object();

        // Group by network prefix to avoid scanning the same network multiple times
        var uniqueNetworks = localIps
            .Select(ip => GetNetworkPrefix(ip))
            .Distinct()
            .ToList();

        await Task.WhenAll(uniqueNetworks.SelectMany(networkPrefix =>
            Enumerable.Range(1, 254)
                .Select(i => $"{networkPrefix}.{i}")
                .Select(async ip =>
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(scanTimeoutMs);
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                            cts.Token, cancellationToken);
                        if (await IsPortOpenAsync(ip, tvPort, linkedCts.Token))
                        {
                            var manufacturer = await GetManufacturerFromIp(ip);
                            var device = new NetworkDevice
                            {
                                IpAddress = ip,
                                Manufacturer = manufacturer
                            };
                            lock (lockObject)
                            {
                                foundDevices.Add(device);
                            }
                            if (manufacturer?.Contains("Samsung", StringComparison.OrdinalIgnoreCase) == true)
                            {
                                device.DeviceName = await _tizenInstaller.GetTvNameAsync(ip);
                            }
                        }
                    }
                    catch { /* Ignore scan failures */ }
                })));

        Debug.WriteLine($"Scan complete! Found {foundDevices.Count} devices with port {tvPort} open.");
        return foundDevices;
    }
    private IEnumerable<IPAddress> GetRelevantLocalIPs()
    {
        var baseIps = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
            .Where(ni => !_excludedInterfacePatterns.Any(p =>
                ni.Name.Contains(p, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
            .Where(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork)
            .Where(ip => !IPAddress.IsLoopback(ip.Address))
            .Select(ip => ip.Address.ToString()); // Convert to string first

        var additionalIps = Enumerable.Empty<string>();
        if (Settings.Default.RememberCustomIP && !string.IsNullOrEmpty(Settings.Default.UserCustomIP))
        {
            try
            {
                // Validate it's a valid IP by parsing, then use the string
                IPAddress.Parse(Settings.Default.UserCustomIP);
                additionalIps = new[] { Settings.Default.UserCustomIP };
            }
            catch (FormatException)
            {
                additionalIps = Enumerable.Empty<string>();
            }
        }

        return baseIps.Concat(additionalIps)
            .Distinct()
            .Select(IPAddress.Parse); // Convert back to IPAddress
    }
    public async Task<bool> IsPortOpenAsync(string ip, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(ip, port, ct);
            var timeoutTask = Task.Delay(Timeout.Infinite, ct);

            var completedTask = await Task.WhenAny(connectTask.AsTask(), timeoutTask);
            if (completedTask == connectTask.AsTask())
            {
                await connectTask; // Ensure connection succeeded
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private string GetNetworkPrefix(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return $"{bytes[0]}.{bytes[1]}.{bytes[2]}";
    }

    public static async Task<string?> GetManufacturerFromIp(string ipAddress)
    {
        string? macAddress = await GetMacAddressFromIp(ipAddress);
        return string.IsNullOrEmpty(macAddress)
            ? null
            : await GetManufacturerFromMac(macAddress);
    }

    private static async Task<string?> GetMacAddressFromIp(string ipAddress)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = $"-a {ipAddress}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            var match = Regex.Match(output, @"([0-9A-Fa-f]{2}[:-]){5}([0-9A-Fa-f]{2})");
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> GetManufacturerFromMac(string macAddress)
    {
        try
        {
            string oui = macAddress
                .Replace(":", "")
                .Replace("-", "")
                .Substring(0, 6)
                .ToUpper();

            return await _httpClient.GetStringAsync($"https://api.macvendors.com/{oui}");
        }
        catch
        {
            return null;
        }
    }
    public string GetLocalIPAddress()
    {
        using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
        {
            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString();
        }
    }
    public string InvertIPAddress(string ipAddress)
    {
        var parts = ipAddress.Split('.');
        if (parts.Length != 4) throw new FormatException("Invalid IPv4 address.");
        Array.Reverse(parts);
        return string.Join(".", parts);
    }

}