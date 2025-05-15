using System.Security.Cryptography;
using System.Text;

namespace Samsung_Jellyfin_Installer.Converters
{
    public static class SamsungCrypto
    {
        private static readonly string PublicKey =
            "MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDGGKzw1G6L2XDSvj49woICjl+uO1wu7YXCxb2DBRBC4a4UHjzyAL2oPWW9Bw1HJpynsfN7ivDhMUcqggvjJCVeHlmh+MNPygc7jV2Ul4IeC087cPEzPRCYkhRusMM8XciRtjIYH44UkE7nWgdgsZ1Fdlj6dtjlLMZsiG4uH6tsGQIDAQAB";

        public static string EncryptPassword(string password)
        {
            using var rsa = new RSACryptoServiceProvider(1024);
            try
            {
                rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
                return Convert.ToBase64String(
                    rsa.Encrypt(Encoding.UTF8.GetBytes(password), RSAEncryptionPadding.Pkcs1));
            }
            catch (CryptographicException ex)
            {
                throw new Exception("Password encryption failed", ex);
            }
            finally
            {
                rsa.PersistKeyInCsp = false;
            }
        }
    }
}