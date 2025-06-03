using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Samsung_Jellyfin_Installer.Converters
{
    public class CipherUtil
    {
        private const string FallbackKeyString = "KYANINYLhijklmnopqrstuvwx"; // 26 chars
        private string _usedPassword = FallbackKeyString;
        private byte[] KeyBytes => Encoding.UTF8.GetBytes(_usedPassword).Take(24).ToArray();

        public async Task<string?> ExtractPasswordAsync(string jarPath)
        {
            var jarFiles = Directory.GetFiles(jarPath, "*.jar");

            foreach (var jar in jarFiles)
            {
                var fileName = Path.GetFileName(jar);

                if (fileName.Contains("org.tizen.common_") && fileName.EndsWith(".jar"))
                {
                    using var fileStream = File.OpenRead(jar);
                    using var msJar = new MemoryStream();
                    await fileStream.CopyToAsync(msJar);
                    msJar.Position = 0;

                    using var jarZip = new ZipArchive(msJar, ZipArchiveMode.Read);

                    foreach (var entry in jarZip.Entries)
                    {
                        if (entry.FullName.EndsWith("CipherUtil.class"))
                        {
                            using var classStream = entry.Open();
                            var extracted = ExtractPasswordFromClass(classStream);

                            if (!string.IsNullOrEmpty(extracted))
                            {
                                _usedPassword = extracted;
                                return extracted;
                            }
                        }
                    }
                }
            }

            _usedPassword = FallbackKeyString;
            return null;
        }

        private string? ExtractPasswordFromClass(Stream classFileStream)
        {
            using var reader = new BinaryReader(classFileStream);

            // Skip header: magic, minor version, major version
            reader.ReadBytes(8);

            ushort constantPoolCount = ReadBigEndianUInt16(reader);
            var constants = new List<(string Type, string Value)>();

            for (int i = 1; i < constantPoolCount; i++)
            {
                byte tag = reader.ReadByte();

                switch (tag)
                {
                    case 1: // CONSTANT_Utf8
                        ushort length = ReadBigEndianUInt16(reader);
                        byte[] bytes = reader.ReadBytes(length);
                        string value = Encoding.UTF8.GetString(bytes);
                        constants.Add(("Utf8", value));
                        break;

                    case 3:
                    case 4:
                    case 9:
                    case 10:
                    case 11:
                    case 12:
                    case 18:
                        reader.ReadBytes(4);
                        break;

                    case 5:
                    case 6:
                        reader.ReadBytes(8);
                        i++; // occupies two entries
                        break;

                    case 7:
                    case 8:
                    case 16:
                        reader.ReadBytes(2);
                        break;

                    case 15:
                        reader.ReadBytes(3);
                        break;

                    default:
                        throw new InvalidOperationException($"Unknown constant pool tag {tag}");
                }
            }

            // Look for 'password' field and a likely value nearby
            for (int i = 0; i < constants.Count - 1; i++)
            {
                if (constants[i].Value == "password")
                {
                    for (int j = i + 1; j < constants.Count; j++)
                    {
                        var val = constants[j].Value;
                        if (val.Length >= 16 && val.Length <= 32 && val.All(char.IsLetterOrDigit))
                        {
                            return val;
                        }
                    }
                }
            }

            return null;
        }

        private ushort ReadBigEndianUInt16(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(2);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            return BitConverter.ToUInt16(bytes, 0);
        }

        public string GetEncryptedString(string plainText)
        {
            using var tdes = new TripleDESCryptoServiceProvider
            {
                Key = KeyBytes,
                Mode = CipherMode.ECB,
                Padding = PaddingMode.PKCS7
            };

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBytes = tdes.CreateEncryptor().TransformFinalBlock(plainBytes, 0, plainBytes.Length);

            return Convert.ToBase64String(encryptedBytes);
        }

        public string GetDecryptedString(string encryptedBase64)
        {
            byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);

            using var tripleDes = TripleDES.Create();
            tripleDes.Mode = CipherMode.ECB;
            tripleDes.Padding = PaddingMode.PKCS7;
            tripleDes.Key = KeyBytes;

            using var decryptor = tripleDes.CreateDecryptor();
            byte[] decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }

        public string GenerateRandomPassword(int length = 12)
        {
            if (length < 8)
                throw new ArgumentException("Password length must be at least 8 characters.");

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
            {
                chars[i] = all[randomBytes[i] % all.Length];
            }

            // Shuffle to avoid predictable positions
            return new string(chars.OrderBy(_ => Guid.NewGuid()).ToArray());
        }
        public string RunWincryptDecrypt(string filePath, string cryptoPath)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = cryptoPath,
                Arguments = $"--decrypt \"{filePath}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(processInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                return output.Split(new[] { "PASSWORD:" }, StringSplitOptions.None)[1].Trim();
            }
        }
        
    }
}
