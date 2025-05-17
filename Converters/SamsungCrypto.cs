using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using Samsung_Jellyfin_Installer.Models;
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public static class SamsungCrypto
{
    private static string _publicKey;
    private static bool _initialized = false;
    private static int _pbeKeySpecIterations = 1000; // Default value, will be updated from HTML

    public static void InitializeFromHtml(string html)
    {
        // Extract wipEnc object
        var wipEncMatch = Regex.Match(html, @"var\s+wipEnc\s*=\s*({[^{}]+})", RegexOptions.Singleline);
        if (!wipEncMatch.Success)
            throw new Exception("Could not find wipEnc object in HTML");

        var wipEncJson = wipEncMatch.Groups[1].Value;
        _publicKey = GetJsonValue(wipEncJson, "pblcKyTxt");

        // Extract iteration count
        var iterationsStr = GetJsonValue(wipEncJson, "pbeKySpcIters");
        if (!string.IsNullOrEmpty(iterationsStr) && int.TryParse(iterationsStr, out int iterations))
            _pbeKeySpecIterations = iterations;

        if (string.IsNullOrEmpty(_publicKey))
            throw new Exception("Failed to extract valid public key from wipEnc");

        Debug.WriteLine($"Public key initialized: {_publicKey}");
        Debug.WriteLine($"Using iterations: {_pbeKeySpecIterations}");
        _initialized = true;
    }

    public static EncryptedPasswordData EncryptCredentials(string email, string password, string oldPassword = null)
    {
        if (!_initialized)
            throw new InvalidOperationException("Crypto module not initialized!");

        // Clean input like in JavaScript
        email = email?.ToLower().Replace(" ", "");
        password = password?.Replace(" ", "");

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
            throw new ArgumentException("Email and password are required");

        // 1. Create SHA256 hash of email (similar to CryptoJS.SHA256(n).toString())
        byte[] emailBytes = Encoding.UTF8.GetBytes(email);
        byte[] emailHashBytes;
        using (SHA256 sha256 = SHA256.Create())
        {
            emailHashBytes = sha256.ComputeHash(emailBytes);
        }
        string emailHashHex = BitConverter.ToString(emailHashBytes).Replace("-", "").ToLower();

        // 2. Generate random salt (similar to CryptoJS.lib.WordArray.random(16))
        byte[] salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(salt);
        }

        // 3. Derive key using PBKDF2 (similar to CryptoJS.PBKDF2())
        byte[] derivedKey = new Rfc2898DeriveBytes(
            emailHashHex,
            salt,
            _pbeKeySpecIterations,
            HashAlgorithmName.SHA1).GetBytes(16); // 4 keySize = 16 bytes (128 bits)

        // 4. Generate random IV
        byte[] iv = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(iv);
        }

        // 5. Encrypt password
        byte[] encryptedPassword = EncryptAes(Encoding.UTF8.GetBytes(password), derivedKey, iv);

        // 6. Encrypt old password if provided
        byte[] encryptedOldPassword = null;
        if (!string.IsNullOrEmpty(oldPassword))
        {
            oldPassword = oldPassword.Replace(" ", "");
            encryptedOldPassword = EncryptAes(Encoding.UTF8.GetBytes(oldPassword), derivedKey, iv);
        }

        // 7. RSA encrypt the derived key
        byte[] encryptedKey;
        using (var rsa = DecodePublicKey(_publicKey))
        {
            encryptedKey = rsa.Encrypt(derivedKey, RSAEncryptionPadding.Pkcs1);
        }

        // Create result object
        var result = new EncryptedPasswordData
        {
            Email = email,
            EncryptedPassword = Convert.ToBase64String(encryptedPassword),
            Key = Convert.ToBase64String(encryptedKey),
            IV = BitConverter.ToString(iv).Replace("-", "").ToLower()
        };

        if (encryptedOldPassword != null)
        {
            result.EncryptedOldPassword = Convert.ToBase64String(encryptedOldPassword);
        }

        return result;
    }

    private static byte[] EncryptAes(byte[] data, byte[] key, byte[] iv)
    {
        using (var aes = Aes.Create())
        {
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7; // Change to PKCS7 padding like CryptoJS

            using (var encryptor = aes.CreateEncryptor())
            {
                return encryptor.TransformFinalBlock(data, 0, data.Length);
            }
        }
    }

    private static RSA DecodePublicKey(string publicKeyPem)
    {
        publicKeyPem = publicKeyPem.Replace("-----BEGIN PUBLIC KEY-----", "")
                                   .Replace("-----END PUBLIC KEY-----", "")
                                   .Trim();
        byte[] keyBytes = Convert.FromBase64String(publicKeyPem);
        AsymmetricKeyParameter publicKey = PublicKeyFactory.CreateKey(keyBytes);
        return DotNetUtilities.ToRSA((RsaKeyParameters)publicKey);
    }

    private static string GetJsonValue(string json, string key)
    {
        var match = Regex.Match(json, $@"""{key}""\s*:\s*""?([^""\s,}}]+)""?");
        return match.Success ? match.Groups[1].Value.Trim('"') : null;
    }
}
