using Jellyfin2Samsung.Interfaces;
using Jellyfin2Samsung.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers.API
{
    public class TizenApiClient
    {
        private readonly IDialogService _dialogService;
        private readonly HttpClient _httpClient;
        public TizenApiClient(
            HttpClient httpClient,
            IDialogService dialogService)
        {
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
                    DeviceName = WebUtility.HtmlDecode(jsonObject["device"]?["name"]?.ToString()),
                    ModelName = jsonObject["device"]?["modelName"].ToString(),
                    Manufacturer = jsonObject["device"]?["type"]?.ToString(),
                    DeveloperMode = jsonObject["device"]?["developerMode"]?.ToString() ?? string.Empty,
                    DeveloperIP = jsonObject["device"]?["developerIP"]?.ToString() ?? string.Empty
                };
            }
            catch (HttpRequestException ex)
            {
                await _dialogService.ShowErrorAsync(
                    $"Error connecting to Samsung TV at {device.IpAddress}: {ex}");
            }
            catch (JsonException ex)
            {
                await _dialogService.ShowErrorAsync(
                    $"Error parsing JSON response: {ex}");
            }
            catch (Exception ex)
            {
                await _dialogService.ShowErrorAsync(
                    $"Unexpected error: {ex}");
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
    }
}
