using Newtonsoft.Json;

namespace Samsung_Jellyfin_Installer.Models
{
    public class TizenResults
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string AccessToken { get; set; }
        public string UserId { get; set; }
        public string Email { get; set; }
    }
    public class TokenData
    {
        public string access_token { get; set; }
        public string token_type { get; set; }
        public string userId { get; set; }
        public string inputEmailID { get; set; }
        // Add other fields as needed
    }
    public class SamsungResponse
    {
        public string rtnCd {  get; set; }
        public string nextURL { get; set; }
    }
    public class EncryptedPasswordData
    {
        public string Email { get; set; }
        public string EncryptedPassword { get; set; }
        public string Key { get; set; }  // RSA-encrypted AES key
        public string IV { get; set; }   // Initialization Vector
        public string Salt { get; set; } // PBKDF2 salt
        public string  EncryptedOldPassword { get; set; }
    }
}