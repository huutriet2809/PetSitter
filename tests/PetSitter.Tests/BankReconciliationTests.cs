using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using PetSitter.Models;
using PetSitter.Services;
using Xunit;

namespace PetSitter.Tests
{
    public class BankReconciliationTests : IAsyncLifetime
    {
        private MongoTestServer _server = null!;
        private PetSitterContext _context = null!;
        private BankReconciliationService _reconciliationService = null!;

        public Task InitializeAsync()
        {
            _server = new MongoTestServer();
            _context = new PetSitterContext(_server.Client.GetDatabase("Test_BankReconcile_" + Guid.NewGuid().ToString("N")));
            var parser = new BankStatementParser();
            _reconciliationService = new BankReconciliationService(_context, parser, NullLogger<BankReconciliationService>.Instance);
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            _server.Dispose();
            return Task.CompletedTask;
        }

        [Fact]
        public async Task ReconcileStatement_ClassifiesAllFourCategoriesCorrectly_AndIsReadOnly()
        {
            var nowUtc = DateTime.UtcNow;
            var adminUserId = ObjectId.GenerateNewId().ToString();

            // 1. Tạo dữ liệu trên hệ thống & sổ cái:
            // Đơn 1: MATCHED - OrderCode 100001, 1.000.000 ₫
            var bookingId1 = ObjectId.GenerateNewId().ToString();
            await _context.Payments.InsertOneAsync(new Payment
            {
                PaymentId = ObjectId.GenerateNewId().ToString(),
                OrderCode = 100001,
                BookingId = bookingId1,
                Amount = 1000000,
                Status = PaymentLinkStatus.PAID,
                CreatedAt = nowUtc
            });
            await _context.JournalEntries.InsertOneAsync(new JournalEntry
            {
                EntryId = ObjectId.GenerateNewId().ToString(),
                BookingId = bookingId1,
                Type = JournalType.PAYMENT_HELD,
                IdempotencyKey = LedgerIdempotencyKeys.PaymentHeld(bookingId1),
                CreatedAt = nowUtc,
                Lines = new List<JournalLine>
                {
                    new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 1000000 },
                    new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 1000000 }
                }
            });

