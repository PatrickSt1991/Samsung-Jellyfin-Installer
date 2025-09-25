using Jellyfin2SamsungCrossOS.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

namespace Jellyfin2SamsungCrossOS.Helpers
{
    public class CertificateHelper
    {
        public List<ExistingCertificates> GetAvailableCertificates(string profilePath, string tizenCrypto)
        {
            var certificates = new List<ExistingCertificates>();
            var cipherUtil = new CipherUtil();

            certificates.Add(new ExistingCertificates
            {
                Name = "Jelly2Sams (default)",
                File = null, // null indicates this needs to be created
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

                    var authorItem = profile.Elements("profileitem")
                        .FirstOrDefault(p => p.Attribute("distributor")?.Value == "0");

                    string? keyPath = authorItem?.Attribute("key")?.Value;
                    string? encryptedPassword = authorItem.Attribute("password")?.Value;
                    DateTime? expireDate = null;
                    string? decryptedPassword = null;

                    if (!string.IsNullOrWhiteSpace(keyPath) && File.Exists(keyPath) && (!string.IsNullOrEmpty(encryptedPassword)))
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
                            certificates.Add(new ExistingCertificates
                            {
                                Name = name,
                                File = keyPath,
                                ExpireDate = expireDate
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

        public static bool IsBase64String(string s)
        {
            s = s.Trim();
            return (s.Length % 4 == 0) &&
                   System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z0-9\+/]*={0,2}$");
        }
    }
}
