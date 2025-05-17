using HtmlAgilityPack;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Ocsp;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Ocsp;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Samsung_Jellyfin_Installer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
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
        private readonly CookieContainer _cookieContainer = new CookieContainer();
        private string _csrfToken;
        private string _recaptchaToken;
        private string _siteKey;

        public TizenCertificateService(ICaptchaSolver captchaSolver)
        {

            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true,
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            _httpClient = new HttpClient(handler);

            /*
            _httpClient = new HttpClient(new HttpClientHandler
            {
                UseCookies = true,
                AllowAutoRedirect = false
            });
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            */
            _captchaSolver = captchaSolver;
        }
        private async Task DebugRequest(HttpRequestMessage request)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Request: {request.Method} {request.RequestUri}");
            sb.AppendLine("Headers:");
            foreach (var header in request.Headers)
                sb.AppendLine($"{header.Key}: {string.Join(", ", header.Value)}");

            if (request.Content != null)
            {
                sb.AppendLine("Body:");
                sb.AppendLine(await request.Content.ReadAsStringAsync());
            }

            Debug.WriteLine(sb.ToString());
        }
        public async Task GenerateCertificateAsync(string email, string password, string deviceIds, Action<string> updateStatus)
        {
            try
            {
                updateStatus("Generating CSR with DUIDs...");
                var csrGenerator = new CsrGenerator();
                var csr = csrGenerator.GenerateCsr(email, deviceIds, out var keyPair);
                File.WriteAllText("private.key", ConvertToPem(keyPair.Private));

                updateStatus("Authenticating with Samsung...");
                var authResponse = await ExecuteWithRetry(async () =>
                    await LoginAsync(email, password),
                    maxRetries: 3,
                    updateStatus: updateStatus);

                if (!authResponse.Success)
                    throw new Exception($"Login failed: {authResponse.ErrorMessage}");

                Debug.Write(authResponse);
                return;

                updateStatus("Submitting CSR to distributor API...");
                var certificate = await ExecuteWithRetry(async () =>
                    await SubmitDistributorRequestV2Async(
                        authResponse.AccessToken,
                        authResponse.UserId,
                        csr),
                    maxRetries: 3,
                    updateStatus: updateStatus);

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
            try
            {

                // Initial request to get session cookies
                var initialResponse = await _httpClient.GetAsync($"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInGate" +
                    "?clientId=v285zxnl3h" +
                    "&redirect_uri=http://localhost:4794/signin/callback" +
                    "&state=accountcheckdogeneratedstatetext" +
                    "&tokenType=TOKEN");

                var initialHtml = await initialResponse.Content.ReadAsStringAsync();

                SamsungCrypto.InitializeFromHtml(initialHtml);
                _csrfToken = ExtractCsrfToken(initialHtml);
                _siteKey = ExtractRecaptchaSiteKey(initialHtml);

                // Submit email with CAPTCHA
                _recaptchaToken = await _captchaSolver.SolveReCaptchaEnterpriseAsync(_siteKey, "login");
                var emailResponse = await SubmitEmailAsync(email);
                var emailResult = JsonConvert.DeserializeObject<SamsungResponse>(await emailResponse.Content.ReadAsStringAsync());
                Debug.Write(emailResult?.rtnCd);
                if (emailResult?.rtnCd != "VALID")
                    throw new Exception("Email submission failed");

                // Submit password
                var passwordPageResponse = await _httpClient.GetAsync($"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInPassword");
                var passwordPageHtml = await passwordPageResponse.Content.ReadAsStringAsync();

                SamsungCrypto.InitializeFromHtml(passwordPageHtml);

                _csrfToken = ExtractCsrfToken(passwordPageHtml);
                _siteKey = ExtractRecaptchaSiteKey(passwordPageHtml);

                _recaptchaToken = await _captchaSolver.SolveReCaptchaEnterpriseAsync(_siteKey, "login");
                
                var passwordResponse = await SubmitPasswordAsync(email, password);

                var passwordResult = JsonConvert.DeserializeObject<SamsungResponse>(await passwordResponse.Content.ReadAsStringAsync());

                if (passwordResult?.rtnCd != "SUCCESS")
                    throw new Exception("Password submission failed");

                // Handle final completion
                var completionResponse = await _httpClient.GetAsync($"{AccountUrl}{passwordResult.nextURL}");
                var completionHtml = await completionResponse.Content.ReadAsStringAsync();

                var tokenData = ExtractTokenData(completionHtml) ?? throw new Exception("Token extraction failed");

                return new TizenResults
                {
                    Success = true,
                    AccessToken = tokenData.access_token,
                    UserId = tokenData.userId,
                    Email = tokenData.inputEmailID
                };
            }
            catch (Exception ex)
            {
                return new TizenResults
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private async Task<HttpResponseMessage> SubmitEmailAsync(string email)
        {
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInIdentificationProc?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");

            request.Headers.Add("X-CSRF-TOKEN", _csrfToken);
            request.Headers.Add("x-recaptcha-token", _recaptchaToken);

            request.Content = new StringContent(
                JsonConvert.SerializeObject(new
                {
                    loginId = email,
                    rememberId = false,
                    pageSource = "signInIdentification"
                }),
                Encoding.UTF8,
                "application/json");

            return await _httpClient.SendAsync(request);
        }

        private async Task<HttpResponseMessage> SubmitPasswordAsync(string email, string password)
        {
            // Validate required tokens
            if (string.IsNullOrEmpty(_csrfToken) || string.IsNullOrEmpty(_recaptchaToken))
                throw new InvalidOperationException("Missing required tokens");

            // Encrypt credentials using proper Samsung flow
            var encryptedData = SamsungCrypto.EncryptCredentials(email, password);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInProc?v={timestamp}");

            // Set headers
            request.Headers.Clear();
            request.Headers.Add("X-CSRF-TOKEN", _csrfToken);
            request.Headers.Add("x-recaptcha-token", _recaptchaToken);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Referer", $"{AccountUrl}/accounts/be1dce529476c1a6d407c4c7578c31bd/signInPassword");
            request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            // Create request body matching Samsung's format
            var requestBody = new Dictionary<string, object>
            {
                ["remIdChkYN"] = false,
                ["captchaAnswer"] = _recaptchaToken,
                ["iptLgnID"] = email,
                ["iptLgnPD"] = encryptedData.EncryptedPassword,
                ["svcIptLgnKY"] = encryptedData.Key,
                ["svcIptLgnIV"] = encryptedData.IV,
                ["lgnEncTp"] = "1"
            };

            var jsonContent = System.Text.Json.JsonSerializer.Serialize(requestBody, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = null,
                WriteIndented = false
            });

            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Debug and send request
            Debug.WriteLine($"Submitting password with: {jsonContent}");
            var response = await _httpClient.SendAsync(request);

            var responseContent = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"Response: {responseContent}");

            return response;
        }
        private TokenData ExtractTokenData(string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var codeInput = doc.DocumentNode.SelectSingleNode("//input[@name='code']");
            if (codeInput == null) return null;

            var encodedJson = codeInput.GetAttributeValue("value", "");
            var decodedJson = WebUtility.HtmlDecode(encodedJson);
            return JsonConvert.DeserializeObject<TokenData>(decodedJson);
        }

        private async Task<string> SubmitDistributorRequestV2Async(string accessToken, string userId, string csr)
        {
            var content = new MultipartFormDataContent("----WebKitFormBoundary7MA4YWxkTrZu0gW")
            {
                { new StringContent(accessToken), "access_token" },
                { new StringContent(userId), "user_id" },
                { new StringContent("Public"), "privilege_level" },
                { new StringContent("Individual"), "developer_type" },
                { new StringContent("VD"), "platform" },
                { new StringContent(csr), "csr" }
            };

            var response = await _httpClient.PostAsync($"{ApiBaseUrl}/apis/v2/distributors", content);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Certificate request failed ({response.StatusCode}): {errorContent}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        private string ExtractCsrfToken(string html)
        {
            var match = Regex.Match(html, @"'token':\s*'([^']*)'");
            return match.Success ? match.Groups[1].Value : null;
        }

        private string ExtractRecaptchaSiteKey(string html)
        {
            var match = Regex.Match(html, @"recaptchaSiteKey\s*=\s*""([^""]+)");
            return match.Success ? match.Groups[1].Value : "6Leu19YoAAAAABs9aBxlHOs_qGOGYt3qTEFI3vxJ";
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
        public string GenerateCsr(string email, string deviceIds, out AsymmetricCipherKeyPair keyPair)
        {
            var randomGenerator = new CryptoApiRandomGenerator();
            var random = new SecureRandom(randomGenerator);

            var keyGenerationParameters = new KeyGenerationParameters(random, 2048);
            var keyPairGenerator = new RsaKeyPairGenerator();
            keyPairGenerator.Init(keyGenerationParameters);
            keyPair = keyPairGenerator.GenerateKeyPair();

            var subject = new X509Name($"CN=TizenSDK, O=Individual, emailAddress={email}");
            var attributes = CreateAttributesWithSanExtensions(email, deviceIds);

            var csr = new Pkcs10CertificationRequest(
                "SHA256WithRSA",
                subject,
                keyPair.Public,
                attributes,
                keyPair.Private);

            return ConvertToPem(csr.GetEncoded());
        }

        private DerSet CreateAttributesWithSanExtensions(string email, string deviceId)
        {
            var sanName = new GeneralName(
                GeneralName.UniformResourceIdentifier,
                $"URN:tizen:deviceid={deviceId}");

            var sanExtension = new X509Extension(
                false,
                new DerOctetString(new DerSequence(sanName)));

            var extensions = new Dictionary<DerObjectIdentifier, X509Extension>
            {
                { X509Extensions.SubjectAlternativeName, sanExtension }
            };

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
                builder.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
            }

            builder.AppendLine("-----END CERTIFICATE REQUEST-----");
            return builder.ToString();
        }
    }
}