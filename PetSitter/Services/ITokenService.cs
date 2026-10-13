using PetSitter.DTOs;
using PetSitter.Models;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public interface ITokenService
    {
        /// <summary>
        /// Tạo Access Token có thời hạn ngắn (15 phút).
        /// GHI CHÚ BẢO MẬT: Sau khi đổi mật khẩu hoặc đổi Role, access token cũ vẫn có thể sống tối đa 15 phút
        /// vì kiến trúc JWT là stateless, không truy vấn CSDL ở mỗi request.
        /// </summary>
        string GenerateAccessToken(User user);

        /// <summary>
        /// Tương thích ngược với interface cũ.
        /// </summary>
        string GenerateToken(User user);

        /// <summary>
        /// Tạo Refresh Token ngẫu nhiên 256-bit mới và lưu hash vào CSDL.
        /// </summary>
        Task<string> CreateRefreshTokenAsync(string userId, string? ipAddress);

        /// <summary>
        /// Xoay vòng Refresh Token nguyên tử, phát hiện Replay Attack và xử lý ân hạn ngắn cho race condition.
        /// </summary>
        Task<RotateResult> RotateRefreshTokenAsync(string rawToken, string? ipAddress);

        /// <summary>
        /// Thu hồi một refresh token cụ thể (khi Logout).
        /// </summary>
        Task RevokeRefreshTokenAsync(string rawToken);

        /// <summary>
        /// Thu hồi toàn bộ refresh token của một user (khi Đổi mật khẩu, Đổi Role, hoặc phát hiện Reuse Attack).
        /// </summary>
        Task RevokeAllUserTokensAsync(string userId);
    }
}
