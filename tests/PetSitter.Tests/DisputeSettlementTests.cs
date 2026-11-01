using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class DisputeSettlementTests : IAsyncLifetime
    {
        private MongoTestServer _server = null!;
        private PetSitterContext _context = null!;
        private LedgerService _ledgerService = null!;
        private BookingService _bookingService = null!;

        public async Task InitializeAsync()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_Dispute_" + Guid.NewGuid().ToString("N")));

            // Khởi tạo index cho sổ cái
            var keyBuilder = Builders<JournalEntry>.IndexKeys;
            var indexModel = new CreateIndexModel<JournalEntry>(
                keyBuilder.Ascending(j => j.IdempotencyKey),
                new CreateIndexOptions { Unique = true, Sparse = true });
            await _context.JournalEntries.Indexes.CreateOneAsync(indexModel);

            var notifKeyBuilder = Builders<Notification>.IndexKeys;
            var notifIndexModel = new CreateIndexModel<Notification>(
                notifKeyBuilder.Ascending(n => n.DedupeKey),
                new CreateIndexOptions { Unique = true, Sparse = true });
            await _context.Notifications.Indexes.CreateOneAsync(notifIndexModel);

            var configDict = new Dictionary<string, string?>
            {
                ["Payments:EarningsHoldHours"] = "48",
                ["Payments:CommissionBps"] = "1000" // 10%
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(configDict).Build();

            _ledgerService = new LedgerService(_context);
            var notificationService = new NotificationService(_context, config, NullLogger<NotificationService>.Instance);

            _bookingService = new BookingService(
                _context,
                config,
                notificationService,
                _ledgerService,
                null,
                NullLogger<BookingService>.Instance,
                null
            );
        }

        public Task DisposeAsync()
        {
            _server.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task Dispute_FullRefund_Cancelled_RecordsJournalAndRefundRequest()
        {
            var adminUserId = ObjectId.GenerateNewId().ToString();
            var ownerId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();
            var bookingId = ObjectId.GenerateNewId().ToString();

            await _context.SitterProfiles.InsertOneAsync(new SitterProfile
            {
                SitterId = sitterId,
                UserId = sitterUserId,
                Status = "Approved"
            });

            var totalAmount = 1000000L;
            await _context.Bookings.InsertOneAsync(new Booking
            {
                BookingId = bookingId,
                MaDatLich = "PS1001",
                OwnerId = ownerId,
                SitterId = sitterId,
                SitterUserId = sitterUserId,
                TrangThai = BookingStatus.DISPUTED,
                PaymentStatus = PaymentStatus.HELD,
                TotalAmount = totalAmount,
                TongTien = totalAmount,
                CommissionBps = 1000,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            });

            var resolveDto = new ResolveDisputeDto
            {
                Resolution = BookingStatus.CANCELLED,
                Note = "Admin quyết định hủy đơn và hoàn trả 100% cho chủ nuôi do sitter vắng mặt."
            };

            var result = await _bookingService.AdminResolveDisputeAsync(adminUserId, bookingId, resolveDto);

            Assert.Equal(BookingStatus.CANCELLED, result.TrangThai);
            Assert.Equal(PaymentStatus.REFUND_PENDING, result.PaymentStatus);

            // Kiểm tra RefundRequest
            var refundReq = await _context.RefundRequests.Find(r => r.BookingId == bookingId).FirstOrDefaultAsync();
            Assert.NotNull(refundReq);
            Assert.Equal(totalAmount, refundReq.Amount);

            // Kiểm tra Sổ cái JournalEntry: Debit ESCROW_HELD, Credit REFUND_PENDING
            var entries = await _context.JournalEntries.Find(j => j.BookingId == bookingId).ToListAsync();
            Assert.Single(entries);
            var entry = entries[0];
            Assert.Equal("ADMIN_CANCEL", entry.Type);

            var debitLine = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.ESCROW_HELD);
            var creditLine = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.REFUND_PENDING);

            Assert.NotNull(debitLine);
            Assert.Equal(LedgerDirection.DEBIT, debitLine.Direction);
            Assert.Equal(totalAmount, debitLine.Amount);

            Assert.NotNull(creditLine);
            Assert.Equal(LedgerDirection.CREDIT, creditLine.Direction);
            Assert.Equal(totalAmount, creditLine.Amount);
        }

        [Fact]
        public async Task Dispute_PaySitter_Completed_ReleasesEscrowCorrectly()
        {
            var adminUserId = ObjectId.GenerateNewId().ToString();
            var ownerId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();
            var bookingId = ObjectId.GenerateNewId().ToString();

            await _context.SitterProfiles.InsertOneAsync(new SitterProfile
            {
                SitterId = sitterId,
                UserId = sitterUserId,
                Status = "Approved"
            });

            var totalAmount = 1000000L;
            await _context.Bookings.InsertOneAsync(new Booking
            {
                BookingId = bookingId,
                MaDatLich = "PS1002",
                OwnerId = ownerId,
                SitterId = sitterId,
                SitterUserId = sitterUserId,
                TrangThai = BookingStatus.DISPUTED,
                PaymentStatus = PaymentStatus.HELD,
                TotalAmount = totalAmount,
                TongTien = totalAmount,
                CommissionBps = 1000, // 10% = 100.000 ₫
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            });

            var resolveDto = new ResolveDisputeDto
            {
                Resolution = BookingStatus.COMPLETED,
                Note = "Sitter đã thực hiện đầy đủ dịch vụ với bằng chứng hình ảnh rõ ràng. Duyệt hoàn tất."
            };

            var result = await _bookingService.AdminResolveDisputeAsync(adminUserId, bookingId, resolveDto);

            Assert.Equal(BookingStatus.COMPLETED, result.TrangThai);
            Assert.Equal(PaymentStatus.RELEASED, result.PaymentStatus);

            // Kiểm tra Sổ cái JournalEntry: Debit ESCROW_HELD 1tr, Credit Sitter 900k, Credit Sàn 100k
            var entries = await _context.JournalEntries.Find(j => j.BookingId == bookingId).ToListAsync();
            Assert.Single(entries);
            var entry = entries[0];
            Assert.Equal("ESCROW_RELEASE", entry.Type);

            var debitEscrow = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.ESCROW_HELD);
            var creditSitter = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.SitterEarnings(sitterUserId));
            var creditRevenue = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.PLATFORM_REVENUE);

            Assert.NotNull(debitEscrow);
            Assert.Equal(1000000L, debitEscrow.Amount);
            Assert.NotNull(creditSitter);
            Assert.Equal(900000L, creditSitter.Amount);
            Assert.NotNull(creditRevenue);
            Assert.Equal(100000L, creditRevenue.Amount);
        }

        [Fact]
        public async Task Dispute_Split_ConservesMoneyExactly_DoubleEntryMatches()
        {
            var adminUserId = ObjectId.GenerateNewId().ToString();
            var ownerId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();
            var bookingId = ObjectId.GenerateNewId().ToString();

            await _context.SitterProfiles.InsertOneAsync(new SitterProfile
            {
                SitterId = sitterId,
                UserId = sitterUserId,
                Status = "Approved"
            });

            var totalAmount = 1000000L;
            await _context.Bookings.InsertOneAsync(new Booking
            {
                BookingId = bookingId,
                MaDatLich = "PS1003",
                OwnerId = ownerId,
                SitterId = sitterId,
                SitterUserId = sitterUserId,
                TrangThai = BookingStatus.DISPUTED,
                PaymentStatus = PaymentStatus.HELD,
                TotalAmount = totalAmount,
                TongTien = totalAmount,
                CommissionBps = 1000,
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            });

            // Hoàn cho chủ nuôi 400.000 ₫. Còn lại 600.000 ₫: Hoa hồng 10% = 60.000 ₫, Sitter = 540.000 ₫.
            // Tổng: 400.000 + 540.000 + 60.000 = 1.000.000 ₫ (Bảo toàn 100%)
            var resolveDto = new ResolveDisputeDto
            {
                Resolution = "SPLIT",
                RefundToOwnerAmount = 400000L,
                Note = "Hòa giải hai bên: chủ nuôi nhận lại 400.000 ₫ do dịch vụ bị gián đoạn 1 buổi."
            };

            var result = await _bookingService.AdminResolveDisputeAsync(adminUserId, bookingId, resolveDto);

            Assert.Equal(BookingStatus.COMPLETED, result.TrangThai);
            Assert.Equal(PaymentStatus.PARTIALLY_REFUNDED, result.PaymentStatus);

            var refundReq = await _context.RefundRequests.Find(r => r.BookingId == bookingId).FirstOrDefaultAsync();
            Assert.NotNull(refundReq);
            Assert.Equal(400000L, refundReq.Amount);

            var entry = await _context.JournalEntries.Find(j => j.BookingId == bookingId).FirstOrDefaultAsync();
            Assert.NotNull(entry);
            Assert.Equal("DISPUTE_SPLIT", entry.Type);

            var debitEscrow = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.ESCROW_HELD);
            var creditRefund = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.REFUND_PENDING);
            var creditSitter = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.SitterEarnings(sitterUserId));
            var creditRevenue = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.PLATFORM_REVENUE);

            Assert.NotNull(debitEscrow);
            Assert.Equal(1000000L, debitEscrow.Amount);

            Assert.NotNull(creditRefund);
            Assert.Equal(400000L, creditRefund.Amount);

            Assert.NotNull(creditSitter);
            Assert.Equal(540000L, creditSitter.Amount);

            Assert.NotNull(creditRevenue);
            Assert.Equal(600000L - 540000L, creditRevenue.Amount);

            // Bảo toàn tuyệt đối: Nợ = Có
            Assert.Equal(debitEscrow.Amount, creditRefund.Amount + creditSitter.Amount + creditRevenue.Amount);
        }

        [Fact]
        public async Task Dispute_Split_CustomAmountMismatch_RejectsWithBadRequest()
        {
            var adminUserId = ObjectId.GenerateNewId().ToString();
            var ownerId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();
            var bookingId = ObjectId.GenerateNewId().ToString();

            await _context.SitterProfiles.InsertOneAsync(new SitterProfile
            {
                SitterId = sitterId,
                UserId = sitterUserId,
                Status = "Approved"
            });

            var totalAmount = 1000000L;
            await _context.Bookings.InsertOneAsync(new Booking
            {
                BookingId = bookingId,
                MaDatLich = "PS1004",
                OwnerId = ownerId,
                SitterId = sitterId,
                SitterUserId = sitterUserId,
                TrangThai = BookingStatus.DISPUTED,
                PaymentStatus = PaymentStatus.HELD,
                TotalAmount = totalAmount,
                TongTien = totalAmount,
                CommissionBps = 1000
            });

            // Tổng chia: 400.000 + 500.000 + 50.000 = 950.000 != 1.000.000 (Lệch 50.000 ₫)
            var resolveDto = new ResolveDisputeDto
            {
                Resolution = "SPLIT",
                RefundToOwnerAmount = 400000L,
                PayToSitterAmount = 500000L,
                PlatformFeeAmount = 50000L,
                Note = "Admin nhập số tiền phân chia thủ công nhưng bị lệch tổng tiền."
            };

            await Assert.ThrowsAsync<BadRequestException>(() =>
                _bookingService.AdminResolveDisputeAsync(adminUserId, bookingId, resolveDto));

            // Đảm bảo không có dòng sổ cái nào được ghi khi bị từ chối
            var entries = await _context.JournalEntries.Find(j => j.BookingId == bookingId).ToListAsync();
            Assert.Empty(entries);
        }

        [Fact]
        public async Task Dispute_Idempotency_RepeatExecutionDoesNotDoubleRecord()
        {
            var bookingId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var nowUtc = DateTime.UtcNow;

            var plan = new SettlementPlan
            {
                SettlementKey = LedgerIdempotencyKeys.DisputeSplit(bookingId),
                SettlementType = "DISPUTE_SPLIT",
                TotalAmount = 500000L,
                RefundAmount = 200000L,
                CommissionAmount = 30000L,
                SitterAmount = 270000L,
                SitterUserId = sitterUserId,
                ClaimedAt = nowUtc
            };

            // Lần 1: Thành công ghi sổ
            var success1 = await _bookingService.ExecuteSettlementPlanAsync(plan, bookingId, nowUtc, "ADMIN");
            Assert.True(success1);

            // Lần 2: Chạy lại cùng SettlementKey -> Idempotent trả về false, không ghi trùng
            var success2 = await _bookingService.ExecuteSettlementPlanAsync(plan, bookingId, nowUtc, "ADMIN");
            Assert.False(success2);

            var count = await _context.JournalEntries.CountDocumentsAsync(j => j.BookingId == bookingId);
            Assert.Equal(1, count);
        }

        [Fact]
        public async Task Dispute_RecordSitterViolation_UpdatesViolationCountAndHistory()
        {
            var adminUserId = ObjectId.GenerateNewId().ToString();
            var ownerId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();
            var bookingId = ObjectId.GenerateNewId().ToString();

            await _context.SitterProfiles.InsertOneAsync(new SitterProfile
            {
                SitterId = sitterId,
                UserId = sitterUserId,
                Status = "Approved",
                ViolationCount = 0
            });

            await _context.Bookings.InsertOneAsync(new Booking
            {
                BookingId = bookingId,
                MaDatLich = "PS1005",
                OwnerId = ownerId,
                SitterId = sitterId,
                SitterUserId = sitterUserId,
                TrangThai = BookingStatus.DISPUTED,
                PaymentStatus = PaymentStatus.HELD,
                TotalAmount = 500000L,
                TongTien = 500000L
            });

            var resolveDto = new ResolveDisputeDto
            {
                Resolution = BookingStatus.CANCELLED,
                Note = "Sitter vi phạm hợp đồng chăm sóc, tự ý hủy ca không báo trước.",
                RecordSitterViolation = true,
                SitterViolationReason = "Bỏ bê thú cưng và không phản hồi liên lạc."
            };

            await _bookingService.AdminResolveDisputeAsync(adminUserId, bookingId, resolveDto);

            var updatedSitter = await _context.SitterProfiles.Find(s => s.SitterId == sitterId).FirstOrDefaultAsync();
            Assert.NotNull(updatedSitter);
            Assert.Equal(1, updatedSitter.ViolationCount);
            Assert.Single(updatedSitter.Violations);
            Assert.Equal(bookingId, updatedSitter.Violations[0].BookingId);
            Assert.Equal("Bỏ bê thú cưng và không phản hồi liên lạc.", updatedSitter.Violations[0].Reason);
            Assert.Equal(adminUserId, updatedSitter.Violations[0].AdminUserId);
        }

        [Fact]
        public async Task Dispute_TwoAdminsConcurrentResolve_OnlyOneSucceeds_ViolationCountIncrementsOnce()
        {
            var admin1 = ObjectId.GenerateNewId().ToString();
            var admin2 = ObjectId.GenerateNewId().ToString();
            var ownerId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();
            var bookingId = ObjectId.GenerateNewId().ToString();

            await _context.SitterProfiles.InsertOneAsync(new SitterProfile
            {
                SitterId = sitterId,
                UserId = sitterUserId,
                Status = "Approved",
                ViolationCount = 0
            });

            await _context.Bookings.InsertOneAsync(new Booking
            {
                BookingId = bookingId,
                MaDatLich = "PS1006",
                OwnerId = ownerId,
                SitterId = sitterId,
                SitterUserId = sitterUserId,
                TrangThai = BookingStatus.DISPUTED,
                PaymentStatus = PaymentStatus.HELD,
                TotalAmount = 800000L,
                TongTien = 800000L
            });

            var dto1 = new ResolveDisputeDto
            {
                Resolution = BookingStatus.CANCELLED,
                Note = "Admin 1 xử lý: hủy đơn hoàn tiền toàn phần.",
                RecordSitterViolation = true,
                SitterViolationReason = "Lỗi vi phạm từ Admin 1."
            };

            var dto2 = new ResolveDisputeDto
            {
                Resolution = BookingStatus.COMPLETED,
                Note = "Admin 2 xử lý: hoàn tất trả tiền cho Sitter.",
                RecordSitterViolation = true,
                SitterViolationReason = "Lỗi vi phạm từ Admin 2."
            };

            // 2 Admin đồng thời bấm giải quyết tranh chấp
            var task1 = _bookingService.AdminResolveDisputeAsync(admin1, bookingId, dto1);
            var task2 = _bookingService.AdminResolveDisputeAsync(admin2, bookingId, dto2);

            int successCount = 0;
            int conflictCount = 0;

            var results = await Task.WhenAll(
                task1.ContinueWith(t => t.IsFaulted ? (object)t.Exception!.InnerException! : t.Result),
                task2.ContinueWith(t => t.IsFaulted ? (object)t.Exception!.InnerException! : t.Result)
            );

            foreach (var r in results)
            {
                if (r is BookingDetailDto) successCount++;
                if (r is ConflictException) conflictCount++;
            }

            Assert.Equal(1, successCount);
            Assert.Equal(1, conflictCount);

            // Bút toán chỉ ghi đúng 1 lần
            var entries = await _context.JournalEntries.Find(j => j.BookingId == bookingId).ToListAsync();
            Assert.Single(entries);

            // ViolationCount chỉ tăng đúng 1 lần
            var sitter = await _context.SitterProfiles.Find(s => s.SitterId == sitterId).FirstOrDefaultAsync();
            Assert.NotNull(sitter);
            Assert.Equal(1, sitter.ViolationCount);
            Assert.Single(sitter.Violations);
        }

        [Fact]
        public async Task Dispute_SplitResolution_ClientSendsHigherPlatformFee_RejectedWithBadRequestException()
        {
            var adminUserId = ObjectId.GenerateNewId().ToString();
            var ownerId = ObjectId.GenerateNewId().ToString();
            var sitterUserId = ObjectId.GenerateNewId().ToString();
            var sitterId = ObjectId.GenerateNewId().ToString();
            var bookingId = ObjectId.GenerateNewId().ToString();

            await _context.SitterProfiles.InsertOneAsync(new SitterProfile
            {
                SitterId = sitterId,
                UserId = sitterUserId,
                Status = "Approved"
            });

            var totalAmount = 1000000L;
            await _context.Bookings.InsertOneAsync(new Booking
            {
                BookingId = bookingId,
                MaDatLich = "PS1007",
                OwnerId = ownerId,
                SitterId = sitterId,
                SitterUserId = sitterUserId,
                TrangThai = BookingStatus.DISPUTED,
                PaymentStatus = PaymentStatus.HELD,
                TotalAmount = totalAmount,
                TongTien = totalAmount,
                CommissionBps = 1000 // 10%
            });

            // Refund = 400.000, còn lại cho Sitter = 600.000
            // Phí sàn đúng theo 10%: 60.000
            // Client cố tình gửi phí cao hơn: 80.000
            var dto = new ResolveDisputeDto
            {
                Resolution = "SPLIT",
                RefundToOwnerAmount = 400000L,
                PayToSitterAmount = 520000L,
                PlatformFeeAmount = 80000L, // Cao hơn tính toán của server (60.000)
                Note = "Admin cố tình nhập phí sàn cao hơn quy định."
            };

            var ex = await Assert.ThrowsAsync<BadRequestException>(() =>
                _bookingService.AdminResolveDisputeAsync(adminUserId, bookingId, dto)
            );

            Assert.Contains("Phí sàn phải tuân thủ đúng tỷ lệ quy định", ex.Message);
            Assert.Contains("không được thu thêm", ex.Message);
        }
    }
}
