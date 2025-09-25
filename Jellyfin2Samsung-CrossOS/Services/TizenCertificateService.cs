using Jellyfin2SamsungCrossOS.Extensions;
using Jellyfin2SamsungCrossOS.Helpers;
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
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Services
{
    public class TizenCertificateService : ITizenCertificateService
    {
        private readonly HttpClient _httpClient;
        private readonly IDialogService _dialogService;

        public TizenCertificateService(HttpClient httpClient, IDialogService dialogService)
        {
            _httpClient = httpClient;
            _dialogService = dialogService;
        }

        public async Task<(string p12Location, string p12Password)> GenerateProfileAsync(
            string duid,
            string accessToken,
            string userId,
            string userEmail,
            string outputPath,
            string jarPath,
            ProgressCallback? progress = null)
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("Output path cannot be empty", nameof(outputPath));

            if (string.IsNullOrEmpty(jarPath) || !Directory.Exists(jarPath))
                throw new ArgumentException($"Invalid jarPath: {jarPath}", nameof(jarPath));

            var cipherUtil = new CipherUtil();
            await cipherUtil.ExtractPasswordAsync(jarPath);

            Directory.CreateDirectory(outputPath);

            progress?.Invoke("GenPassword".Localized());
            string p12Plain = cipherUtil.GenerateRandomPassword();
            string p12Encrypted = cipherUtil.GetEncryptedString(p12Plain);
            await File.WriteAllTextAsync(Path.Combine(outputPath, "password.txt"), p12Plain);

            progress?.Invoke("GenKeyPair".Localized());
            var keyPair = GenerateKeyPair();

            progress?.Invoke("CreateAuthorCsr".Localized());
            var authorCsrData = GenerateAuthorCsr(keyPair);
            await File.WriteAllBytesAsync(Path.Combine(outputPath, "author.csr"), authorCsrData);

            progress?.Invoke("CreateDistributorCSR".Localized());
            var distributorCsrData = GenerateDistributorCsr(keyPair, duid, userEmail);
            await File.WriteAllBytesAsync(Path.Combine(outputPath, "distributor.csr"), distributorCsrData);

            progress?.Invoke("PostAuthorCSR".Localized());
            var signedAuthorCsrBytes = await PostAuthorCsrAsync(authorCsrData, accessToken, userId);
            await File.WriteAllBytesAsync(Path.Combine(outputPath, "signed_author.cer"), signedAuthorCsrBytes);

            progress?.Invoke("PostDistributorCSR".Localized());
            var (profileXmlBytes, signedDistributorCsrBytes) = await PostDistributorCsrAsync(accessToken, userId, distributorCsrData, duid);
            if (profileXmlBytes != null)
                await File.WriteAllBytesAsync(Path.Combine(outputPath, "device-profile.xml"), profileXmlBytes);
            await File.WriteAllBytesAsync(Path.Combine(outputPath, "signed_distributor.cer"), signedDistributorCsrBytes);

            progress?.Invoke("ExtractRootCertificate".Localized());
            await ExtractRootCertificateAsync(jarPath);

            await CheckCertificateExistenceAsync(Path.Combine(outputPath, "ca"));

            progress?.Invoke("ExportPfxCertificates".Localized());
            await ExportPfxWithCaChainAsync(signedAuthorCsrBytes, keyPair.Private, p12Plain, outputPath, Path.Combine(outputPath, "ca"), "author", "vd_tizen_dev_author_ca.cer");
            await ExportPfxWithCaChainAsync(signedDistributorCsrBytes, keyPair.Private, p12Plain, outputPath, Path.Combine(outputPath, "ca"), "distributor", "vd_tizen_dev_public2.crt");

            progress?.Invoke("MovingP12Files".Localized());
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
            var subject = new X509Name("C=, ST=, L=, O=, OU=, CN=Jelly2Sams");
            var csr = new Pkcs10CertificationRequest("SHA512WithRSA", subject, keyPair.Public, null, keyPair.Private);

            using var ms = new MemoryStream();
            using var sw = new StreamWriter(ms);
            new PemWriter(sw).WriteObject(csr);
            sw.Flush();
            return ms.ToArray();
        }

        private static byte[] GenerateDistributorCsr(AsymmetricCipherKeyPair keyPair, string duid, string userEmail)
        {
            var subject = new X509Name($"CN=TizenSDK, OU=, O=, L=, ST=, C=, emailAddress={userEmail}");
            var generalNames = new GeneralNames(new[]
            {
                new GeneralName(GeneralName.UniformResourceIdentifier, "URN:tizen:packageid="),
                new GeneralName(GeneralName.UniformResourceIdentifier, $"URN:tizen:deviceid={duid}")
            });

            var extensions = new X509Extensions(new Dictionary<DerObjectIdentifier, Org.BouncyCastle.Asn1.X509.X509Extension>
            {
                { X509Extensions.SubjectAlternativeName, new Org.BouncyCastle.Asn1.X509.X509Extension(false, new DerOctetString(generalNames)) }
            });

            var attribute = new AttributePkcs(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest, new DerSet(extensions));
            var csr = new Pkcs10CertificationRequest("SHA512WithRSA", subject, keyPair.Public, new DerSet(attribute), keyPair.Private);

            using var ms = new MemoryStream();
            using var sw = new StreamWriter(ms);
            new PemWriter(sw).WriteObject(csr);
            sw.Flush();
            return ms.ToArray();
        }

        private async Task CheckCertificateExistenceAsync(string caPath)
        {
            string[] requiredFiles = { "vd_tizen_dev_author_ca.cer", "vd_tizen_dev_public2.crt" };
            string caLocalPath = Path.Combine(caPath, "ca_local");

            foreach (var file in requiredFiles)
            {
                string target = Path.Combine(caPath, file);
                if (!File.Exists(target))
                {
                    string source = Path.Combine(caLocalPath, file);
                    if (!File.Exists(source))
                        await _dialogService.ShowErrorAsync($"Missing CA file: {file}");
                    else
                        File.Copy(source, target, true);
                }
            }
        }

        // Simplified Http Post for Author CSR
        private async Task<byte[]> PostAuthorCsrAsync(byte[] csrData, string accessToken, string userId)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, AppSettings.Default.AuthorEndpoint_V3);
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

        // Simplified Http Post for Distributor CSR
        private async Task<(byte[] profileXml, byte[] distributorCert)> PostDistributorCsrAsync(string accessToken, string userId, byte[] csrBytes, string duid)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, AppSettings.Default.DistributorsEndpoint_V3);
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
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return (bytes, bytes); // same for simplicity
        }

        public async Task ExtractRootCertificateAsync(string jarPath)
        {
            if (!Directory.Exists(jarPath)) return;

            foreach (var jar in Directory.GetFiles(jarPath, "*.jar"))
            {
                if (!Path.GetFileName(jar).StartsWith("org.tizen.common.cert")) continue;
                using var fs = File.OpenRead(jar);
                using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
                foreach (var entry in zip.Entries)
                {
                    string fileName = Path.GetFileName(entry.FullName);
                    if (fileName == "vd_tizen_dev_author_ca.cer" || fileName == "vd_tizen_dev_public2.crt")
                    {
                        string target = Path.Combine("TizenProfile", "ca", fileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        using var outStream = File.Create(target);
                        using var entryStream = entry.Open();
                        await entryStream.CopyToAsync(outStream);
                    }
                }
            }
        }

        private static async Task ExportPfxWithCaChainAsync(byte[] signedCertBytes, AsymmetricKeyParameter privateKey, string password, string outputPath, string caPath, string filename, string caFile)
        {
            string caCertFile = Path.Combine(caPath, caFile);
            var parser = new X509CertificateParser();
            var signedCert = parser.ReadCertificate(signedCertBytes);
            var caCert = parser.ReadCertificate(await File.ReadAllBytesAsync(caCertFile));

            var signedCertDotNet = new X509Certificate2(signedCert.GetEncoded());
            var caCertDotNet = new X509Certificate2(caCert.GetEncoded());

            var rsaPrivateKey = DotNetUtilities.ToRSA((RsaPrivateCrtKeyParameters)privateKey);
            using var certWithPrivateKey = signedCertDotNet.CopyWithPrivateKey(rsaPrivateKey);

            var collection = new X509Certificate2Collection { caCertDotNet, certWithPrivateKey };
            await File.WriteAllBytesAsync(Path.Combine(outputPath, $"{filename}.p12"), collection.Export(X509ContentType.Pkcs12, password));

            signedCertDotNet.Dispose();
            caCertDotNet.Dispose();
        }

        public static string MoveTizenCertificateFiles()
        {
            string dest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SamsungCertificate", "Jelly2Sams");
            Directory.CreateDirectory(dest);
            string src = Path.Combine(Environment.CurrentDirectory, "TizenProfile");
            foreach (var file in Directory.GetFiles(src, "*.*"))
                File.Move(file, Path.Combine(dest, Path.GetFileName(file)!), true);
            return dest;
        }
    }
}
