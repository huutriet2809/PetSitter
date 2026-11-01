using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Models;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class RefreshTokenRotationTests : IDisposable
    {
        private readonly MongoTestServer _server;
        private readonly PetSitterContext _context;
        private readonly TokenService _tokenService;

        public RefreshTokenRotationTests()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_RefreshTokenDb"));

            var inMemorySettings = new Dictionary<string, string?>
            {
                { "JwtSettings:Secret", "Super_Secret_Key_For_PetSitter_Testing_Purposes_32chars_long!" },
                { "JwtSettings:Issuer", "PetSitterAppTest" },
                { "JwtSettings:Audience", "PetSitterUsersTest" },
                { "JwtSettings:ExpirationMinutes", "15" }
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _tokenService = new TokenService(configuration, _context, NullLogger<TokenService>.Instance);
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        private async Task<User> CreateTestUserAsync(bool isActive = true)
        {
            var user = new User
            {
                UserId = ObjectId.GenerateNewId().ToString(),
                Email = $"user_{Guid.NewGuid():N}@example.com",
                FullName = "Nguyễn Văn Test",
                Role = "OWNER",
                PasswordHash = "hashed_pw",
                IsActive = isActive,
                CreatedAt = DateTime.UtcNow
            };
            await _context.Users.InsertOneAsync(user);
            return user;
        }

        [Fact]
        public async Task Test1_RotateSuccess_ReturnsNewTokens_AndMarksOldTokenUsed()
        {
            // Arrange
            var user = await CreateTestUserAsync(isActive: true);
            var rawToken1 = await _tokenService.CreateRefreshTokenAsync(user.UserId, "127.0.0.1");

            // Act
            var rotateResult = await _tokenService.RotateRefreshTokenAsync(rawToken1, "127.0.0.1");

            // Assert
            Assert.Equal(RotateStatus.Success, rotateResult.Status);
            Assert.NotNull(rotateResult.AccessToken);
            Assert.NotNull(rotateResult.NewRefreshToken);
            Assert.NotEqual(rawToken1, rotateResult.NewRefreshToken);

            // Kiểm tra token cũ trong DB
            var hash1 = TokenService.HashToken(rawToken1);
            var oldTokenInDb = await _context.RefreshTokens.Find(r => r.TokenHash == hash1).FirstOrDefaultAsync();
            Assert.NotNull(oldTokenInDb);
            Assert.NotNull(oldTokenInDb.RevokedAt);
            Assert.NotNull(oldTokenInDb.ReplacedBy);

            // Kiểm tra token mới trong DB
            var hash2 = TokenService.HashToken(rotateResult.NewRefreshToken);
            Assert.Equal(hash2, oldTokenInDb.ReplacedBy);

            var newTokenInDb = await _context.RefreshTokens.Find(r => r.TokenHash == hash2).FirstOrDefaultAsync();
            Assert.NotNull(newTokenInDb);
            Assert.Null(newTokenInDb.RevokedAt);
            Assert.Null(newTokenInDb.ReplacedBy);
        }

        [Fact]
        public async Task Test2_ReuseTokenOutsideGraceWindow_RejectedAndRevokesChain()
        {
            // Arrange
            var user = await CreateTestUserAsync(isActive: true);
            var rawToken1 = await _tokenService.CreateRefreshTokenAsync(user.UserId, "127.0.0.1");

            // Lần 1: Xoay vòng thành công
            var firstRotate = await _tokenService.RotateRefreshTokenAsync(rawToken1, "127.0.0.1");
            Assert.Equal(RotateStatus.Success, firstRotate.Status);

            // Giả lập token1 đã bị thu hồi cách đây 30 giây (ngoài Grace Window 15s)
            var hash1 = TokenService.HashToken(rawToken1);
            await _context.RefreshTokens.UpdateOneAsync(
                r => r.TokenHash == hash1,
                Builders<RefreshToken>.Update.Set(r => r.RevokedAt, DateTime.UtcNow.AddSeconds(-30))
            );

            // Act: Cố dùng lại token1
            var secondRotate = await _tokenService.RotateRefreshTokenAsync(rawToken1, "127.0.0.1");

            // Assert: Bị từ chối do tái sử dụng (Reuse Detection)
            Assert.Equal(RotateStatus.RevokedReuse, secondRotate.Status);

            // Toàn bộ chuỗi refresh token của user phải bị thu hồi
            var activeTokens = await _context.RefreshTokens
                .Find(r => r.UserId == user.UserId && r.RevokedAt == null)
                .ToListAsync();
            Assert.Empty(activeTokens);
        }

        [Fact]
        public async Task Test3_ReuseTokenInsideGraceWindow_AcceptedOnceWithNewAccessToken()
        {
            // Arrange
            var user = await CreateTestUserAsync(isActive: true);
            var rawToken1 = await _tokenService.CreateRefreshTokenAsync(user.UserId, "127.0.0.1");

            // Lần 1: Xoay vòng thành công sang token 2
            var firstRotate = await _tokenService.RotateRefreshTokenAsync(rawToken1, "127.0.0.1");
            Assert.Equal(RotateStatus.Success, firstRotate.Status);

            // Act: Dùng lại token1 ngay lập tức (trong cửa sổ ân hạn 15s)
            var graceRotate = await _tokenService.RotateRefreshTokenAsync(rawToken1, "127.0.0.1");

            // Assert: Nhận được GracePeriod, chỉ cấp AccessToken mới, không cấp thêm RefreshToken
            Assert.Equal(RotateStatus.GracePeriod, graceRotate.Status);
            Assert.NotNull(graceRotate.AccessToken);
            Assert.Null(graceRotate.NewRefreshToken);

            // Token2 vẫn còn nguyên vẹn, không bị revoke oan
            var hash2 = TokenService.HashToken(firstRotate.NewRefreshToken!);
            var token2InDb = await _context.RefreshTokens.Find(r => r.TokenHash == hash2).FirstOrDefaultAsync();
            Assert.NotNull(token2InDb);
            Assert.Null(token2InDb.RevokedAt);
        }

        [Fact]
        public async Task Test4_UserIsActiveFalse_RejectedAndRevokesAllTokens()
        {
            // Arrange
            var user = await CreateTestUserAsync(isActive: true);
            var rawToken1 = await _tokenService.CreateRefreshTokenAsync(user.UserId, "127.0.0.1");

            // Khóa tài khoản
            await _context.Users.UpdateOneAsync(
                u => u.UserId == user.UserId,
                Builders<User>.Update.Set(u => u.IsActive, false)
            );

            // Act
            var rotateResult = await _tokenService.RotateRefreshTokenAsync(rawToken1, "127.0.0.1");

            // Assert
            Assert.Equal(RotateStatus.Invalid, rotateResult.Status);
            Assert.Contains("vô hiệu hóa", rotateResult.Message);

            // Toàn bộ refresh token của user bị thu hồi ngay lập tức
            var activeTokens = await _context.RefreshTokens
                .Find(r => r.UserId == user.UserId && r.RevokedAt == null)
                .ToListAsync();
            Assert.Empty(activeTokens);
        }
    }
}
