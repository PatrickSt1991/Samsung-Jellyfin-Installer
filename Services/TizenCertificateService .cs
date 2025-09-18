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
using System.Net.Mail;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Windows;

namespace Samsung_Jellyfin_Installer.Services
{
    public class TizenCertificateService(HttpClient httpClient) : ITizenCertificateService
    {
        private readonly HttpClient _httpClient = httpClient;

        public async Task<(string p12Location, string p12Password)> GenerateProfileAsync(string duid, string accessToken, string userId, string userEmail, string outputPath, Action<string> updateStatus, string jarPath)
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
            var distributorCsrData = GenerateDistributorCsr(keyPair, duid, userEmail);
            var distributorCsrPath = Path.Combine(outputPath, "distributor.csr");
            await File.WriteAllBytesAsync(distributorCsrPath, distributorCsrData);

            updateStatus("PostAuthorCSR".Localized());
            var signedAuthorCsrBytes = await PostAuthorCsrAsync(authorCsrData, accessToken, userId);
            var signedAuthorCsrPath = Path.Combine(outputPath, "signed_author.cer");
            await File.WriteAllBytesAsync(signedAuthorCsrPath, signedAuthorCsrBytes);

            updateStatus("PostDistributorCSR".Localized());
            var (profileXmlBytes, signedDistributorCsrBytes) = await PostDistributorCsrAsync(accessToken, userId, distributorCsrData, duid);

            if (profileXmlBytes != null)
            {
                var profileXmlPath = Path.Combine(outputPath, "device-profile.xml");
                await File.WriteAllBytesAsync(profileXmlPath, profileXmlBytes);
            }

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

                    Directory.CreateDirectory(caPath);
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
            var subject = new X509Name("C=, ST=, L=, O=, OU=, CN=Jelly2Sams");

            var csr = new Pkcs10CertificationRequest(
                "SHA512WithRSA",
                subject,
                keyPair.Public,
                null, // No attributes/extensions needed for Author CSR
                keyPair.Private
            );

            using (var ms = new MemoryStream())
            using (var sw = new StreamWriter(ms))
            {
                var pemWriter = new PemWriter(sw);
                pemWriter.WriteObject(csr);
                sw.Flush();
                return ms.ToArray();
            }
        }

        private static byte[] GenerateDistributorCsr(AsymmetricCipherKeyPair keyPair, string duid, string userEmail)
        {
            var subject = new X509Name($"E={userEmail}, CN=TizenSDK, OU=, O=, L=, ST=, C=");

            // Create the SubjectAlternativeName extension required by the distributor endpoint.
            var generalNameList = new List<GeneralName>
            {
                new GeneralName(GeneralName.UniformResourceIdentifier, "URN:tizen:packageid="),
                new GeneralName(GeneralName.UniformResourceIdentifier, $"URN:tizen:deviceid={duid}"),
                new GeneralName(GeneralName.Rfc822Name, userEmail)
            };
            var generalNames = new GeneralNames(generalNameList.ToArray());

            // Explicitly use the Bouncy Castle X509Extension class to resolve ambiguity
            var extensions = new X509Extensions(
                new Dictionary<DerObjectIdentifier, Org.BouncyCastle.Asn1.X509.X509Extension>
                {
                    { X509Extensions.SubjectAlternativeName, new Org.BouncyCastle.Asn1.X509.X509Extension(false, new DerOctetString(generalNames)) }
                }
            );

            // Wrap extensions in a pkcs-9-at-extensionRequest attribute.
            var attribute = new AttributePkcs(
                PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
                new DerSet(extensions)
            );

            var csr = new Pkcs10CertificationRequest(
                "SHA512WithRSA",
                subject,
                keyPair.Public,
                new DerSet(attribute),
                keyPair.Private
            );

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
            var request = new HttpRequestMessage(HttpMethod.Post, Settings.Default.AuthorEndpoint_V3);

            var content = new MultipartFormDataContent
            {
                { new StringContent(accessToken), "access_token" },
                { new StringContent(userId), "user_id" },
                { new StringContent("VD"), "platform"},
                { new ByteArrayContent(csrData), "csr", "author.csr" }
            };

            request.Content = content;
            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                await HandleErrorResponse(response);
                response.EnsureSuccessStatusCode();
            }

