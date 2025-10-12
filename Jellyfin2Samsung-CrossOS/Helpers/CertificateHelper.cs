using Jellyfin2Samsung.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading.Tasks;

namespace Jellyfin2Samsung.Helpers
{
    public class CertificateHelper
    {
        public List<ExistingCertificates> GetAvailableCertificates(string certificateFolders)
        {
            var certificates = new List<ExistingCertificates>();
            var cipherUtil = new CipherUtil();
            List<string> duids = new List<string>();

            // Default item
            certificates.Add(new ExistingCertificates
            {
                Name = "Jelly2Sams (default)",
                Duid = string.Empty,
                File = null,
                ExpireDate = null
            });

            if (!Directory.Exists(certificateFolders))
                return certificates;

            
                var p12Files = Directory.GetFiles(
                    certificateFolders,
                    "author.p12",
                    SearchOption.AllDirectories);

                foreach(var p12Path in p12Files)
                {
                    var directory = Path.GetDirectoryName(p12Path);
                    if (directory == null)
                        continue;

                    var passwordPath = Path.Combine(directory, "password.txt");
                    if (!File.Exists(passwordPath))
                        continue;

                    var password = File.ReadAllText(passwordPath).Trim();
                    if (string.IsNullOrWhiteSpace(password))
                        continue;
                try
                {
                    var cert = new X509Certificate2(
                        p12Path,
                        password,
                        X509KeyStorageFlags.Exportable);

                    if(cert.NotAfter.Date >= DateTime.Today)
                    {
                        certificates.Add(new ExistingCertificates
                        {
                            Name = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
                            File = p12Path,
                            ExpireDate = cert.NotAfter,
                            Duid = string.Empty
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to load certificate '{p12Path}': {ex.Message}");
                }
            }

            return certificates;
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
