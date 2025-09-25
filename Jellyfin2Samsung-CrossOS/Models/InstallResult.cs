namespace Jellyfin2SamsungCrossOS.Models
{
    public class InstallResult
    {
        public bool Success { get; init; }
        public string ErrorMessage { get; init; }

        public static InstallResult SuccessResult() => new() { Success = true };
        public static InstallResult FailureResult(string error) => new()
        {
            Success = false,
            ErrorMessage = error
        };
    }
}
