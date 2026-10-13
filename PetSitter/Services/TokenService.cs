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
            var nowUtc = DateTime.UtcNow;

            var entity = new RefreshToken
            {
                UserId = userId,
                TokenHash = tokenHash,
                ExpiresAt = nowUtc.AddDays(7), // Hạn trượt 7 ngày
                AbsoluteExpiresAt = nowUtc.AddDays(30), // Trần tuyệt đối 30 ngày từ lúc đăng nhập ban đầu
                RevokedAt = null,
                ReplacedBy = null,
                CreatedByIp = ipAddress,
                CreatedAt = nowUtc
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

            // Kiểm tra token hiện tại
            var existing = await _context.RefreshTokens.Find(r => r.TokenHash == hash).FirstOrDefaultAsync();
            if (existing == null)
            {
                return RotateResult.Fail(RotateStatus.Invalid, "Refresh token không tồn tại.");
            }

            // Kiểm tra hết hạn: hoặc quá hạn trượt (ExpiresAt), hoặc chạm trần tuyệt đối (AbsoluteExpiresAt)
            if (existing.ExpiresAt <= nowUtc || (existing.AbsoluteExpiresAt != default && existing.AbsoluteExpiresAt <= nowUtc))
            {
                return RotateResult.Fail(RotateStatus.Expired, "Refresh token đã hết hạn phiên đăng nhập (tối đa 30 ngày). Vui lòng đăng nhập lại.");
            }

            // Nếu token ĐÃ bị revoke trước đó: kiểm tra ân hạn hoặc thu hồi toàn bộ (Replay Attack)
            if (existing.RevokedAt != null)
            {
                // GHI CHÚ BẢO MẬT: Ân hạn 15 giây (Grace Period) là sự đánh đổi có chủ ý (deliberate trade-off)
                // giữa tính khả dụng (tránh đăng xuất oan khi trình duyệt mở nhiều tab hoặc đa luồng gửi request song song)
                // và tính nghiêm ngặt tuyệt đối của token rotation.
                // Trong cửa sổ 15s này, bên thua race chỉ nhận Access Token mới (15 phút), KHÔNG được cấp Refresh Token mới.
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

                // Token đã revoke ngoài thời gian ân hạn hoặc không có ReplacedBy (logout, đổi pass, reuse attack)
                await RevokeAllUserTokensAsync(existing.UserId);
                return RotateResult.Fail(RotateStatus.RevokedReuse, "Cảnh báo bảo mật: Token đã bị thu hồi trước đó. Toàn bộ phiên đăng nhập đã bị hủy để bảo vệ tài khoản.");
            }

            // Chuẩn bị token mới kế thừa AbsoluteExpiresAt
            var newRawToken = GenerateRawToken256Bit();
            var newHash = HashToken(newRawToken);

            var absoluteExpiry = existing.AbsoluteExpiresAt != default ? existing.AbsoluteExpiresAt : nowUtc.AddDays(30);
            var newExpiresAt = nowUtc.AddDays(7);
            if (newExpiresAt > absoluteExpiry)
            {
                newExpiresAt = absoluteExpiry; // Không vượt quá trần tuyệt đối
            }

            var newTokenEntity = new RefreshToken
            {
                UserId = existing.UserId,
                TokenHash = newHash,
                ExpiresAt = newExpiresAt,
                AbsoluteExpiresAt = absoluteExpiry,
                RevokedAt = null,
                ReplacedBy = null,
                CreatedByIp = ipAddress,
                CreatedAt = nowUtc
            };

            // BƯỚC 1: Insert token mới TRƯỚC để request song song đến giữa chừng luôn tìm thấy replacedToken
            await _context.RefreshTokens.InsertOneAsync(newTokenEntity);

            // BƯỚC 2: Cập nhật nguyên tử token cũ
            var filter = Builders<RefreshToken>.Filter.Eq(r => r.TokenHash, hash)
                       & Builders<RefreshToken>.Filter.Eq(r => r.RevokedAt, null)
                       & Builders<RefreshToken>.Filter.Gt(r => r.ExpiresAt, nowUtc);

            var update = Builders<RefreshToken>.Update
                .Set(r => r.RevokedAt, nowUtc)
                .Set(r => r.ReplacedBy, newHash);

            var oldToken = await _context.RefreshTokens.FindOneAndUpdateAsync(filter, update);

            if (oldToken != null)
            {
                // Thắng race condition: trả về access token và refresh token mới
                var user = await _context.Users.Find(u => u.UserId == oldToken.UserId).FirstOrDefaultAsync();
                if (user == null)
                    return RotateResult.Fail(RotateStatus.Invalid, "Tài khoản không tồn tại hoặc đã bị xóa.");

                var newAccessToken = GenerateAccessToken(user);
                return RotateResult.Ok(newAccessToken, newRawToken, user.Role);
            }

            // BƯỚC 3: Nếu update không match (thua race condition với request song song):
            // Xóa ngay token vừa insert thừa
            await _context.RefreshTokens.DeleteOneAsync(r => r.TokenHash == newHash);

            // Đọc lại token cũ từ DB và đi tiếp vào nhánh ân hạn
            var updatedExisting = await _context.RefreshTokens.Find(r => r.TokenHash == hash).FirstOrDefaultAsync();
            if (updatedExisting != null && updatedExisting.RevokedAt != null)
            {
                if (updatedExisting.RevokedAt >= nowUtc.AddSeconds(-15) && !string.IsNullOrEmpty(updatedExisting.ReplacedBy))
                {
                    var replacedToken = await _context.RefreshTokens.Find(r => r.TokenHash == updatedExisting.ReplacedBy).FirstOrDefaultAsync();
                    if (replacedToken != null && replacedToken.RevokedAt == null && replacedToken.ExpiresAt > nowUtc)
                    {
                        var user = await _context.Users.Find(u => u.UserId == updatedExisting.UserId).FirstOrDefaultAsync();
                        if (user != null)
                        {
                            return RotateResult.Grace(GenerateAccessToken(user), user.Role);
                        }
                    }
                }

                await RevokeAllUserTokensAsync(updatedExisting.UserId);
                return RotateResult.Fail(RotateStatus.RevokedReuse, "Cảnh báo bảo mật: Token đã bị thu hồi trước đó. Toàn bộ phiên đăng nhập đã bị hủy.");
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
