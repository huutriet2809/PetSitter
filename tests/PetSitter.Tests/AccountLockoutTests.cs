using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Helpers;
using PetSitter.Models;
using PetSitter.Repositories;
using PetSitter.Services;
using PetSitter.Utils;
using Xunit;

namespace PetSitter.Tests
{
    public class AccountLockoutTests : IDisposable
    {
        private readonly MongoTestServer _server;
        private readonly PetSitterContext _context;
        private readonly UserService _userService;
        private const string TestPassword = "CorrectPassword123!";

        public AccountLockoutTests()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_LockoutDb"));

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

            var tokenService = new TokenService(configuration, _context, NullLogger<TokenService>.Instance);
            var userRepository = new UserRepository(_context);

            _userService = new UserService(userRepository, tokenService, _context, configuration);
        }

        public void Dispose()
        {
            _server.Dispose();
        }

        private async Task<User> CreateUserAsync(string email)
        {
            var user = new User
            {
                UserId = ObjectId.GenerateNewId().ToString(),
                Email = email.ToLower(),
                FullName = "Người dùng Thử nghiệm",
                Role = "OWNER",
                PasswordHash = PasswordHasher.HashPassword(TestPassword),
                IsActive = true,
                FailedLoginAttempts = 0,
                LockoutEndUtc = null,
                CreatedAt = DateTime.UtcNow
            };
            await _context.Users.InsertOneAsync(user);
            return user;
        }

        [Fact]
        public async Task Test1_FailedFiveTimes_LocksAccountFor15Minutes()
        {
            // Arrange
            var email = "lockout_test_1@example.com";
            var user = await CreateUserAsync(email);

            var wrongDto = new LoginDto { Email = email, Password = "WrongPassword!" };

            // Act: Nhập sai 4 lần đầu -> trả về null (mật khẩu sai)
            for (int i = 1; i <= 4; i++)
            {
                var result = await _userService.AuthenticateAsync(wrongDto);
                Assert.Null(result);

                var userAfterTry = await _context.Users.Find(u => u.UserId == user.UserId).FirstOrDefaultAsync();
                Assert.Equal(i, userAfterTry.FailedLoginAttempts);
                Assert.Null(userAfterTry.LockoutEndUtc);
            }

            // Lần thứ 5: Đạt ngưỡng khóa -> ném ForbiddenException thông báo tài khoản bị tạm khóa
            var fifthEx = await Assert.ThrowsAsync<ForbiddenException>(() =>
                _userService.AuthenticateAsync(wrongDto)
            );
            Assert.Contains("tạm khóa", fifthEx.Message);

            var userAfterFifth = await _context.Users.Find(u => u.UserId == user.UserId).FirstOrDefaultAsync();
            Assert.NotNull(userAfterFifth.LockoutEndUtc);
            Assert.True(userAfterFifth.LockoutEndUtc.Value > DateTime.UtcNow.AddMinutes(14));

            // Lần thứ 6 (kể cả gõ đúng hay sai): Ném ForbiddenException
            var ex = await Assert.ThrowsAsync<ForbiddenException>(() =>
                _userService.AuthenticateAsync(new LoginDto { Email = email, Password = TestPassword })
            );
            Assert.Contains("tạm thời bị khóa", ex.Message);
        }

        [Fact]
        public async Task Test2_SuccessfulLogin_ResetsFailedAttemptsAndLockout()
        {
            // Arrange
            var email = "lockout_test_2@example.com";
            var user = await CreateUserAsync(email);

            // Giả lập đã nhập sai 3 lần trước đó
            await _context.Users.UpdateOneAsync(
                u => u.UserId == user.UserId,
                Builders<User>.Update.Set(u => u.FailedLoginAttempts, 3)
            );

            // Act: Đăng nhập đúng mật khẩu
            var successDto = new LoginDto { Email = email, Password = TestPassword };
            var authResult = await _userService.AuthenticateAsync(successDto);

            // Assert
            Assert.NotNull(authResult);
            Assert.Equal(email, authResult.Email);

            // Bộ đếm phải được reset về 0
            var userInDb = await _context.Users.Find(u => u.UserId == user.UserId).FirstOrDefaultAsync();
            Assert.Equal(0, userInDb.FailedLoginAttempts);
            Assert.Null(userInDb.LockoutEndUtc);
        }
    }
}
