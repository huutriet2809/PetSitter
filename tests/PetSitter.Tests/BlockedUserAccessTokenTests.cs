using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using PetSitter.Controllers;
using PetSitter.DTOs;
using PetSitter.Models;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class BlockedUserAccessTokenTests : IAsyncLifetime
    {
        private MongoTestServer _server = null!;
        private PetSitterContext _context = null!;
        private IMemoryCache _memoryCache = null!;

        public Task InitializeAsync()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_BlockedToken_" + Guid.NewGuid().ToString("N")));
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            _server.Dispose();
            _memoryCache.Dispose();
            return Task.CompletedTask;
        }

        private async Task<bool> SimulateTokenValidationAsync(string userId, HttpContext httpContext)
        {
            bool isFailed = false;

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim("sub", userId)
            };
            var identity = new ClaimsIdentity(claims, "Bearer");
            var principal = new ClaimsPrincipal(identity);

            var authScheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
            var tokenValidatedContext = new TokenValidatedContext(httpContext, authScheme, new JwtBearerOptions())
            {
                Principal = principal
            };

            // Mô phỏng logic OnTokenValidated từ Program.cs
            var uid = tokenValidatedContext.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? tokenValidatedContext.Principal?.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(uid))
            {
                tokenValidatedContext.Fail("Token thiếu định danh người dùng.");
                return false;
            }

            var cache = httpContext.RequestServices.GetRequiredService<IMemoryCache>();
            var cacheKey = $"user_active_status_{uid}";

            if (!cache.TryGetValue(cacheKey, out bool isActive))
            {
                try
                {
                    var dbContext = httpContext.RequestServices.GetRequiredService<PetSitterContext>();
                    var user = await dbContext.Users.Find(u => u.UserId == uid).FirstOrDefaultAsync();

                    if (user == null)
                    {
                        tokenValidatedContext.Fail("Tài khoản của bạn không tồn tại hoặc đã bị xóa.");
                        return false;
                    }

                    isActive = user.IsActive != false;
                    cache.Set(cacheKey, isActive, TimeSpan.FromSeconds(30));
                }
                catch (Exception)
                {
                    // Fail-Closed strategy: Khi DB lỗi tạm thời, từ chối token để đảm bảo an toàn
                    tokenValidatedContext.Fail("Hệ thống tạm thời không thể xác thực tài khoản. Vui lòng thử lại sau.");
                    return false;
                }
            }

            if (!isActive)
            {
                tokenValidatedContext.Fail("Tài khoản của bạn đã bị khóa hoặc ngừng hoạt động.");
                isFailed = true;
            }

            return !isFailed;
        }

        [Fact]
        public async Task ActiveUser_AccessTokenValidated_Accepted()
        {
            var userId = ObjectId.GenerateNewId().ToString();
            await _context.Users.InsertOneAsync(new User
            {
                UserId = userId,
                Email = "active@pawstay.vn",
                FullName = "Active User",
                IsActive = true
            });

            var services = new ServiceCollection();
            services.AddSingleton(_context);
            services.AddSingleton(_memoryCache);
            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

            var isValid = await SimulateTokenValidationAsync(userId, httpContext);
            Assert.True(isValid);

            // Xác nhận trạng thái đã được lưu vào cache 30s
            Assert.True(_memoryCache.TryGetValue($"user_active_status_{userId}", out bool cachedActive));
            Assert.True(cachedActive);
        }

        [Fact]
        public async Task LockedUser_AccessTokenValidated_RejectedImmediately()
        {
            var userId = ObjectId.GenerateNewId().ToString();
            await _context.Users.InsertOneAsync(new User
            {
                UserId = userId,
                Email = "locked@pawstay.vn",
                FullName = "Locked User",
                IsActive = false // Tài khoản đã bị khóa
            });

            var services = new ServiceCollection();
            services.AddSingleton(_context);
            services.AddSingleton(_memoryCache);
            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

            var isValid = await SimulateTokenValidationAsync(userId, httpContext);
            Assert.False(isValid); // Token bị từ chối ngay lập tức
        }

        [Fact]
        public async Task AdminLocksUser_InvalidatesCache_TokenInstantlyRejectedWithoutWaitingExpiry()
        {
            var userId = ObjectId.GenerateNewId().ToString();
            var adminId = ObjectId.GenerateNewId().ToString();

            await _context.Users.InsertOneAsync(new User
            {
                UserId = userId,
                Email = "to_be_locked@pawstay.vn",
                FullName = "To Be Locked",
                IsActive = true
            });

            var services = new ServiceCollection();
            services.AddSingleton(_context);
            services.AddSingleton(_memoryCache);
            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

            // 1. Lúc đầu user active -> Token hợp lệ
            var initialValid = await SimulateTokenValidationAsync(userId, httpContext);
            Assert.True(initialValid);

            // 2. Admin gọi API khóa tài khoản người dùng
            var tokenServiceMock = new Mock<ITokenService>();
            var bookingServiceMock = new Mock<IBookingService>();
            var adminController = new AdminController(
                _context,
                bookingServiceMock.Object,
                tokenServiceMock.Object,
                null,
                null,
                _memoryCache
            );

            adminController.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, adminId),
                        new Claim(ClaimTypes.Role, "ADMIN")
                    }, "TestAuth"))
                }
            };

            await adminController.UpdateUserStatus(userId, new UpdateUserStatusDto { IsActive = false });

            // 3. User tiếp tục dùng access token cũ vừa có -> Ngay lập tức bị từ chối vì cache đã set IsActive = false
            var validAfterLock = await SimulateTokenValidationAsync(userId, httpContext);
            Assert.False(validAfterLock);
        }

        [Fact]
        public async Task UserDeletedFromDb_AccessTokenValidated_Rejected()
        {
            var nonExistentUserId = ObjectId.GenerateNewId().ToString();

            var services = new ServiceCollection();
            services.AddSingleton(_context);
            services.AddSingleton(_memoryCache);
            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

            // User không tồn tại trong DB -> bị từ chối
            var isValid = await SimulateTokenValidationAsync(nonExistentUserId, httpContext);
            Assert.False(isValid);
        }

        [Fact]
        public async Task DatabaseErrorOccurs_FailClosedStrategy_TokenRejected()
        {
            var userId = ObjectId.GenerateNewId().ToString();

            // Client trỏ tới port không tồn tại để cố tình gây lỗi kết nối MongoDB
            var deadClient = new MongoClient("mongodb://127.0.0.1:59999/?connectTimeoutMS=100&serverSelectionTimeoutMS=100");
            var deadContext = new PetSitterContext(deadClient.GetDatabase("DeadDb"));

            var services = new ServiceCollection();
            services.AddSingleton(deadContext);
            services.AddSingleton(_memoryCache);
            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

            // Khi DB gặp sự cố, hệ thống thực thi Fail-Closed (không cho qua im lặng)
            var isValid = await SimulateTokenValidationAsync(userId, httpContext);
            Assert.False(isValid);
        }

        [Fact]
        public async Task CacheExpired_RequeriesDatabase_ReflectsUpdatedState()
        {
            var userId = ObjectId.GenerateNewId().ToString();
            await _context.Users.InsertOneAsync(new User
            {
                UserId = userId,
                Email = "cache_test@pawstay.vn",
                FullName = "Cache Test User",
                IsActive = true
            });

            var services = new ServiceCollection();
            services.AddSingleton(_context);
            services.AddSingleton(_memoryCache);
            var serviceProvider = services.BuildServiceProvider();

            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };

            // 1. Lần đầu: Active
            var firstCheck = await SimulateTokenValidationAsync(userId, httpContext);
            Assert.True(firstCheck);

            // 2. Trực tiếp sửa DB thành IsActive = false (không qua API để không trigger InvalidateCache)
            await _context.Users.UpdateOneAsync(
                u => u.UserId == userId,
                Builders<User>.Update.Set(u => u.IsActive, false)
            );

            // 3. Trong thời gian cache còn hiệu lực (chưa hết hạn): vẫn đọc từ cache
            var cachedCheck = await SimulateTokenValidationAsync(userId, httpContext);
            Assert.True(cachedCheck);

            // 4. Giả lập cache hết hạn (Remove key khỏi MemoryCache)
            _memoryCache.Remove($"user_active_status_{userId}");

            // 5. Sau khi cache hết hạn: Đọc lại DB và thấy trạng thái mới (false) -> bị từ chối
            var afterExpiryCheck = await SimulateTokenValidationAsync(userId, httpContext);
            Assert.False(afterExpiryCheck);
        }
    }
}

