using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
    public class TestLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> LoggedMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter != null)
            {
                LoggedMessages.Enqueue(formatter(state, exception));
            }
        }
    }

    public class AuditLogTests : IAsyncLifetime
    {
        private MongoTestServer _server = null!;
        private PetSitterContext _context = null!;

        public Task InitializeAsync()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_Audit_" + Guid.NewGuid().ToString("N")));
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            _server.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task AuditLog_ResolvedDispute_LogsAuditMessage()
        {
            var logger = new TestLogger<BookingService>();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var ledgerService = new LedgerService(_context);
            var bookingService = new BookingService(_context, config, null, ledgerService, null, logger, null);

            var adminId = ObjectId.GenerateNewId().ToString();
            var ownerId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();
            var bookingId = ObjectId.GenerateNewId().ToString();

            await _context.SitterProfiles.InsertOneAsync(new SitterProfile { SitterId = sitterId, UserId = sitterUserId, Status = "Approved" });
            await _context.Bookings.InsertOneAsync(new Booking
            {
                BookingId = bookingId,
                OwnerId = ownerId,
                MaDatLich = "PS_AUDIT_1",
                SitterId = sitterId,
                SitterUserId = sitterUserId,
                TrangThai = BookingStatus.DISPUTED,
                PaymentStatus = PaymentStatus.HELD,
                TotalAmount = 500000L,
                TongTien = 500000L
            });

            await bookingService.AdminResolveDisputeAsync(adminId, bookingId, new ResolveDisputeDto
            {
                Resolution = BookingStatus.CANCELLED,
                Note = "Admin quyết định hủy đơn và hoàn tiền."
            });

            Assert.Contains(logger.LoggedMessages, msg => msg.Contains("[AUDIT]") && msg.Contains(adminId) && msg.Contains(bookingId));
        }

        [Fact]
        public async Task AuditLog_BankReconciliation_LogsAuditMessage()
        {
            var logger = new TestLogger<BankReconciliationService>();
            var parser = new BankStatementParser();
            var reconService = new BankReconciliationService(_context, parser, logger);

            var adminId = ObjectId.GenerateNewId().ToString();
            var csv = "NgayGiaoDich,MoTa,SoTien,MaThamChieu\r\n01/11/2026,Giao dich,100.000,REF1\r\n";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

            await reconService.ReconcileStatementAsync(stream, "test_audit.csv", stream.Length, adminId);

            Assert.Contains(logger.LoggedMessages, msg => msg.Contains("[AUDIT]") && msg.Contains(adminId) && msg.Contains("test_audit.csv"));
        }

        [Fact]
        public async Task AuditLog_AdminSetUserActive_LogsAuditMessage()
        {
            var logger = new TestLogger<AdminController>();
            var tokenServiceMock = new Mock<ITokenService>();
            var bookingServiceMock = new Mock<IBookingService>();

            var adminController = new AdminController(
                _context,
                bookingServiceMock.Object,
                tokenServiceMock.Object,
                null,
                logger
            );

            var adminId = ObjectId.GenerateNewId().ToString();
            var targetUserId = ObjectId.GenerateNewId().ToString();

            await _context.Users.InsertOneAsync(new User
            {
                UserId = targetUserId,
                Email = "violate_user@pawstay.vn",
                FullName = "Violate User",
                IsActive = true
            });

            // Giả lập HttpContext với Claims của Admin
            var userPrincipal = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, adminId),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "ADMIN")
            }, "TestAuth"));

            adminController.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = userPrincipal }
            };

            // Khóa tài khoản
            var actionResult = await adminController.UpdateUserStatus(targetUserId, new UpdateUserStatusDto { IsActive = false });
            Assert.IsType<OkObjectResult>(actionResult);

            Assert.Contains(logger.LoggedMessages, msg => msg.Contains("[AUDIT]") && msg.Contains(adminId) && msg.Contains(targetUserId) && msg.Contains("khóa"));
        }
    }
}

