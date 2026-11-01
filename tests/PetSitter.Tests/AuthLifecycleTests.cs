using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using PetSitter.Repositories;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class AuthLifecycleTests : IDisposable
    {
        private readonly MongoTestServer _server;
        private readonly PetSitterContext _context;
        private readonly UserService _userService;
        private readonly TokenService _tokenService;

        public AuthLifecycleTests()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_AuthLifecycle_" + Guid.NewGuid().ToString("N")));

            var inMemorySettings = new Dictionary<string, string?>
            {
                { "JwtSettings:Secret", "Super_Secret_Key_For_PetSitter_Testing_Purposes_32chars_long!" },
                { "JwtSettings:Issuer", "PetSitterAppTest" },
                { "JwtSettings:Audience", "PetSitterUsersTest" },
                { "JwtSettings:ExpirationMinutes", "15" },
                { "AccountLockout:MaxFailedAttempts", "5" },
                { "AccountLockout:LockoutMinutes", "15" }
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _tokenService = new TokenService(configuration, _context, NullLogger<TokenService>.Instance);
            var userRepository = new UserRepository(_context);

            _userService = new UserService(userRepository, _tokenService, _context, configuration);
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        private static string ComputeSha256(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLower();
        }

        [Fact]
        public async Task CompleteAuthLifecycle_Register_Login_Lockout_ResetPassword_ChangePassword_LogoutAll()
        {
            var email = "lifecycle_user@pawstay.vn";
            var initialPassword = "InitialPassword123!";
            var resetPassword = "ResetPassword456!";
            var changedPassword = "ChangedPassword789!";

            // 1. Đăng ký tài khoản (Register)
            var registerDto = new RegisterDto
            {
                FullName = "Nguyễn Văn Test",
                Email = email,
                Password = initialPassword
            };
            var authResult = await _userService.RegisterAsync(registerDto);
            Assert.NotNull(authResult);
            Assert.NotEmpty(authResult.Token);
            var userId = authResult.UserId;

            // 2. Đăng nhập đúng (Login Correct)
            var loginDto = new LoginDto { Email = email, Password = initialPassword };
            var loggedIn = await _userService.AuthenticateAsync(loginDto);
            Assert.NotNull(loggedIn);

            // 3. Đăng nhập sai 5 lần -> Bị khóa (Lockout)
            var wrongDto = new LoginDto { Email = email, Password = "WrongPassword!" };
            for (int i = 1; i <= 4; i++)
            {
                var fail = await _userService.AuthenticateAsync(wrongDto);
                Assert.Null(fail);
            }
            var ex = await Assert.ThrowsAsync<ForbiddenException>(() => _userService.AuthenticateAsync(wrongDto));
            Assert.Contains("khóa", ex.Message);

            // Đăng nhập mật khẩu đúng khi đang bị khóa -> Vẫn bị chặn
            var lockedEx = await Assert.ThrowsAsync<ForbiddenException>(() => _userService.AuthenticateAsync(loginDto));
            Assert.Contains("khóa", lockedEx.Message);

            // 4. Mô phỏng luồng Quên mật khẩu & Tạo reset token
            var tokenBytes = new byte[32];
            RandomNumberGenerator.Fill(tokenBytes);
            var rawToken = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
            var tokenHash = ComputeSha256(rawToken);

            var reset = new PasswordReset
            {
                Email = email,
                TokenHash = tokenHash,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                Used = false
            };
            await _context.PasswordResets.InsertOneAsync(reset);

            // 5. Đặt lại mật khẩu (Reset Password)
            var resetRecord = await _context.PasswordResets.Find(r => r.TokenHash == tokenHash && !r.Used && r.ExpiresAt > DateTime.UtcNow).FirstOrDefaultAsync();
            Assert.NotNull(resetRecord);

            var update = Builders<User>.Update
                .Set(u => u.PasswordHash, BCrypt.Net.BCrypt.HashPassword(resetPassword))
                .Set(u => u.FailedLoginAttempts, 0)
                .Set(u => u.LockoutEndUtc, null);
            await _context.Users.UpdateOneAsync(u => u.UserId == userId, update);

            var resetUpdate = Builders<PasswordReset>.Update.Set(r => r.Used, true);
            await _context.PasswordResets.UpdateOneAsync(r => r.Id == resetRecord.Id, resetUpdate);

            // 6. Đăng nhập với mật khẩu mới thành công
            var loginWithResetDto = new LoginDto { Email = email, Password = resetPassword };
            var loggedWithReset = await _userService.AuthenticateAsync(loginWithResetDto);
            Assert.NotNull(loggedWithReset);

            // 7. Đổi mật khẩu (Change Password)
            await _userService.ChangePasswordAsync(userId, new ChangePasswordDto
            {
                CurrentPassword = resetPassword,
                NewPassword = changedPassword
            });

            // Mật khẩu cũ không đăng nhập được nữa
            var oldLoginFail = await _userService.AuthenticateAsync(loginWithResetDto);
            Assert.Null(oldLoginFail);

            // Mật khẩu đã đổi đăng nhập thành công
            var newLoginDto = new LoginDto { Email = email, Password = changedPassword };
            var loggedWithChanged = await _userService.AuthenticateAsync(newLoginDto);
            Assert.NotNull(loggedWithChanged);

            // 8. Đăng xuất tất cả thiết bị (Logout All)
            await _tokenService.CreateRefreshTokenAsync(userId, "127.0.0.1");
            await _tokenService.RevokeAllUserTokensAsync(userId);
            var activeTokens = await _context.RefreshTokens.Find(r => r.UserId == userId && r.RevokedAt == null).ToListAsync();
            Assert.Empty(activeTokens);
        }
    }
}
