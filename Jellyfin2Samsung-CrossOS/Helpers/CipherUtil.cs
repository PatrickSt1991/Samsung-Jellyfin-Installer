using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Helpers
{
    public class CipherUtil
    {
        private const string FallbackKeyString = "KYANINYLhijklmnopqrstuvwx";
        private string _usedPassword = FallbackKeyString;

        private byte[] KeyBytes => Encoding.UTF8.GetBytes(_usedPassword).Take(24).ToArray();

        public async Task<string> ExtractPasswordAsync(string jarPath)
        {
            string? extracted = await TryExtractFromJarAsync(jarPath);
            if (!string.IsNullOrEmpty(extracted))
            {
                _usedPassword = extracted;
                return extracted;
            }

            _usedPassword = FallbackKeyString;
            return FallbackKeyString;
        }

        private async Task<string?> TryExtractFromJarAsync(string jarPath)
        {
            try
            {
                var jarFiles = Directory.GetFiles(jarPath, "*.jar");
                foreach (var jar in jarFiles)
                {
                    string fileName = Path.GetFileName(jar);
                    if (!fileName.StartsWith("org.tizen.common.cert") || !fileName.EndsWith(".jar"))
                        continue;

                    using var fs = File.OpenRead(jar);
                    using var ms = new MemoryStream();
                    await fs.CopyToAsync(ms);
                    ms.Position = 0;

                    using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
                    foreach (var entry in zip.Entries)
                    {
                        if (!entry.FullName.EndsWith("CipherUtil.class", StringComparison.OrdinalIgnoreCase))
                            continue;

                        using var classStream = entry.Open();
                        var password = ExtractPasswordFromClassSimple(classStream);
                        if (!string.IsNullOrEmpty(password))
                            return password;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cipher extraction failed: {ex.Message}");
            }

            return null;
        }

        private string? ExtractPasswordFromClassSimple(Stream classStream)
        {
            try
            {
                using var reader = new StreamReader(classStream);
                string content = reader.ReadToEnd();

                string knownPassword = FallbackKeyString;
                int index = content.IndexOf(knownPassword, StringComparison.Ordinal);

                if (index != -1)
                {
                    string extracted = content.Substring(index, knownPassword.Length);
                    if (extracted.All(char.IsLetterOrDigit) && extracted.Length == 26)
                        return extracted;
                }
            }
            catch { }

            return null;
        }

        public string GetEncryptedString(string plainText)
        {
            using var tdes = new TripleDESCryptoServiceProvider
            {
                Key = KeyBytes,
                Mode = CipherMode.ECB,
                Padding = PaddingMode.PKCS7
            };

            var data = Encoding.UTF8.GetBytes(plainText);
            byte[] encrypted = tdes.CreateEncryptor().TransformFinalBlock(data, 0, data.Length);
            return Convert.ToBase64String(encrypted);
        }


        public string GetDecryptedString(string encryptedBase64)
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);

            using var tripleDes = TripleDES.Create();
            tripleDes.Key = KeyBytes;
            tripleDes.Mode = CipherMode.ECB;
            tripleDes.Padding = PaddingMode.PKCS7;

            byte[] decrypted = tripleDes.CreateDecryptor().TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
            return Encoding.UTF8.GetString(decrypted);
        }

        public string GenerateRandomPassword(int length = 12)
        {
            if (length < 8)
                throw new ArgumentException("Password must be at least 8 characters long.");

            const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lower = "abcdefghijklmnopqrstuvwxyz";
            const string digits = "0123456789";
            const string all = upper + lower + digits;

            var randomBytes = new byte[length];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);

            var chars = new char[length];
            chars[0] = upper[randomBytes[0] % upper.Length];
            chars[1] = lower[randomBytes[1] % lower.Length];
            chars[2] = digits[randomBytes[2] % digits.Length];

            for (int i = 3; i < length; i++)
                chars[i] = all[randomBytes[i] % all.Length];

            return new string(chars.OrderBy(_ => Guid.NewGuid()).ToArray());
        }

        public string RunWincryptDecrypt(string filePath, string cryptoPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = cryptoPath,
                Arguments = $"--decrypt \"{filePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)!;
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Split(new[] { "PASSWORD:" }, StringSplitOptions.None)[1].Trim();
        }
    }
}
