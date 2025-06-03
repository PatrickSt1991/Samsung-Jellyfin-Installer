using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using System.Text;

namespace Samsung_Jellyfin_Installer.Converters
{
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
