using Samsung_Jellyfin_Installer.Models;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public static class SamsungCrypto
{
    private static string _publicKey;
    private static string _encryptionType = "1"; // Default to RSA
    private static bool _initialized = false;

    public static void InitializeWithPublicKey(string publicKey = null)
    {
        _publicKey = publicKey ?? @"MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQCacdQbW2lQai4Lppj5bt4h+r6NmK4Vgd7i/+gqdNdeAgwvsEKPJGI1dekY7oKx81K+vaONU63qpDOKOdNZBRaln2kBoHDr2EQ2rgKH91xbjR8EZ//rtgzRkd5KGROkaZGtSstf6YnmPYPDCPIFbyx48QX/BJaocnSJ5xBFlDMmRQIDAQAB";
        _encryptionType = "1";
        _initialized = true;
    }

    public static EncryptedPasswordData EncryptPassword(string password)
    {
        if (!_initialized)
            throw new InvalidOperationException("Crypto module not initialized! Call InitializeWithPublicKey first.");

        using var aes = Aes.Create();
        aes.GenerateKey();
        aes.GenerateIV();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Encrypt password with AES
        byte[] encryptedPassword;
        using (var encryptor = aes.CreateEncryptor())
        using (var ms = new MemoryStream())
        {
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            using (var sw = new StreamWriter(cs, Encoding.UTF8))
            {
                sw.Write(password);
            }
            encryptedPassword = ms.ToArray();
        }

        // Encrypt AES key with RSA
        byte[] encryptedKey;
        using (var rsa = RSA.Create())
        {
            // Try both common public key formats
            try
            {
                rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(_publicKey), out _);
            }
            catch
            {
                try
                {
                    // Some Samsung servers might use PKCS#1 format
                    var pkcs1PubKey = Convert.FromBase64String(_publicKey);
                    rsa.ImportRSAPublicKey(pkcs1PubKey, out _);
                }
                catch (Exception ex)
                {
                    throw new Exception("Failed to import RSA public key", ex);
                }
            }

            encryptedKey = rsa.Encrypt(aes.Key, RSAEncryptionPadding.Pkcs1);
        }

        return new EncryptedPasswordData
        {
            EncryptedPassword = Convert.ToBase64String(encryptedPassword),
            Key = Convert.ToBase64String(encryptedKey),
            // Samsung appears to expect IV in Base64 (same as other fields)
            IV = Convert.ToBase64String(aes.IV)
        };
    }

    public static void InitializeFromHtml(string html)
    {
        // More robust regex to handle minified JS
        var wipEncMatch = Regex.Match(html, @"var\s+wipEnc\s*=\s*({[^{}]+})", RegexOptions.Singleline);
        if (!wipEncMatch.Success)
            throw new Exception("Could not find wipEnc object in HTML");

        var wipEncJson = wipEncMatch.Groups[1].Value;

        // Parse with more lenient JSON handling
        _publicKey = GetJsonValue(wipEncJson, "pblcKyTxt")
            ?? throw new Exception("Failed to extract public key from wipEnc");

        _encryptionType = GetJsonValue(wipEncJson, "lgnEncTp") ?? "1";
        _initialized = true;
    }

    private static string GetJsonValue(string json, string key)
    {
        // Handles both quoted and unquoted values
        var match = Regex.Match(json, $@"""{key}""\s*:\s*""?([^""\s,}}]+)""?");
        return match.Success ? match.Groups[1].Value.Trim('"') : null;
    }
}