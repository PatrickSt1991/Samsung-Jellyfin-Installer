using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Samsung_Jellyfin_Installer.Models;

namespace Samsung_Jellyfin_Installer.Services;

public class NetworkService : INetworkService
{
    private readonly ITizenInstallerService _tizenInstaller;
    public NetworkService(ITizenInstallerService tizenInstaller)
    {
        _tizenInstaller = tizenInstaller;
    }
    private bool HasSamePrefix(IPAddress a, IPAddress b)
    {
        // Get the byte arrays from both IP addresses
        byte[] bytesA = a.GetAddressBytes();
        byte[] bytesB = b.GetAddressBytes();

        // For IPv4, check if the first 3 bytes are the same
        // (This assumes IPv4 addresses; additional handling would be needed for IPv6)
        return bytesA.Length == bytesB.Length &&
               bytesA.Length >= 3 &&
               bytesA[0] == bytesB[0] &&
               bytesA[1] == bytesB[1] &&
               bytesA[2] == bytesB[2];
    }

    private IEnumerable<IPAddress> GetLocalIpAddresses()
    {
        List<IPAddress> addresses = [];
        // Get all IPv4 addresses that aren't loopback addresses
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up) continue;
            foreach (UnicastIPAddressInformation ip in networkInterface.GetIPProperties().UnicastAddresses)
            {
                if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip.Address) && !addresses.Any(it => HasSamePrefix(it, ip.Address)))
                {
                    addresses.Add(ip.Address);
                }
            }
        }

        return addresses;
    }

    public async Task<IEnumerable<NetworkDevice>> GetLocalTizenAddresses()
    {
        var localAddresses = GetLocalIpAddresses();

        // Create a cancellation token source with a timeout
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMinutes(2)); // 2 minute timeout for the entire scan

        var tasks = new List<Task>();
        var foundDevices = new List<NetworkDevice>();
        var lockObject = new object();

        try
        {
            foreach (IPAddress localAddress in localAddresses)
            {
                Debug.WriteLine($"Local IP: {localAddress}");

                // Extract network prefix for scanning
                string[] ipParts = localAddress.ToString().Split('.');
                string networkPrefix = $"{ipParts[0]}.{ipParts[1]}.{ipParts[2]}";

                Debug.WriteLine($"Scanning network: {networkPrefix}.0/24 for devices with port 26101 open...");
                Debug.WriteLine("");

                // Scan all IPs in the subnet (1-254)
                for (var i = 1; i <= 254; i++)
                {
                    var ip = $"{networkPrefix}.{i}";
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            using var tcpClient = new TcpClient();
                            
                            // Set a short connection timeout
                            Task connectTask = tcpClient.ConnectAsync(ip, 26101);

                            if (await Task.WhenAny(connectTask, Task.Delay(1500)) == connectTask)
                            {
                                var manufacturer = await GetManufacturerFromIp(ip);
                                lock (lockObject)
                                {
                                    foundDevices.Add(new NetworkDevice
                                    {
                                        IpAddress = ip,
                                        Manufacturer = manufacturer
                                    });
                                    Debug.WriteLine($"Found device at {ip}:26101");
                                }
                            }
                        }
                        catch
                        {
                            // Connection failed, ignore
                        }
                    }, cts.Token));
                }
            }

            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("Scan was cancelled due to timeout.");
        }

        Debug.WriteLine("");
        Debug.WriteLine("Scan complete!");
        Debug.WriteLine($"Found {foundDevices.Count} devices with port 26101 open:");

        if (foundDevices.Count > 0)
        {
            foreach (NetworkDevice device in foundDevices)
            {
                Debug.WriteLine($"- {device.IpAddress}:26101");
                Debug.WriteLine($"Manufacturer: {device.Manufacturer}");
                
                await _tizenInstaller.ConnectToTvAsync(device.IpAddress);
                device.DeviceName = await _tizenInstaller.GetTvNameAsync(device.IpAddress);
            }
        }
        else
        {
            Debug.WriteLine("No devices found with port 26101 open.");
        }

        return foundDevices;
    }

    public static async Task<string?> GetManufacturerFromIp(string ipAddress)
    {
        // Get MAC address using ARP
        string? macAddress = await GetMacAddressFromIp(ipAddress);
        if (string.IsNullOrEmpty(macAddress))
            return null;

        // Look up manufacturer from MAC address
        return await GetManufacturerFromMac(macAddress);
    }

    private static async Task<string?> GetMacAddressFromIp(string ipAddress)
    {
        try
        {
            var process = new Process
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

            // Extract MAC address using regex
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
            // Use first 6 characters (OUI) of MAC address
            string oui = macAddress.Replace(":", "").Replace("-", "").Substring(0, 6).ToUpper();

            // Use an OUI lookup service or API
            using var client = new HttpClient();
            var response = await client.GetStringAsync($"https://api.macvendors.com/{oui}");
            return response;
        }
        catch
        {
            return null;
        }
    }
}