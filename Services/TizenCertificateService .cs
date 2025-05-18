using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Samsung_Jellyfin_Installer.Services;


public class TizenCertificateService : ITizenCertificateService
{
    private readonly HttpClient _httpClient;

    public TizenCertificateService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task GenerateDistributorProfileAsync(string duid, string accessToken, string userId, string outputPath, Action<string> updateStatus)
    {
        // Ensure output directory exists
        Directory.CreateDirectory(outputPath);

        // 1. Generate key pair and CSR with DUID
        var keyPair = GenerateKeyPair();
        var csrBytes = GenerateCsr(keyPair, duid);

        var csrPath = Path.Combine(outputPath, "distributor.csr");
        await File.WriteAllBytesAsync(csrPath, csrBytes);

        // 2. POST to /v1/distributors to get deviceProfile.xml
        var profileXmlBytes = await PostCsrV1Async(accessToken, userId, csrBytes);
        var profileXmlPath = Path.Combine(outputPath, "deviceProfile.xml");
        await File.WriteAllBytesAsync(profileXmlPath, profileXmlBytes);

        // 3. POST to /v2/distributors to get signed CSR (certificate)
        var signedCsrBytes = await PostCsrV2Async(accessToken, userId, csrBytes, csrPath, outputPath);
        var signedCsrPath = Path.Combine(outputPath, "signed_distributor.cer");
        await File.WriteAllBytesAsync(signedCsrPath, signedCsrBytes);

        // 4. Generate random password and export .p12
        var p12Password = GenerateRandomPassword();
        ExportPfx(signedCsrBytes, keyPair.Private, p12Password, outputPath);

        // 5. Save password to text file
        var passwordFilePath = Path.Combine(outputPath, "distributor_password.txt");
        await File.WriteAllTextAsync(passwordFilePath, p12Password);
    }

    private AsymmetricCipherKeyPair GenerateKeyPair()
    {
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        return keyGen.GenerateKeyPair();
    }

    private byte[] GenerateCsr(AsymmetricCipherKeyPair keyPair, string duid)
    {
        var attrs = new Dictionary<DerObjectIdentifier, string>
        {
            { X509Name.CN, duid }
        };
        var subject = new X509Name(new List<DerObjectIdentifier> { X509Name.CN }, new List<string> { duid });

        var csr = new Pkcs10CertificationRequest("SHA256WITHRSA", subject, keyPair.Public, null, keyPair.Private);

        return csr.GetDerEncoded();
    }

    private async Task<byte[]> PostCsrV1Async(string accessToken, string userId, byte[] csrBytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://dev.tizen.samsung.com/apis/v1/distributors");

        var content = new MultipartFormDataContent
        {
            { new StringContent(accessToken), "access_token" },
            { new StringContent(userId), "user_id" },
            { new StringContent("Public"), "privilege_level" },
            { new StringContent("Individual"), "developer_type" },
            { new StringContent("VD"), "platform" },
            { new ByteArrayContent(csrBytes), "csr", "distributor.csr" }
        };

        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync();
    }

    private async Task<byte[]> PostCsrV2Async(string accessToken, string userId, byte[] csrBytes, string csrFilePath, string outputPath)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://dev.tizen.samsung.com/apis/v2/distributors");

        var content = new MultipartFormDataContent
        {
            { new StringContent(accessToken), "access_token" },
            { new StringContent(userId), "user_id" },
            { new StringContent("Public"), "privilege_level" },
            { new StringContent("Individual"), "developer_type" },
            { new StringContent("VD"), "platform" }
        };

        var csrFileBytes = await File.ReadAllBytesAsync(csrFilePath);
        content.Add(new ByteArrayContent(csrFileBytes), "csr", "distributor.csr");

        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync();
    }

    private string GenerateRandomPassword()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(randomBytes);
    }

    private void ExportPfx(byte[] signedCertBytes, AsymmetricKeyParameter privateKey, string password, string outputPath)
    {
        // Convert BouncyCastle private key to RSA
        var rsaPrivateKey = DotNetUtilities.ToRSA((RsaPrivateCrtKeyParameters)privateKey);

        // Load the signed certificate
        var signedCert = new X509Certificate2(signedCertBytes);

        // Combine cert and private key
        using var finalCert = signedCert.CopyWithPrivateKey(rsaPrivateKey);

        var pfxBytes = finalCert.Export(X509ContentType.Pfx, password);
        var pfxPath = Path.Combine(outputPath, "distributor.p12");
        File.WriteAllBytes(pfxPath, pfxBytes);
    }
}
