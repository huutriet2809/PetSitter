namespace PetSitter.DTOs
{
    public class RefreshTokenResponseDto
    {
        public string AccessToken { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    public enum RotateStatus
    {
        Success,
        GracePeriod,
        Expired,
        RevokedReuse,
        Invalid
    }

    public class RotateResult
    {
        public RotateStatus Status { get; set; }
        public string? AccessToken { get; set; }
        public string? NewRefreshToken { get; set; }
        public string? Role { get; set; }
        public string? Message { get; set; }

        public static RotateResult Ok(string accessToken, string newRefreshToken, string role) =>
            new RotateResult { Status = RotateStatus.Success, AccessToken = accessToken, NewRefreshToken = newRefreshToken, Role = role };

        public static RotateResult Grace(string accessToken, string role) =>
            new RotateResult { Status = RotateStatus.GracePeriod, AccessToken = accessToken, Role = role };

        public static RotateResult Fail(RotateStatus status, string message) =>
            new RotateResult { Status = status, Message = message };
    }
}