            return await response.Content.ReadAsByteArrayAsync();
        }

        private async Task<(byte[] profileXml, byte[] distributorCert)> PostDistributorCsrAsync(string accessToken, string userId, byte[] csrBytes, string duid)
        {
            var v1Request = new HttpRequestMessage(HttpMethod.Post, Settings.Default.DistributorsEndpoint_V3);
            var v1Content = new MultipartFormDataContent
            {
                { new StringContent(accessToken), "access_token" },
                { new StringContent(userId), "user_id" },
                { new StringContent("Public"), "privilege_level" },
                { new StringContent("Individual"), "developer_type" },
                { new StringContent("VD"), "platform" },
                { new ByteArrayContent(csrBytes), "csr", "distributor.csr" }
            };
            v1Request.Content = v1Content;
            var v1Response = await _httpClient.SendAsync(v1Request);

            if (!v1Response.IsSuccessStatusCode)
            {
                await HandleErrorResponse(v1Response);
                v1Response.EnsureSuccessStatusCode();
            }

            var profileXml = await v1Response.Content.ReadAsByteArrayAsync();

            var v3Request = new HttpRequestMessage(HttpMethod.Post, Settings.Default.DistributorsEndpoint_V3);
            var v3Content = new MultipartFormDataContent
            {
                { new StringContent(accessToken), "access_token" },
                { new StringContent(userId), "user_id" },
                { new StringContent("Public"), "privilege_level" },
                { new StringContent("Individual"), "developer_type" },
                { new StringContent("VD"), "platform" },
                { new ByteArrayContent(csrBytes), "csr", "distributor.csr" }
            };
            v3Request.Content = v3Content;
            var v3Response = await _httpClient.SendAsync(v3Request);

            if (!v3Response.IsSuccessStatusCode)
            {
                await HandleErrorResponse(v3Response);
                v3Response.EnsureSuccessStatusCode();
            }

            var distributorCert = await v3Response.Content.ReadAsByteArrayAsync();

            return (profileXml, distributorCert);
        }

        private async Task HandleErrorResponse(HttpResponseMessage response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                throw new Exception("You've made too many requests in a given amount of time.\nPlease wait and try your request again later.");
            }

            try
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                if (!string.IsNullOrEmpty(errorContent))
                {
                    var errorJson = JsonSerializer.Deserialize<JsonElement>(errorContent);
                    if (errorJson.TryGetProperty("error", out var errorObj))
                    {
                        var name = errorObj.TryGetProperty("name", out var nameEl) ? nameEl.ToString() : "";
                        var status = errorObj.TryGetProperty("status", out var statusEl) ? statusEl.ToString() : "";
                        var code = errorObj.TryGetProperty("code", out var codeEl) ? codeEl.ToString() : "";
                        var description = errorObj.TryGetProperty("description", out var descEl) ? descEl.ToString() : "";

                        throw new Exception($"Samsung API Error - Name: {name}, Status: {status}, Code: {code}, Description: {description}");
                    }
                }
            }
            catch (JsonException)
            {
            }

            throw new Exception($"Server response code: {response.StatusCode}");
        }

        public async Task ExtractRootCertificateAsync(string jarPath)
        {
            if (string.IsNullOrEmpty(jarPath))
                throw new ArgumentException("jarPath cannot be null or empty", nameof(jarPath));

            if (!Directory.Exists(jarPath))
                throw new DirectoryNotFoundException($"JAR directory not found: {jarPath}");

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
                        if (memberFileName == "vd_tizen_dev_author_ca.cer" || memberFileName == "vd_tizen_dev_public2.crt")
                        {
                            var targetPath = Path.Combine("TizenProfile", "ca", memberFileName);
                            var directoryName = Path.GetDirectoryName(targetPath);
                            if (!string.IsNullOrEmpty(directoryName))
                                Directory.CreateDirectory(directoryName);

                            using var entryStream = member.Open();
                            using var fileStreamOut = File.Create(targetPath);
                            await entryStream.CopyToAsync(fileStreamOut);
                        }
                    }
                }
            }
        }

        private static async Task ExportPfxWithCaChainAsync(byte[] signedCertBytes, AsymmetricKeyParameter privateKey, string password, string outputPath, string caPath, string filename, string caFile)
        {
            string caCertFile = Path.Combine(caPath, caFile);

            if (!File.Exists(caCertFile))
            {
                throw new FileNotFoundException($"CA certificate file not found: {caCertFile}");
            }

            var caCertBytes = await File.ReadAllBytesAsync(caCertFile);

            var parser = new X509CertificateParser();
            var certificates = new List<X509Certificate2>();

            // Parse the signed certificate
            var signedCert = parser.ReadCertificate(signedCertBytes);
            if (signedCert == null)
            {
                throw new Exception("Failed to parse signed certificate");
            }
            var signedCertDotNet = new X509Certificate2(signedCert.GetEncoded());

            // Parse the CA certificate
            var caCert = parser.ReadCertificate(caCertBytes);
            if (caCert == null)
            {
                throw new Exception("Failed to parse CA certificate");
            }
            var caCertDotNet = new X509Certificate2(caCert.GetEncoded());

            // Export to PFX
            var rsaPrivateKey = DotNetUtilities.ToRSA((RsaPrivateCrtKeyParameters)privateKey);
            using var certWithPrivateKey = signedCertDotNet.CopyWithPrivateKey(rsaPrivateKey);

            // Create the certificate collection and add certificates in the CORRECT order
            var certCollection = new X509Certificate2Collection();
            certCollection.Add(caCertDotNet);      // Add the Intermediate CA first
            certCollection.Add(certWithPrivateKey); // Add the signed certificate with its private key next

            var pfxBytes = certCollection.Export(X509ContentType.Pkcs12, password);
            var pfxPath = Path.Combine(outputPath, $"{filename}.p12");
            await File.WriteAllBytesAsync(pfxPath, pfxBytes);

            // Clean up
            signedCertDotNet.Dispose();
            caCertDotNet.Dispose();
        }
        public static string MoveTizenCertificateFiles()
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(userHome))
                throw new InvalidOperationException("Unable to get user profile directory");

            string destinationFolder = Path.Combine(userHome, "SamsungCertificate", "Jelly2Sams");
            Directory.CreateDirectory(destinationFolder);

            string sourceFolder = Path.Combine(Environment.CurrentDirectory, "TizenProfile");
            if (!Directory.Exists(sourceFolder))
                throw new DirectoryNotFoundException($"Source folder not found: {sourceFolder}");

            string[] fileExtensions = { "*.xml", "*.pri", "*.p12", "*.pwd", "*.csr", "*.crt", "*.cer", "*.txt" };
            foreach (var pattern in fileExtensions)
            {
                foreach (var file in Directory.GetFiles(sourceFolder, pattern))
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