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
using Org.BouncyCastle.Utilities;
using Samsung_Jellyfin_Installer.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows;


public class TizenCertificateService(HttpClient httpClient) : ITizenCertificateService
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task GenerateProfileAsync(string duid, string accessToken, string userId, string outputPath, Action<string> updateStatus)
    {
        // 1. Ensure output directory exists
        Directory.CreateDirectory(outputPath);

        // 2. Generate random password
        var p12Password = GenerateRandomPassword();
        var passwordFilePath = Path.Combine(outputPath, "password.txt");
        await File.WriteAllTextAsync(passwordFilePath, p12Password);

        // 3. Generate keypair
        var keyPair = GenerateKeyPair();
        
        // 4. Create Author CSR
        var authorCsrData = GenerateAuthorCsr(keyPair);
        var authorCsrPath = Path.Combine(outputPath, "author.csr");
        await File.WriteAllBytesAsync(authorCsrPath, authorCsrData);
        
        // 5. Generate Distributor CSR with DUID
        var distributorCsrData = GenerateDistributorCsr(keyPair, duid);
        var distributorCsrPath = Path.Combine(outputPath, "distributor.csr");
        await File.WriteAllBytesAsync(distributorCsrPath, distributorCsrData);

        // 6.  POST to /v2/authors
        var signedAuthorCsrBytes = await PostAuthorCsrAsync(authorCsrData, accessToken, userId);

        // 7. POST to /v1/distributors to get deviceProfile.xml
        var profileXmlBytes = await PostCsrV1Async(accessToken, userId, distributorCsrData);
        var profileXmlPath = Path.Combine(outputPath, "deviceProfile.xml");
        await File.WriteAllBytesAsync(profileXmlPath, profileXmlBytes);

        // 8. POST to /v2/distributors to get signed CSR (certificate)
        var signedDistributorCsrBytes = await PostCsrV2Async(accessToken, userId, distributorCsrData, distributorCsrPath, outputPath);
        var signedDistributorCsrPath = Path.Combine(outputPath, "signed_distributor.cer");
        await File.WriteAllBytesAsync(signedDistributorCsrPath, signedDistributorCsrBytes);

        // 9. Export p12 files
        ExportPfx(signedAuthorCsrBytes, keyPair.Private, p12Password, outputPath, "author");
        ExportPfx(signedDistributorCsrBytes, keyPair.Private, p12Password, outputPath, "distributor");

        // 10. Copy files to be used in certificate-manager
        MoveTizenCertificateFiles();

    }
    private static AsymmetricCipherKeyPair GenerateKeyPair()
    {
        var keyGen = new RsaKeyPairGenerator();
        keyGen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        return keyGen.GenerateKeyPair();
    }
    private static byte[] GenerateAuthorCsr(AsymmetricCipherKeyPair keyPair)
    {
        var oids = new List<DerObjectIdentifier>
    {
        X509Name.CN
    };
        var values = new List<string>
    {
        "Jelly2Sams"  // Common Name
    };

        var subject = new X509Name(oids, values);

        var csr = new Pkcs10CertificationRequest("SHA256WITHRSA", subject, keyPair.Public, null, keyPair.Private);

        using (var writer = new StreamWriter("author.csr"))
            new PemWriter(writer).WriteObject(csr);

        using (var writer = new StreamWriter("author.key"))
            new PemWriter(writer).WriteObject(keyPair.Private);

        using (var ms = new MemoryStream())
        using (var sw = new StreamWriter(ms))
        {
            var pemWriter = new PemWriter(sw);
            pemWriter.WriteObject(csr);
            sw.Flush();
            return ms.ToArray();
        }
    }
    private static byte[] GenerateDistributorCsrOld(AsymmetricCipherKeyPair keyPair, string duid)
    {
        // Build subject: CN=duid
        var subject = new X509Name(new List<DerObjectIdentifier> { X509Name.CN }, new List<string> { duid });

        // Create SAN URIs as GeneralNames
        var sanUris = new List<GeneralName>
    {
        // packageid is empty string
        new GeneralName(GeneralName.UniformResourceIdentifier, "URN:tizen:packageid="),
        // deviceid set to duid
        new GeneralName(GeneralName.UniformResourceIdentifier, $"URN:tizen:deviceid={duid}")
    };

        var subjectAlternativeNames = new DerSequence(sanUris.ToArray());

        // Create Extensions object with SAN extension (critical = false)
        var extensionsGenerator = new X509ExtensionsGenerator();
        extensionsGenerator.AddExtension(
            X509Extensions.SubjectAlternativeName,
            false,
            subjectAlternativeNames
        );

        var extensions = extensionsGenerator.Generate();

        // Create the Attribute for extensionRequest (OID 1.2.840.113549.1.9.14)
        var attribute = new AttributePkcs(
            PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
            new DerSet(extensions)
        );

        // Create CSR with extensions attribute
        var csr = new Pkcs10CertificationRequest(
            "SHA256WITHRSA",
            subject,
            keyPair.Public,
            new DerSet(attribute),
            keyPair.Private
        );

        return csr.GetDerEncoded();
    }
    private static byte[] GenerateDistributorCsr(AsymmetricCipherKeyPair keyPair, string duid)
    {
        // Build subject: CN=duid
        var subject = new X509Name(new List<DerObjectIdentifier> { X509Name.CN }, new List<string> { "TizenSDK" });

        // Create SAN URIs as GeneralNames
        var sanUris = new List<GeneralName>
    {
        new GeneralName(GeneralName.UniformResourceIdentifier, "URN:tizen:packageid="),
        new GeneralName(GeneralName.UniformResourceIdentifier, $"URN:tizen:deviceid={duid}")
    };

        var subjectAlternativeNames = new DerSequence(sanUris.ToArray());

        // Create Extensions object with SAN extension (critical = false)
        var extensionsGenerator = new X509ExtensionsGenerator();
        extensionsGenerator.AddExtension(
            X509Extensions.SubjectAlternativeName,
            false,
            subjectAlternativeNames
        );

        var extensions = extensionsGenerator.Generate();

        // Create the Attribute for extensionRequest (OID 1.2.840.113549.1.9.14)
        var attribute = new AttributePkcs(
            PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
            new DerSet(extensions)
        );

        // Create CSR with extensions attribute
        var csr = new Pkcs10CertificationRequest(
            "SHA256WITHRSA",
            subject,
            keyPair.Public,
            new DerSet(attribute),
            keyPair.Private
        );

        // Write to PEM file
        using (var writer = new StreamWriter("distributor.csr"))
            new PemWriter(writer).WriteObject(csr);

        // Return as byte[] in PEM format
        using (var ms = new MemoryStream())
        using (var sw = new StreamWriter(ms))
        {
            var pemWriter = new PemWriter(sw);
            pemWriter.WriteObject(csr);
            sw.Flush();
            return ms.ToArray();
        }
    }

    public async Task<byte[]> PostAuthorCsrAsync(byte[] csrData, string accessToken, string userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://dev.tizen.samsung.com/apis/v2/authors");

        var content = new MultipartFormDataContent
    {
        { new StringContent(accessToken), "access_token" },
        { new StringContent(userId), "user_id" },
        { new ByteArrayContent(csrData), "csr", "author.csr" }
    };

        request.Content = content;

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync();
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
    private static string GenerateRandomPassword()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(randomBytes);
    }
    private static void ExportPfx(byte[] signedCertBytes, AsymmetricKeyParameter privateKey, string password, string outputPath, string filename)
    {
        // Convert BouncyCastle private key to RSA
        var rsaPrivateKey = DotNetUtilities.ToRSA((RsaPrivateCrtKeyParameters)privateKey);

        // Load the signed certificate
        var signedCert = new X509Certificate2(signedCertBytes);

        // Combine cert and private key
        using var finalCert = signedCert.CopyWithPrivateKey(rsaPrivateKey);

        var pfxBytes = finalCert.Export(X509ContentType.Pfx, password);
        var pfxPath = Path.Combine(outputPath, $"{filename}.p12");
        File.WriteAllBytes(pfxPath, pfxBytes);
    }
    public static void MoveTizenCertificateFiles()
    {
        // Step 1: Get the current user's home directory
        string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Step 2: Create SamsungCertificate\Jelly2Sams
        string destinationFolder = Path.Combine(userHome, "SamsungCertificate", "Jelly2Sams");
        Directory.CreateDirectory(destinationFolder);

        // Step 3: Define source folder
        string sourceFolder = Path.Combine(Environment.CurrentDirectory, "TizenProfile");

        // Step 4: Define file extensions to move
        string[] fileExtensions = { "*.xml", "*.pwd", "*.pri", "*.p12", "*.csr", "*.crt" };

        foreach (var pattern in fileExtensions)
        {
            string[] files = Directory.GetFiles(sourceFolder, pattern);
            foreach (var file in files)
            {
                string destFile = Path.Combine(destinationFolder, Path.GetFileName(file));
                File.Move(file, destFile, overwrite: true);
                Debug.WriteLine($"Moved {file} -> {destFile}");
            }
        }
    }
}
