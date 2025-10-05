using Jellyfin2SamsungCrossOS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Jellyfin2SamsungCrossOS.Helpers
{
    public class CertificateHelper
    {
        public List<ExistingCertificates> GetAvailableCertificates(string profilePath, string tizenCrypto)
        {
            var certificates = new List<ExistingCertificates>();
            var cipherUtil = new CipherUtil();

            // Default item
            certificates.Add(new ExistingCertificates
            {
                Name = "Jelly2Sams (default)",
                Duid = string.Empty,
                File = null,
                ExpireDate = null
            });

            if (!File.Exists(profilePath))
                return certificates;

            try
            {
                var doc = XDocument.Load(profilePath);
                var profiles = doc.Root?.Elements("profile");

                if (profiles == null)
                    return certificates;

                foreach (var profile in profiles)
                {
                    string? name = profile.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // Author Certificate
                    var authorItem = profile.Elements("profileitem")
                        .FirstOrDefault(p => p.Attribute("distributor")?.Value == "0");

                    string? keyPath = authorItem?.Attribute("key")?.Value;
                    string? encryptedPassword = authorItem?.Attribute("password")?.Value;
                    DateTime? expireDate = null;
                    string? decryptedPassword = null;

                    if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath) && !string.IsNullOrEmpty(encryptedPassword))
                    {
                        if (File.Exists(encryptedPassword))
                            decryptedPassword = cipherUtil.RunWincryptDecrypt(encryptedPassword, tizenCrypto);
                        else if (IsBase64String(encryptedPassword))
                            decryptedPassword = cipherUtil.GetDecryptedString(encryptedPassword);
                        else
                            continue;

                        try
                        {
                            var cert = new X509Certificate2(keyPath, decryptedPassword, X509KeyStorageFlags.Exportable);
                            expireDate = cert.NotAfter;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to read certificate '{keyPath}': {ex.Message}");
                        }

                        if (expireDate.HasValue && expireDate.Value.Date >= DateTime.Today)
                        {
                            // Retrieve distributor certificate for DUID
                            string duid = ExtractDistributorDuid(profile, cipherUtil, tizenCrypto);

                            certificates.Add(new ExistingCertificates
                            {
                                Name = name,
                                File = keyPath,
                                ExpireDate = expireDate,
                                Duid = duid
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading profile XML: {ex.Message}");
            }

            return certificates;
        }
        private string ExtractDistributorDuid(XElement profile, CipherUtil cipherUtil, string tizenCrypto)
        {
            List<string> duids = new List<string>();

            var distributorItem = profile.Elements("profileitem")
                .FirstOrDefault(p => p.Attribute("distributor")?.Value == "1");

            if (distributorItem == null)
                return string.Empty;

            string? keyPath = distributorItem.Attribute("key")?.Value;
            string? encryptedPassword = distributorItem.Attribute("password")?.Value;
            string? decryptedPassword = null;

            if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath) && !string.IsNullOrEmpty(encryptedPassword))
            {
                if (File.Exists(encryptedPassword))
                    decryptedPassword = cipherUtil.RunWincryptDecrypt(encryptedPassword, tizenCrypto);
                else if (IsBase64String(encryptedPassword))
                    decryptedPassword = cipherUtil.GetDecryptedString(encryptedPassword);

                try
                {
                    var distributorCert = new X509Certificate2(keyPath, decryptedPassword, X509KeyStorageFlags.Exportable);
                    foreach (var ext in distributorCert.Extensions)
                    {
                        var raw = ext.Format(true);
                        foreach (Match match in Regex.Matches(raw, @"URN:tizen:deviceid=([A-Za-z0-9]+)"))
                        {
                            string duid = match.Groups[1].Value;
                            if (!duids.Contains(duid))
                                duids.Add(duid);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to read distributor certificate '{keyPath}': {ex.Message}");
                }
            }

            return string.Join(",", duids); // ✅ Return comma-separated list
        }
        public static bool IsBase64String(string s)
        {
            s = s.Trim();
            return (s.Length % 4 == 0) &&
                   Regex.IsMatch(s, @"^[A-Za-z0-9\+/]*={0,2}$");
        }
        public async Task HandleErrorResponse(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new Exception("You've made too many requests in a given amount of time.\nPlease wait and try your request again later.");
            }

            try
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(errorContent))
                {
                    var errorJson = JsonSerializer.Deserialize<JsonElement>(errorContent);
                    if (errorJson.TryGetProperty("error", out var errorObj))
                    {
                        var name = errorObj.TryGetProperty("name", out var nameEl) ? nameEl.ToString() : "";
                        var status = errorObj.TryGetProperty("status", out var statusEl) ? statusEl.ToString() : "";
                        var code = errorObj.TryGetProperty("code", out var codeEl) ? codeEl.ToString() : "";
                        var description = errorObj.TryGetProperty("description", out var descEl) ? descEl.ToString() : "";

                        throw new Exception($"Samsung API Error - Name: {name}, Status: {status}, Code: {code}, Description: {description}");
                    }
                }
            }
            catch (JsonException)
            {
            }

            throw new Exception($"Server response code: {response.StatusCode}");
        }
    }
}
