using Newtonsoft.Json;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Samsung_Jellyfin_Installer.Converters;
using Samsung_Jellyfin_Installer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Samsung_Jellyfin_Installer.Services
{
    public class TizenCertificateService : ITizenCertificateService
    {
        private const string AccountUrl = "https://account.samsung.com";
        private const string ApiBaseUrl = "https://dev.tizen.samsung.com";
        private readonly HttpClient _httpClient;
        private readonly ICaptchaSolver _captchaSolver;
        private string _csrfToken;
        private string _recaptchaToken;

        public TizenCertificateService(ICaptchaSolver captchaSolver)
        {
            _httpClient = new HttpClient(new HttpClientHandler
            {
                UseCookies = true,
                AllowAutoRedirect = false
            });
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            _captchaSolver = captchaSolver;
        }

        public async Task GenerateCertificateAsync(string email, string password, string[] deviceIds, Action<string> updateStatus)
        {
            try
            {
                // Generate CSR with key pair
                updateStatus("Generating CSR with DUIDs...");
                var csrGenerator = new CsrGenerator();
                var csr = csrGenerator.GenerateCsr(email, deviceIds, out var keyPair);

                // Save private key securely
                File.WriteAllText("private.key", ConvertToPem(keyPair.Private));

                // Login to Samsung
                updateStatus("Authenticating with Samsung...");
                var authResponse = await ExecuteWithRetry(async () =>
                    await LoginAsync(email, password),
                    maxRetries: 3,
                    updateStatus: updateStatus);

                // Submit CSR
                updateStatus("Submitting CSR to distributor API...");
                var certificate = await ExecuteWithRetry(async () =>
                    await SubmitDistributorRequestV2Async(
                        authResponse.AccessToken,
                        authResponse.UserId,
                        csr),
                    maxRetries: 3,
                    updateStatus: updateStatus);

                // Save certificate
                updateStatus("Saving certificate...");
                File.WriteAllText("distributor.p12", certificate);
                updateStatus("Certificate generated successfully!");
            }
            catch (Exception ex)
            {
                updateStatus($"Error: {ex.Message}");
                throw;
            }
        }

        private async Task<TizenResults> LoginAsync(string email, string password)
        {
            // Get initial session cookies
            await _httpClient.GetAsync($"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInGate" +
                "?clientId=v285zxnl3h" +
                "&redirect_uri=http://localhost:4794/signin/callback" +
                "&state=accountcheckdogeneratedstatetext" +
                "&tokenType=TOKEN");

            // Submit email
            await _httpClient.PostAsync(
                $"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInIdentificationProc",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("loginId", email),
                    new KeyValuePair<string, string>("rememberId", "false")
                }));

            // Get password page to extract tokens
            var passwordPage = await _httpClient.GetStringAsync(
                $"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInPassword");

            _csrfToken = ExtractCsrfToken(passwordPage);
            var (key, iv) = ExtractEncryptionParams(passwordPage);

            // Solve CAPTCHA
            _recaptchaToken = await _captchaSolver.SolveReCaptchaV2Async(
                "6Le3CGAUAAAAADgwHP5vnwfsLbIOoxnh07DcaMcq",
                $"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInPassword");

            // Submit encrypted password with CAPTCHA
            var encryptedPassword = SamsungCrypto.EncryptPassword(password);
            var authResponse = await _httpClient.PostAsync(
                $"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInProc",
                new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("remIdChkYN", "false"),
                    new KeyValuePair<string, string>("captchaAnswer", _recaptchaToken),
                    new KeyValuePair<string, string>("iptLgnID", email),
                    new KeyValuePair<string, string>("iptLgnPD", encryptedPassword),
                    new KeyValuePair<string, string>("svcIptLgnKY", key),
                    new KeyValuePair<string, string>("svcIptLgnIV", iv),
                    new KeyValuePair<string, string>("_csrf", _csrfToken)
                }));

            var responseContent = await authResponse.Content.ReadAsStringAsync();
            if (!authResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Login failed: {responseContent}");
            }

            return JsonConvert.DeserializeObject<TizenResults>(responseContent);
        }

        private async Task<string> SubmitDistributorRequestV2Async(string accessToken, string userId, string csr)
        {
            var content = new MultipartFormDataContent("*****")
            {
                { new StringContent(accessToken), "access_token" },
                { new StringContent(userId), "user_id" },
                { new StringContent("Public"), "privilege_level" },
                { new StringContent("Individual"), "developer_type" },
                { new StringContent("VD"), "platform" },
                { new ByteArrayContent(Encoding.UTF8.GetBytes(csr)), "csr", "distributor.csr" }
            };

            var response = await _httpClient.PostAsync(
                $"{ApiBaseUrl}/apis/v2/distributors",
                content);

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Certificate request failed: {response.StatusCode}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        private string ExtractCsrfToken(string html)
        {
            var match = Regex.Match(html, @"'token':\s*'([^']*)'");
            return match.Success ? match.Groups[1].Value :
                throw new Exception("CSRF token not found in response");
        }

        private (string Key, string Iv) ExtractEncryptionParams(string html)
        {
            var keyMatch = Regex.Match(html, @"svcIptLgnKY.*?'([^']*)'");
            var ivMatch = Regex.Match(html, @"svcIptLgnIV.*?'([^']*)'");

            if (!keyMatch.Success || !ivMatch.Success)
                throw new Exception("Failed to extract encryption parameters");

            return (keyMatch.Groups[1].Value, ivMatch.Groups[1].Value);
        }

        private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> action, int maxRetries, Action<string> updateStatus)
        {
            var retryCount = 0;
            while (true)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (retryCount < maxRetries)
                {
                    retryCount++;
                    updateStatus($"Attempt {retryCount} failed. Retrying... Error: {ex.Message}");
                    await Task.Delay(2000 * retryCount);
                }
            }
        }

        private string ConvertToPem(AsymmetricKeyParameter key)
        {
            using var writer = new StringWriter();
            var pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(writer);
            pemWriter.WriteObject(key);
            return writer.ToString();
        }
    }

    public class CsrGenerator
    {
        public string GenerateCsr(string email, string[] deviceIds, out AsymmetricCipherKeyPair keyPair)
        {
            // 1. Generate RSA Key Pair
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);

            var keyGenerationParameters = new KeyGenerationParameters(random, 2048);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            keyPair = keyPairGenerator.GenerateKeyPair();

            // 2. Prepare Subject
            var subject = new X509Name($"CN=TizenSDK, O=Individual, emailAddress={email}");

            // 3. Create Attributes (for SAN extensions)
            var attributes = CreateAttributesWithSanExtensions(email, deviceIds);

            // 4. Create and sign the CSR
            var signatureAlgorithm = "SHA-256WithRSAEncryption";
            var signingKey = (AsymmetricKeyParameter)keyPair.Private;
            var publicKey = (AsymmetricKeyParameter)keyPair.Public;

            var csr = new Pkcs10CertificationRequest(
                signatureAlgorithm,
                subject,
                publicKey,
                attributes,
                signingKey
            );

            // 5. Convert to PEM format
            return ConvertToPem(csr.GetEncoded());
        }

        private DerSet CreateAttributesWithSanExtensions(string email, string[] deviceIds)
        {
            // Create SAN extensions
            var sanNames = new List<Asn1Encodable>();
            foreach (var duid in deviceIds)
            {
                sanNames.Add(new GeneralName(
                    GeneralName.UniformResourceIdentifier,
                    $"URN:tizen:deviceid={duid}"));
            }

            var sanExtension = new X509Extension(
                false, // not critical
                new DerOctetString(new DerSequence(sanNames.ToArray())));

            var extensions = new Dictionary<DerObjectIdentifier, X509Extension>
        {
            { X509Extensions.SubjectAlternativeName, sanExtension }
        };

            // Wrap in Attribute
            var attribute = new AttributePkcs(
                PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
                new DerSet(new X509Extensions(extensions)));

            return new DerSet(attribute);
        }

        private string ConvertToPem(byte[] derEncoded)
        {
            var builder = new StringBuilder();
            builder.AppendLine("-----BEGIN CERTIFICATE REQUEST-----");

            var base64 = Convert.ToBase64String(derEncoded);
            for (var i = 0; i < base64.Length; i += 64)
            {
                var length = Math.Min(64, base64.Length - i);
                builder.AppendLine(base64.Substring(i, length));
            }

            builder.AppendLine("-----END CERTIFICATE REQUEST-----");
            return builder.ToString();
        }
    }
}