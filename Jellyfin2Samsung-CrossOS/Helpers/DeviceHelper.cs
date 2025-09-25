using Jellyfin2SamsungCrossOS.Models;
using Jellyfin2SamsungCrossOS.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Helpers
{
    public class DeviceHelper
    {
        private readonly INetworkService _networkService;
        private readonly IDialogService _dialogService;
        private readonly HttpClient _httpClient;

        public DeviceHelper(
            INetworkService networkService,
            IDialogService dialogService,
            HttpClient httpClient)
        {
            _networkService = networkService;
            _dialogService = dialogService;
            _httpClient = httpClient;
        }

        public async Task<NetworkDevice> GetDeveloperInfoAsync(NetworkDevice device)
        {
            try
            {
                string url = $"http://{device.IpAddress}:8001/api/v2/";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                string jsonContent = await response.Content.ReadAsStringAsync();
                JObject jsonObject = JObject.Parse(jsonContent);

                return new NetworkDevice
                {
                    IpAddress = jsonObject["device"]?["ip"]?.ToString(),
                    DeviceName = jsonObject["device"]?["name"]?.ToString(),
                    ModelName = jsonObject["device"]?["modelName"].ToString(),
                    Manufacturer = jsonObject["device"]?["type"]?.ToString(),
                    DeveloperMode = jsonObject["device"]?["developerMode"]?.ToString() ?? string.Empty,
                    DeveloperIP = jsonObject["device"]?["developerIP"]?.ToString() ?? string.Empty
                };
            }
            catch (HttpRequestException ex)
            {
                await _dialogService.ShowErrorAsync(
                    $"Error connecting to Samsung TV at {device.IpAddress}: {ex.Message}");
            }
            catch (JsonException ex)
            {
                await _dialogService.ShowErrorAsync(
                    $"Error parsing JSON response: {ex.Message}");
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    $"Unexpected error: {ex.Message}");
            }

            return new NetworkDevice
            {
                IpAddress = device.IpAddress,
                DeviceName = device.DeviceName,
                Manufacturer = device.Manufacturer,
                DeveloperMode = string.Empty,
                DeveloperIP = string.Empty
            };
        }

        public async Task<List<NetworkDevice>> ScanForDevicesAsync(CancellationToken cancellationToken = default, bool virtualScan = false)
        {
            var devices = new List<NetworkDevice>();

            var networkDevices = await _networkService.GetLocalTizenAddresses(cancellationToken, virtualScan);

            foreach (NetworkDevice device in networkDevices)
            {
                if (await _networkService.IsPortOpenAsync(device.IpAddress, 8001, cancellationToken))
                {
                    try
                    {
                        var samsungDevice = await GetDeveloperInfoAsync(device);
                        Debug.WriteLine(samsungDevice.DeveloperIP);
                        Debug.WriteLine(samsungDevice.DeviceName);
                        Debug.WriteLine(samsungDevice.DeveloperMode);
                        if (!string.IsNullOrEmpty(samsungDevice.DeviceName))
                            devices.Add(samsungDevice);
                    }
                    catch
                    {
                    }
                }
            }

            return devices;
        }
    }
}
