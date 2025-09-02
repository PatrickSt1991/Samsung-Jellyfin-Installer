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
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Services
{
    public class TizenCertificateService(HttpClient httpClient) : ITizenCertificateService
    {
        private readonly HttpClient _httpClient = httpClient;

        public async Task<(string p12Location, string p12Password)> GenerateProfileAsync(string duid, string accessToken, string userId, string outputPath, Action<string> updateStatus, string jarPath)
        {
            if (string.IsNullOrEmpty(jarPath))
            {
                string defaultJarPath = jarPath;
                string rootPath = Directory.GetParent(Directory.GetParent(jarPath).FullName).FullName;
                jarPath = Path.Combine(rootPath, "tools", "certificate-manager", "plugins");

                if (string.IsNullOrEmpty(jarPath) || !Directory.Exists(jarPath))
                {
                    throw new ArgumentException($"Default jarPath is null or empty: {defaultJarPath}, and fallback jarPath could not be found: {jarPath}", nameof(jarPath));
                }
            }

            if (string.IsNullOrEmpty(outputPath))
            {
                throw new ArgumentException("outputPath cannot be null or empty", nameof(outputPath));
            }

            var cipherUtil = new CipherUtil();
            await cipherUtil.ExtractPasswordAsync(jarPath);

            updateStatus("OutputDir".Localized());
            Directory.CreateDirectory(outputPath);

            updateStatus("SettingsCaCerts".Localized());
            var caPath = Path.Combine(outputPath, "ca");
            Directory.CreateDirectory(caPath);

            updateStatus("GenPassword".Localized());
            string p12Plain = cipherUtil.GenerateRandomPassword();
            string p12Encrypted = cipherUtil.GetEncryptedString(p12Plain);

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
            await ExtractRootCertificateAsync(jarPath);

            await CheckCertificateExistanceAsync(caPath);

            updateStatus("CreateNewCertificates".Localized());
            await ExportPfxWithCaChainAsync(signedAuthorCsrBytes, keyPair.Private, p12Plain, outputPath, caPath, "author", "vd_tizen_dev_author_ca.cer");
            await ExportPfxWithCaChainAsync(signedDistributorCsrBytes, keyPair.Private, p12Plain, outputPath, caPath, "distributor", "vd_tizen_dev_public2.crt");

            updateStatus("MovingP12Files".Localized());
            string p12Location = MoveTizenCertificateFiles();

            return (p12Location, p12Encrypted);
        }

        private static async Task CheckCertificateExistanceAsync(string caPath)
        {
            var requiredFiles = new[]
            {
                "vd_tizen_dev_author_ca.cer",
                "vd_tizen_dev_public2.crt"
            };

            string caLocalPath = Path.Combine(caPath, "ca_local");

            foreach (var fileName in requiredFiles)
            {
                var targetFilePath = Path.Combine(caPath, fileName);

                if (!File.Exists(targetFilePath))
                {
                    var sourceFilePath = Path.Combine(caLocalPath, fileName);

                    if (!File.Exists(sourceFilePath))
                    {
                        MessageBox.Show($"[ERROR] Source file not found: {sourceFilePath}");
                        return;
                    }

                    Directory.CreateDirectory(caPath); // Ensure target directory exists

                    await using var sourceStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    await using var targetStream = new FileStream(targetFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await sourceStream.CopyToAsync(targetStream);

                    Console.WriteLine($"[INFO] Copied missing file: {fileName} from ca_local to ca.");
                }
            }
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
                X509Name.CN,
                X509Name.O,
                X509Name.OU,
                X509Name.EmailAddress
            };

            var values = new List<string>
            {
                "Jelly2Sams",
                "Individual",
                "TizenSDK",
                "dev@example.com"
            };

            var subject = new X509Name(oids, values);

            var extensionGenerator = new X509ExtensionsGenerator();

            extensionGenerator.AddExtension(
                X509Extensions.BasicConstraints,
                true,
                new BasicConstraints(false)
            );

            extensionGenerator.AddExtension(
                X509Extensions.KeyUsage,
                true,
                new KeyUsage(KeyUsage.DigitalSignature)
            );

            extensionGenerator.AddExtension(
                X509Extensions.ExtendedKeyUsage,
                false,
                new ExtendedKeyUsage(KeyPurposeID.IdKPCodeSigning)
            );

            var extensions = extensionGenerator.Generate();

            var attribute = new AttributePkcs(
                PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
                new DerSet(extensions)
            );

            var csr = new Pkcs10CertificationRequest(
                "SHA256WITHRSA",
                subject,
                keyPair.Public,
                new DerSet(attribute),
                keyPair.Private
            );

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
            var oids = new List<DerObjectIdentifier>
            {
                X509Name.CN,
                X509Name.O,
                X509Name.OU
            };

            var values = new List<string>
            {
                "TizenSDK",
                "Individual",
                "TizenSDK"
            };

            var subject = new X509Name(oids, values);

            // Create SAN URIs
            var sanUris = new List<GeneralName>
            {
                new GeneralName(
                    GeneralName.UniformResourceIdentifier,
                    "URN:tizen:packageid="
                ),
                new GeneralName(
                    GeneralName.UniformResourceIdentifier,
                    $"URN:tizen:deviceid={duid}"
                )
            };

            var subjectAlternativeNames = new DerSequence(sanUris.ToArray());

            var extensionsGenerator = new X509ExtensionsGenerator();

            extensionsGenerator.AddExtension(
                X509Extensions.BasicConstraints,
                true,
                new BasicConstraints(false)
            );

            extensionsGenerator.AddExtension(
                X509Extensions.KeyUsage,
                true,
                new KeyUsage(KeyUsage.DigitalSignature)
            );

            extensionsGenerator.AddExtension(
                X509Extensions.ExtendedKeyUsage,
                false,
                new ExtendedKeyUsage(KeyPurposeID.IdKPCodeSigning)
            );

            extensionsGenerator.AddExtension(
                X509Extensions.SubjectAlternativeName,
                false,
                subjectAlternativeNames
            );

            var extensions = extensionsGenerator.Generate();

            var attribute = new AttributePkcs(
                PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
                new DerSet(extensions)
            );

            var csr = new Pkcs10CertificationRequest(
                "SHA256WITHRSA",
                subject,
                keyPair.Public,
                new DerSet(attribute),
                keyPair.Private
            );

            using (var writer = new StreamWriter("distributor.csr"))
                new PemWriter(writer).WriteObject(csr);

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
            { new StringContent("8.0"), "tizen_version" },
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
            { new StringContent("8.0"), "tizen_version" },
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
            { new StringContent("8.0"), "tizen_version" },
            { new StringContent("VD"), "platform" }
        };

            var csrFileBytes = await File.ReadAllBytesAsync(csrFilePath);
            content.Add(new ByteArrayContent(csrFileBytes), "csr", "distributor.csr");

            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }
        public async Task ExtractRootCertificateAsync(string jarPath)
        {
            if (string.IsNullOrEmpty(jarPath))
            {
                throw new ArgumentException("jarPath cannot be null or empty", nameof(jarPath));
            }

            if (!Directory.Exists(jarPath))
            {
                throw new DirectoryNotFoundException($"JAR directory not found: {jarPath}");
            }

            var jarFiles = Directory.GetFiles(jarPath, "*.jar");

            foreach (var jar in jarFiles)
            {
                var fileName = Path.GetFileName(jar);

                if (fileName.StartsWith("org.tizen.common.cert") && fileName.EndsWith(".jar"))
                {
                    using var fileStream = File.OpenRead(jar);
                    using var msJar = new MemoryStream();
                    await fileStream.CopyToAsync(msJar);
                    msJar.Position = 0;

                    using var jarZip = new ZipArchive(msJar, ZipArchiveMode.Read);
                    foreach (var member in jarZip.Entries)
                    {
                        string memberFileName = Path.GetFileName(member.FullName);

                        if (memberFileName == ("vd_tizen_dev_author_ca.cer") || memberFileName == "vd_tizen_dev_public2.crt")
                        {
                            var targetPath = Path.Combine("TizenProfile", "ca", memberFileName);
                            var directoryName = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(directoryName))
                            {
                                Directory.CreateDirectory(directoryName);
                            }

                            using var entryStream = member.Open();
                            using var fileStreamOut = File.Create(targetPath);
                            await entryStream.CopyToAsync(fileStreamOut);
                        }
                    }
                }
            }

            await Task.CompletedTask;
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
            if (string.IsNullOrEmpty(userHome))
            {
                throw new InvalidOperationException("Unable to get user profile directory");
            }

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
            if (!Directory.Exists(sourceFolder))
            {
                throw new DirectoryNotFoundException($"Source folder not found: {sourceFolder}");
            }

            string[] fileExtensions = { "*.xml", "*.pri", "*.p12", "*.pwd", "*.csr", "*.crt", "*.cer", "*.txt" };

            foreach (var pattern in fileExtensions)
            {
                string[] files = Directory.GetFiles(sourceFolder, pattern);
                foreach (var file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        string destFile = Path.Combine(destinationFolder, fileName);
                        File.Move(file, destFile, overwrite: true);
                    }
                }
            }
            return destinationFolder;
        }
    }
}