            // Đơn 2: AMOUNT_MISMATCH - OrderCode 100002, Sổ cái ghi 1.200.000 ₫ (Sao kê ghi 1.000.000 ₫)
            var bookingId2 = ObjectId.GenerateNewId().ToString();
            await _context.Payments.InsertOneAsync(new Payment
            {
                PaymentId = ObjectId.GenerateNewId().ToString(),
                OrderCode = 100002,
                BookingId = bookingId2,
                Amount = 1200000,
                Status = PaymentLinkStatus.PAID,
                CreatedAt = nowUtc
            });
            await _context.JournalEntries.InsertOneAsync(new JournalEntry
            {
                EntryId = ObjectId.GenerateNewId().ToString(),
                BookingId = bookingId2,
                Type = JournalType.PAYMENT_HELD,
                IdempotencyKey = LedgerIdempotencyKeys.PaymentHeld(bookingId2),
                CreatedAt = nowUtc,
                Lines = new List<JournalLine>
                {
                    new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 1200000 },
                    new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 1200000 }
                }
            });

            // Đơn 3: MISSING_IN_BANK - OrderCode 100003, Sổ cái ghi 800.000 ₫ (Không có trong file sao kê ngân hàng)
            var bookingId3 = ObjectId.GenerateNewId().ToString();
            await _context.Payments.InsertOneAsync(new Payment
            {
                PaymentId = ObjectId.GenerateNewId().ToString(),
                OrderCode = 100003,
                BookingId = bookingId3,
                Amount = 800000,
                Status = PaymentLinkStatus.PAID,
                CreatedAt = nowUtc
            });
            await _context.JournalEntries.InsertOneAsync(new JournalEntry
            {
                EntryId = ObjectId.GenerateNewId().ToString(),
                BookingId = bookingId3,
                Type = JournalType.PAYMENT_HELD,
                IdempotencyKey = LedgerIdempotencyKeys.PaymentHeld(bookingId3),
                CreatedAt = nowUtc,
                Lines = new List<JournalLine>
                {
                    new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 800000 },
                    new JournalLine { Account = LedgerAccounts.ESCROW_HELD, Direction = LedgerDirection.CREDIT, Amount = 800000 }
                }
            });

            var initialJournalCount = await _context.JournalEntries.CountDocumentsAsync(_ => true);

            // 2. Chuẩn bị file sao kê ngân hàng (CSV):
            // - Dòng 1: Đơn 100001 (1.000.000 ₫) -> Khớp
            // - Dòng 2: Đơn 100002 (1.000.000 ₫) -> Lệch số tiền (Sổ cái 1.200.000)
            // - Dòng 3: Đơn 100099 (500.000 ₫) -> Thiếu trong sổ cái (Không có trên hệ thống)
            var csvContent = $@"NgayGiaoDich,MoTa,SoTien,MaThamChieu
{nowUtc:dd/MM/yyyy},Chuyen tien don hang 100001,1.000.000,REF100001
{nowUtc:dd/MM/yyyy},Chuyen tien don hang 100002,1.000.000,REF100002
{nowUtc:dd/MM/yyyy},Chuyen tien don hang 100099,500.000,REF100099
";

            using var memoryStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

            // 3. Thực hiện đối soát
            var result = await _reconciliationService.ReconcileStatementAsync(
                memoryStream, "SaoKe_Thang11.csv", memoryStream.Length, adminUserId);

            // 4. Kiểm tra phân loại chính xác 4 nhóm:
            Assert.Equal(1, result.MatchedCount);
            Assert.Equal(1, result.AmountMismatchCount);
            Assert.Equal(1, result.MissingInLedgerCount);
            Assert.Equal(1, result.MissingInBankCount);

            var matchedItem = result.Items.FirstOrDefault(i => i.MatchStatus == BankMatchStatus.MATCHED);
            Assert.NotNull(matchedItem);
            Assert.Equal(100001, matchedItem.OrderCode);
            Assert.Equal(1000000L, matchedItem.StatementAmount);
            Assert.Equal(1000000L, matchedItem.LedgerAmount);

            var mismatchItem = result.Items.FirstOrDefault(i => i.MatchStatus == BankMatchStatus.AMOUNT_MISMATCH);
            Assert.NotNull(mismatchItem);
            Assert.Equal(100002, mismatchItem.OrderCode);
            Assert.Equal(1000000L, mismatchItem.StatementAmount);
            Assert.Equal(1200000L, mismatchItem.LedgerAmount);

            var missingLedgerItem = result.Items.FirstOrDefault(i => i.MatchStatus == BankMatchStatus.MISSING_IN_LEDGER);
            Assert.NotNull(missingLedgerItem);
            Assert.Equal(100099, missingLedgerItem.OrderCode);
            Assert.Equal(500000L, missingLedgerItem.StatementAmount);

            var missingBankItem = result.Items.FirstOrDefault(i => i.MatchStatus == BankMatchStatus.MISSING_IN_BANK);
            Assert.NotNull(missingBankItem);
            Assert.Equal(100003, missingBankItem.OrderCode);
            Assert.Equal(800000L, missingBankItem.LedgerAmount);

            // 5. Xác nhận tính BẤT BIẾN / CHỈ ĐỌC của đối soát (Không tự ý tạo/sửa bút toán sổ cái):
            var afterJournalCount = await _context.JournalEntries.CountDocumentsAsync(_ => true);
            Assert.Equal(initialJournalCount, afterJournalCount);

            // Lịch sử đối soát đã được lưu
            var savedRun = await _context.BankReconciliationRuns.Find(r => r.RunId == result.RunId).FirstOrDefaultAsync();
            Assert.NotNull(savedRun);
            Assert.Equal(adminUserId, savedRun.AdminUserId);
        }

        [Fact]
        public async Task ReconcileStatement_MissingInBank_OnlyConsidersEntriesWithinDateRange()
        {
            var adminUserId = ObjectId.GenerateNewId().ToString();
            var baseDate = new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

            // Bút toán A: ngày 15/11/2026 (nằm trong file sao kê 15/11 - 16/11) -> MISSING_IN_BANK
            var bookingIdA = ObjectId.GenerateNewId().ToString();
            await _context.Payments.InsertOneAsync(new Payment
            {
                PaymentId = ObjectId.GenerateNewId().ToString(),
                OrderCode = 200001,
                BookingId = bookingIdA,
                Amount = 500000,
                Status = PaymentLinkStatus.PAID,
                CreatedAt = baseDate
            });
            await _context.JournalEntries.InsertOneAsync(new JournalEntry
            {
                EntryId = ObjectId.GenerateNewId().ToString(),
                BookingId = bookingIdA,
                CreatedAt = baseDate,
                Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 500000 } }
            });

            // Bút toán B: ngày 01/10/2026 (ngoài khoảng ngày của file sao kê) -> BỎ QUA, KHÔNG đưa vào MISSING_IN_BANK
            var bookingIdB = ObjectId.GenerateNewId().ToString();
            await _context.Payments.InsertOneAsync(new Payment
            {
                PaymentId = ObjectId.GenerateNewId().ToString(),
                OrderCode = 200002,
                BookingId = bookingIdB,
                Amount = 700000,
                Status = PaymentLinkStatus.PAID,
                CreatedAt = baseDate.AddDays(-45)
            });
            await _context.JournalEntries.InsertOneAsync(new JournalEntry
            {
                EntryId = ObjectId.GenerateNewId().ToString(),
                BookingId = bookingIdB,
                CreatedAt = baseDate.AddDays(-45),
                Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 700000 } }
            });

            // File sao kê chỉ chứa ngày 15/11 đến 16/11
            var csv = @"NgayGiaoDich,MoTa,SoTien,MaThamChieu
