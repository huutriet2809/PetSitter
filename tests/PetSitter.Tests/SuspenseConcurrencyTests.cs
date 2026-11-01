using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using PetSitter.DTOs;
using PetSitter.Models;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class SuspenseConcurrencyTests : IAsyncLifetime
    {
        private MongoTestServer _server = null!;
        private PetSitterContext _context = null!;
        private LedgerService _ledgerService = null!;
        private PaymentService _paymentService = null!;

        public async Task InitializeAsync()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_Suspense_" + Guid.NewGuid().ToString("N")));

            // Unique index cho JournalEntry IdempotencyKey
            var keyBuilder = Builders<JournalEntry>.IndexKeys;
            var indexModel = new CreateIndexModel<JournalEntry>(
                keyBuilder.Ascending(j => j.IdempotencyKey),
                new CreateIndexOptions { Unique = true, Sparse = true });
            await _context.JournalEntries.Indexes.CreateOneAsync(indexModel);

            // Unique index cho RefundRequest SettlementKey (giống MongoSeeder)
            var refIndexModel = new CreateIndexModel<RefundRequest>(
                Builders<RefundRequest>.IndexKeys.Ascending(r => r.SettlementKey),
                new CreateIndexOptions { Unique = true, Sparse = true });
            await _context.RefundRequests.Indexes.CreateOneAsync(refIndexModel);

            var configDict = new Dictionary<string, string?>
            {
                ["Payments:EarningsHoldHours"] = "48",
                ["Payments:CommissionBps"] = "1000"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();

            _ledgerService = new LedgerService(_context);

            var gatewayMock = new Mock<IPaymentGateway>();
            var notifMock = new Mock<INotificationService>();
            var dataProtectionProvider = new EphemeralDataProtectionProvider();

            _paymentService = new PaymentService(
                _context,
                gatewayMock.Object,
                _ledgerService,
                notifMock.Object,
                dataProtectionProvider,
                config,
                NullLogger<PaymentService>.Instance,
                null
            );
        }

        public Task DisposeAsync()
        {
            _server?.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task AssignSuspense_ConcurrentAdmins_RecordsOnlyOneJournalEntry()
        {
            // Arrange
            var nowUtc = DateTime.UtcNow;
            long orderCode = 888101;
            var bookingId = ObjectId.GenerateNewId().ToString();

            var booking = new Booking
            {
                BookingId = bookingId,
                MaDatLich = "BK_CONCUR_01",
                OwnerId = ObjectId.GenerateNewId().ToString(),
                SitterId = ObjectId.GenerateNewId().ToString(),
                TrangThai = BookingStatus.ACCEPTED,
                PaymentStatus = PaymentStatus.PENDING_PAYMENT,
                TotalAmount = 250000,
                TongTien = 250000,
                CreatedAt = nowUtc,
                History = new List<InquiryHistory>()
            };
            await _context.Bookings.InsertOneAsync(booking);

            var abnormalPayment = new Payment
            {
                PaymentId = ObjectId.GenerateNewId().ToString(),
                OrderCode = orderCode,
                BookingId = "TEMP_ABNORMAL",
                Amount = 250000,
                ReceivedAmount = 250000,
                Status = PaymentLinkStatus.ABNORMAL,
                IsAbnormal = true,
                AbnormalReason = "Test abnormal",
                CreatedAt = nowUtc
            };
            await _context.Payments.InsertOneAsync(abnormalPayment);

            var dto = new AssignSuspenseDto
            {
                BookingId = bookingId,
                Note = "Admin gán đồng thời để test race condition"
            };

            // Act: 2 Admin cùng bấm gán cùng lúc bằng Task.WhenAll
            Task call1 = _paymentService.AssignSuspensePaymentAsync("ADMIN_ALICE", orderCode, dto, nowUtc);
            Task call2 = _paymentService.AssignSuspensePaymentAsync("ADMIN_BOB", orderCode, dto, nowUtc);

            int successCount = 0;
            int errorCount = 0;

            var tasks = new[] { call1, call2 }.Select(async t =>
            {
                try
                {
                    await t;
                    System.Threading.Interlocked.Increment(ref successCount);
                }
                catch
                {
                    System.Threading.Interlocked.Increment(ref errorCount);
                }
            });

            await Task.WhenAll(tasks);

            // Assert:
            // 1. Chỉ 1 request được ghi nhận thành công
            Assert.True(successCount >= 1, "Ít nhất một lệnh gán phải hoàn tất.");

            // 2. Booking chuyển sang HELD
            var updatedBooking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            Assert.NotNull(updatedBooking);
            Assert.Equal(PaymentStatus.HELD, updatedBooking.PaymentStatus);

            // 3. Quan trọng nhất: Bút toán sổ cái kép CHỈ ĐƯỢC GHI ĐÚNG 1 LẦN DUY NHẤT
            var expectedKey = LedgerIdempotencyKeys.SuspenseAssign(orderCode);
            var journalEntries = await _context.JournalEntries
                .Find(j => j.IdempotencyKey == expectedKey)
                .ToListAsync();

            Assert.Single(journalEntries);
            Assert.Equal(JournalType.SUSPENSE_ASSIGN_BOOKING, journalEntries[0].Type);
        }

        [Fact]
        public async Task RefundSuspense_ConcurrentAdmins_RecordsOnlyOneJournalEntryAndOneRefund()
        {
            // Arrange
            var nowUtc = DateTime.UtcNow;
            long orderCode = 888202;

            var abnormalPayment = new Payment
            {
                PaymentId = ObjectId.GenerateNewId().ToString(),
                OrderCode = orderCode,
                BookingId = "SUSPENSE_REFUND",
                Amount = 180000,
                ReceivedAmount = 180000,
                Status = PaymentLinkStatus.ABNORMAL,
                IsAbnormal = true,
                AbnormalReason = "Thanh toan sai noi dung",
                CreatedAt = nowUtc
            };
            await _context.Payments.InsertOneAsync(abnormalPayment);

            var dto = new RefundSuspenseDto
            {
                Reason = "Hoàn tiền do chuyển khoản thừa"
            };

            // Act: 2 Admin cùng bấm hoàn trả cùng một thời điểm
            Task call1 = _paymentService.RefundSuspensePaymentAsync("ADMIN_ALICE", orderCode, dto, nowUtc);
            Task call2 = _paymentService.RefundSuspensePaymentAsync("ADMIN_BOB", orderCode, dto, nowUtc);

            int successCount = 0;
            int errorCount = 0;

            var tasks = new[] { call1, call2 }.Select(async t =>
            {
                try
                {
                    await t;
                    System.Threading.Interlocked.Increment(ref successCount);
                }
                catch
                {
                    System.Threading.Interlocked.Increment(ref errorCount);
                }
            });

            await Task.WhenAll(tasks);

            // Assert:
            // 1. Ít nhất 1 admin thành công
            Assert.True(successCount >= 1, "Ít nhất một lệnh hoàn tiền phải thành công.");
            // 1. Chỉ có đúng 1 yêu cầu hoàn tiền (RefundRequest) được tạo
            var settlementKey = LedgerIdempotencyKeys.SuspenseRefund(orderCode);
            var refunds = await _context.RefundRequests
                .Find(r => r.SettlementKey == settlementKey)
                .ToListAsync();

            Assert.Single(refunds);
            Assert.Equal(RefundStatus.REFUND_PENDING, refunds[0].Status);

            // 2. Quan trọng nhất: Sổ cái chỉ ghi DUY NHẤT 1 bút toán
            var entries = await _context.JournalEntries
                .Find(j => j.IdempotencyKey == settlementKey)
                .ToListAsync();

            Assert.Single(entries);
            Assert.Equal(JournalType.SUSPENSE_REFUND_OWNER, entries[0].Type);
        }
    }
}
