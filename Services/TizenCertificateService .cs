using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Samsung_Jellyfin_Installer.Converters;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace Samsung_Jellyfin_Installer.Services
{
    public class TizenCertificateService(HttpClient httpClient) : ITizenCertificateService
    {
        private readonly HttpClient _httpClient = httpClient;


        public async Task<(string p12Location, string p12Password)> GenerateProfileAsync(string duid, string accessToken, string userId, string outputPath, Action<string> updateStatus)
        {
            updateStatus("OutputDir".Localized());
            Directory.CreateDirectory(outputPath);

            updateStatus("SettingsCaCerts".Localized());
            var caPath = Path.Combine(outputPath, "ca");
            Directory.CreateDirectory(caPath);

            updateStatus("GenPassword".Localized());
            string p12Plain = CipherUtil.GenerateRandomPassword();
            string p12Encrypted = CipherUtil.GetEncryptedString(p12Plain);

            var passwordFilePath = Path.Combine(outputPath, "password.txt");
            await File.WriteAllTextAsync(passwordFilePath, p12Plain);

            updateStatus("GenKeyPair".Localized());
            var keyPair = GenerateKeyPair();

            updateStatus("CreateAuthorCsr".Localized());
            var authorCsrData = GenerateAuthorCsr(keyPair);
            var authorCsrPath = Path.Combine(outputPath, "author.csr");
            await File.WriteAllBytesAsync(authorCsrPath, authorCsrData);

            updateStatus("CreateDistributorCSR".Localized());
            var distributorCsrData = GenerateDistributorCsr(keyPair, duid);
            var distributorCsrPath = Path.Combine(outputPath, "distributor.csr");
            await File.WriteAllBytesAsync(distributorCsrPath, distributorCsrData);

            updateStatus("PostAuthorCSR".Localized());
            var signedAuthorCsrBytes = await PostAuthorCsrAsync(authorCsrData, accessToken, userId);
            var signedAuthorCsrPath = Path.Combine(outputPath, "signed_author.cer");
            await File.WriteAllBytesAsync(signedAuthorCsrPath, signedAuthorCsrBytes);


            updateStatus("PostFirstDistributorCSR".Localized());
            var profileXmlBytes = await PostCsrV1Async(accessToken, userId, distributorCsrData);
            var profileXmlPath = Path.Combine(outputPath, "device-profile.xml");
            await File.WriteAllBytesAsync(profileXmlPath, profileXmlBytes);

            updateStatus("PostSecondDistributorCSR".Localized());
            var signedDistributorCsrBytes = await PostCsrV2Async(accessToken, userId, distributorCsrData, distributorCsrPath, outputPath);
            var signedDistributorCsrPath = Path.Combine(outputPath, "signed_distributor.cer");
            await File.WriteAllBytesAsync(signedDistributorCsrPath, signedDistributorCsrBytes);

            updateStatus("CreateNewCertificates".Localized());
            await ExportPfxWithCaChainAsync(signedAuthorCsrBytes, keyPair.Private, p12Plain, outputPath, caPath, "author", "author_ca.cer");
            await ExportPfxWithCaChainAsync(signedDistributorCsrBytes, keyPair.Private, p12Plain, outputPath, caPath, "distributor", "public2.crt");

            updateStatus("MovingP12Files".Localized());
            string p12Location = MoveTizenCertificateFiles();

            return (p12Location, p12Encrypted);
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
                "Jelly2Sams"
        }   ;

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

        private static byte[] GenerateDistributorCsr(AsymmetricCipherKeyPair keyPair, string duid)
        {
            // Build subject: CN=TizenSDK
            var subject = new X509Name(new List<DerObjectIdentifier> { X509Name.CN }, new List<string> { "TizenSDK" });

            // Create SAN URIs as GeneralNames - Empty package ID like Python version
            var sanUris = new List<GeneralName>
        {
            new GeneralName(GeneralName.UniformResourceIdentifier, "URN:tizen:packageid="),
            new GeneralName(GeneralName.UniformResourceIdentifier, $"URN:tizen:deviceid={duid}")
            //new GeneralName(GeneralName.UniformResourceIdentifier, $"URN:tizen:deviceid:{duid}")
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
            var request = new HttpRequestMessage(HttpMethod.Post, Settings.Default.AuthorEndpoint);

            var content = new MultipartFormDataContent
        {
            { new StringContent(accessToken), "access_token" },
            { new StringContent(userId), "user_id" },
            { new StringContent("VD"), "platform"},
            { new ByteArrayContent(csrData), "csr", "author.csr" }
        };

            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }

        private async Task<byte[]> PostCsrV1Async(string accessToken, string userId, byte[] csrBytes)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, Settings.Default.DistributorsEndpoint_V1);

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
            var request = new HttpRequestMessage(HttpMethod.Post, Settings.Default.DistributorsEndpoint_V2);

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

        private static async Task ExportPfxWithCaChainAsync(byte[] signedCertBytes, AsymmetricKeyParameter privateKey, string password, string outputPath, string caPath, string filename, string caFile)
        {
            string caCertFile = Path.Combine(caPath, caFile);

            var caCertBytes = await File.ReadAllBytesAsync(caCertFile);

            var chainBytes = CombineCertificates(signedCertBytes, caCertBytes);

            var parser = new X509CertificateParser();
            var certificates = new List<X509Certificate2>();

            using (var ms = new MemoryStream(chainBytes))
            using (var reader = new StreamReader(ms))
            {
                var pemReader = new PemReader(reader);
                object pemObject;

                while ((pemObject = pemReader.ReadObject()) != null)
                {
                    if (pemObject is Org.BouncyCastle.X509.X509Certificate bcCert)
                    {
                        var cert = new X509Certificate2(bcCert.GetEncoded());
                        certificates.Add(cert);
                    }
                }
            }

            if (certificates.Count == 0)
                throw new Exception("No certificates found in chain");

            var rsaPrivateKey = DotNetUtilities.ToRSA((RsaPrivateCrtKeyParameters)privateKey);

            var endEntityCert = certificates[0];

            using var certWithPrivateKey = endEntityCert.CopyWithPrivateKey(rsaPrivateKey);

            var certCollection = new X509Certificate2Collection();
            certCollection.Add(certWithPrivateKey);

            for (int i = 1; i < certificates.Count; i++)
                certCollection.Add(certificates[i]);

            var pfxBytes = certCollection.Export(X509ContentType.Pkcs12, password);
            var pfxPath = Path.Combine(outputPath, $"{filename}.p12");
            await File.WriteAllBytesAsync(pfxPath, pfxBytes);

            foreach (var cert in certificates)
                cert.Dispose();
        }

        private static byte[] CombineCertificates(byte[] signedCertBytes, byte[] caCertBytes)
        {
            using var ms = new MemoryStream();

            ms.Write(signedCertBytes);

            if (signedCertBytes.Length > 0 && signedCertBytes[signedCertBytes.Length - 1] != (byte)'\n')
                ms.WriteByte((byte)'\n');

            ms.Write(caCertBytes);

            return ms.ToArray();
        }

        public static string MoveTizenCertificateFiles()
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string destinationFolder = Path.Combine(userHome, "SamsungCertificate", "Jelly2Sams");

            if (Directory.Exists(destinationFolder))
            {
                foreach (string file in Directory.GetFiles(destinationFolder))
                    File.Delete(file);

                foreach (string subDirectory in Directory.GetDirectories(destinationFolder))
                    Directory.Delete(subDirectory, recursive: true);
            }
            else
            {
                Directory.CreateDirectory(destinationFolder);
            }

            string sourceFolder = Path.Combine(Environment.CurrentDirectory, "TizenProfile");

            string[] fileExtensions = { "*.xml", "*.pri", "*.p12", "*.pwd", "*.csr", "*.crt", "*.cer", "*.txt" };

            foreach (var pattern in fileExtensions)
            {
                string[] files = Directory.GetFiles(sourceFolder, pattern);
                foreach (var file in files)
                {
                    string destFile = Path.Combine(destinationFolder, Path.GetFileName(file));
                    File.Move(file, destFile, overwrite: true);
                }
            }
            return destinationFolder;
        }
    }
}