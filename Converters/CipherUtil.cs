using System;
using System.Security.Cryptography;
using System.Text;

namespace Samsung_Jellyfin_Installer.Converters
{
    public static class CipherUtil
    {
        private const string KeyString = "KYANINYLhijklmnopqrstuvwx"; // 26 chars, use first 24 bytes only
        private static readonly byte[] KeyBytes = Encoding.UTF8.GetBytes(KeyString).Take(24).ToArray();


        public static string GetEncryptedString(string plainText)
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
        public static string GetDecryptedString(string encryptedBase64)
        {
            byte[] keyBytes = Encoding.UTF8.GetBytes(KeyString);
            byte[] encryptedBytes = Convert.FromBase64String(encryptedBase64);

            using var tripleDes = TripleDES.Create();
            tripleDes.Mode = CipherMode.ECB;
            tripleDes.Padding = PaddingMode.PKCS7;
            tripleDes.Key = keyBytes;

            using var decryptor = tripleDes.CreateDecryptor();
            byte[] decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        public static string GenerateRandomPassword(int length = 12)
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

    }
}
