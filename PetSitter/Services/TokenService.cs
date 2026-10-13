using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Models;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public class TokenService : ITokenService
    {
        private readonly IConfiguration _config;
        private readonly PetSitterContext _context;

        public TokenService(IConfiguration config, PetSitterContext context)
        {
            _config = config;
            _context = context;
        }

        /// <summary>
        /// Tạo Access Token có thời hạn 15 phút.
        /// GHI CHÚ BẢO MẬT: Sau khi đổi mật khẩu hoặc đổi Role, access token cũ vẫn có thể sống tối đa 15 phút
        /// vì JWT là stateless, không kiểm tra CSDL ở từng request API thông thường.
        /// </summary>
        public string GenerateAccessToken(User user)
        {
            var secret = _config["JwtSettings:Secret"];
            if (string.IsNullOrEmpty(secret))
                throw new InvalidOperationException("CRITICAL: JwtSettings:Secret is not configured!");

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId),
                new Claim(JwtRegisteredClaimNames.Sub, user.UserId),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("FullName", user.FullName)
            };

            var token = new JwtSecurityToken(
                issuer: _config["JwtSettings:Issuer"],
                audience: _config["JwtSettings:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(15), // Thời hạn chuẩn 15 phút
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public string GenerateToken(User user) => GenerateAccessToken(user);

        public async Task<string> CreateRefreshTokenAsync(string userId, string? ipAddress)
        {
            var rawToken = GenerateRawToken256Bit();
            var tokenHash = HashToken(rawToken);

            var entity = new RefreshToken
            {
                UserId = userId,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddDays(7), // Hạn 7 ngày
                RevokedAt = null,
                ReplacedBy = null,
                CreatedByIp = ipAddress,
                CreatedAt = DateTime.UtcNow
            };

            await _context.RefreshTokens.InsertOneAsync(entity);
            return rawToken;
        }

        public async Task<RotateResult> RotateRefreshTokenAsync(string rawToken, string? ipAddress)
        {
            if (string.IsNullOrWhiteSpace(rawToken))
                return RotateResult.Fail(RotateStatus.Invalid, "Refresh token không hợp lệ.");

            var hash = HashToken(rawToken);
            var nowUtc = DateTime.UtcNow;

            var newRawToken = GenerateRawToken256Bit();
            var newHash = HashToken(newRawToken);

            // 1. Cập nhật nguyên tử: TokenHash khớp, chưa bị revoke, chưa hết hạn
            var filter = Builders<RefreshToken>.Filter.Eq(r => r.TokenHash, hash)
                       & Builders<RefreshToken>.Filter.Eq(r => r.RevokedAt, null)
                       & Builders<RefreshToken>.Filter.Gt(r => r.ExpiresAt, nowUtc);

            var update = Builders<RefreshToken>.Update
                .Set(r => r.RevokedAt, nowUtc)
                .Set(r => r.ReplacedBy, newHash);

            var oldToken = await _context.RefreshTokens.FindOneAndUpdateAsync(filter, update);

            if (oldToken != null)
            {
                // Thêm token mới vào DB
                var newTokenEntity = new RefreshToken
                {
                    UserId = oldToken.UserId,
                    TokenHash = newHash,
                    ExpiresAt = nowUtc.AddDays(7),
                    RevokedAt = null,
                    ReplacedBy = null,
                    CreatedByIp = ipAddress,
                    CreatedAt = nowUtc
                };
                await _context.RefreshTokens.InsertOneAsync(newTokenEntity);

                // Luôn đọc Role hiện tại từ DB để gán vào access token mới (kịp thời phản ánh khi Admin đổi Role)
                var user = await _context.Users.Find(u => u.UserId == oldToken.UserId).FirstOrDefaultAsync();
                if (user == null)
                    return RotateResult.Fail(RotateStatus.Invalid, "Tài khoản không tồn tại hoặc đã bị xóa.");

                var newAccessToken = GenerateAccessToken(user);
                return RotateResult.Ok(newAccessToken, newRawToken, user.Role);
            }

            // 2. Nếu FindOneAndUpdate trả về null, kiểm tra nguyên nhân để xử lý an toàn
            var existing = await _context.RefreshTokens.Find(r => r.TokenHash == hash).FirstOrDefaultAsync();
            if (existing == null)
            {
                return RotateResult.Fail(RotateStatus.Invalid, "Refresh token không tồn tại.");
            }

            if (existing.ExpiresAt <= nowUtc)
            {
                return RotateResult.Fail(RotateStatus.Expired, "Refresh token đã hết hạn (7 ngày). Vui lòng đăng nhập lại.");
            }

            if (existing.RevokedAt != null)
            {
                // XỬ LÝ ĐUA LỆNH ĐỒNG THỜI (Race Condition Grace Period):
                // Nếu token vừa bị revoke trong vòng 15 giây gần nhất và có ReplacedBy,
                // đây là trường hợp 2 tab hoặc đa luồng cùng refresh đồng thời một lúc.
                // Cho phép cấp access token mới mà không đăng xuất oan user.
                if (existing.RevokedAt >= nowUtc.AddSeconds(-15) && !string.IsNullOrEmpty(existing.ReplacedBy))
                {
                    var replacedToken = await _context.RefreshTokens.Find(r => r.TokenHash == existing.ReplacedBy).FirstOrDefaultAsync();
                    if (replacedToken != null && replacedToken.RevokedAt == null && replacedToken.ExpiresAt > nowUtc)
                    {
                        var user = await _context.Users.Find(u => u.UserId == existing.UserId).FirstOrDefaultAsync();
                        if (user != null)
                        {
                            return RotateResult.Grace(GenerateAccessToken(user), user.Role);
                        }
                    }
                }

                // PHÁT HIỆN TÁI SỬ DỤNG TOKEN ĐÃ REVOKE (REPLAY ATTACK):
                // Token cũ ngoài thời gian ân hạn bị dùng lại -> Nghi vấn token bị lộ hoặc kẻ gian chiếm đoạt.
                // Lập tức thu hồi toàn bộ Refresh Tokens của User này để ngắt mọi phiên đăng nhập!
                await RevokeAllUserTokensAsync(existing.UserId);
                return RotateResult.Fail(RotateStatus.RevokedReuse, "Cảnh báo bảo mật: Token đã bị thu hồi trước đó. Toàn bộ phiên đăng nhập đã bị hủy để bảo vệ tài khoản.");
            }

            return RotateResult.Fail(RotateStatus.Invalid, "Refresh token không hợp lệ.");
        }

        public async Task RevokeRefreshTokenAsync(string rawToken)
        {
            if (string.IsNullOrWhiteSpace(rawToken)) return;
            var hash = HashToken(rawToken);
            await _context.RefreshTokens.UpdateOneAsync(
                r => r.TokenHash == hash && r.RevokedAt == null,
                Builders<RefreshToken>.Update.Set(r => r.RevokedAt, DateTime.UtcNow)
            );
        }

        public async Task RevokeAllUserTokensAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            await _context.RefreshTokens.UpdateManyAsync(
                r => r.UserId == userId && r.RevokedAt == null,
                Builders<RefreshToken>.Update.Set(r => r.RevokedAt, DateTime.UtcNow)
            );
        }

        private static string GenerateRawToken256Bit()
        {
            var bytes = new byte[32]; // 256 bits
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes)
                .Replace("+", "-")
                .Replace("/", "_")
                .Replace("=", "");
        }

        private static string HashToken(string token)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