15/11/2026,Giao dich khac,100.000,REF_KHAC
16/11/2026,Giao dich khac 2,200.000,REF_KHAC_2
";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = await _reconciliationService.ReconcileStatementAsync(stream, "SaoKe_Nov.csv", stream.Length, adminUserId);

            // Bút toán A (15/11) thiếu -> MISSING_IN_BANK
            Assert.Equal(1, result.MissingInBankCount);
            var missingItem = result.Items.FirstOrDefault(i => i.MatchStatus == BankMatchStatus.MISSING_IN_BANK);
            Assert.NotNull(missingItem);
            Assert.Equal(200001, missingItem.OrderCode);

            // Bút toán B (01/10) không được đưa vào danh sách thiếu
            Assert.DoesNotContain(result.Items, i => i.OrderCode == 200002);
        }

        [Fact]
        public async Task ReconcileStatement_ExtractedFromDescription_DateTooFar_ClassifiesAsSuspicious()
        {
            var adminUserId = ObjectId.GenerateNewId().ToString();
            var ledgerDate = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

            var bookingId = ObjectId.GenerateNewId().ToString();
            await _context.Payments.InsertOneAsync(new Payment
            {
                PaymentId = ObjectId.GenerateNewId().ToString(),
                OrderCode = 300001,
                BookingId = bookingId,
                Amount = 1000000,
                Status = PaymentLinkStatus.PAID,
                CreatedAt = ledgerDate
            });
            await _context.JournalEntries.InsertOneAsync(new JournalEntry
            {
                EntryId = ObjectId.GenerateNewId().ToString(),
                BookingId = bookingId,
                CreatedAt = ledgerDate,
                Lines = new List<JournalLine> { new JournalLine { Account = LedgerAccounts.GATEWAY_CLEARING, Direction = LedgerDirection.DEBIT, Amount = 1000000 } }
            });

            // Sao kê ngân hàng: Số tiền khớp 1.000.000 ₫, mã 300001 nằm trong nội dung mô tả
            // Nhưng ngày là 20/11/2026 (lệch 19 ngày so với 01/11/2026) -> SUSPICIOUS
            var csv = @"NgayGiaoDich,MoTa,SoTien,MaThamChieu
20/11/2026,Nap tien tai khoan noi dung co so 300001 bat thuong,1.000.000,
";
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var result = await _reconciliationService.ReconcileStatementAsync(stream, "SaoKe_Suspicious.csv", stream.Length, adminUserId);

            Assert.Equal(1, result.SuspiciousCount);
            Assert.Equal(0, result.MatchedCount);

            var suspiciousItem = result.Items.FirstOrDefault(i => i.MatchStatus == BankMatchStatus.SUSPICIOUS);
            Assert.NotNull(suspiciousItem);
            Assert.Equal(300001, suspiciousItem.OrderCode);
            Assert.Contains("Nghi ngờ", suspiciousItem.DiscrepancyNote);
        }
    }
}
