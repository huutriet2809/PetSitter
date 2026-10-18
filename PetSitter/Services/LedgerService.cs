using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Models;

namespace PetSitter.Services
{
    public class LedgerService : ILedgerService
    {
        private readonly PetSitterContext _context;

        public LedgerService(PetSitterContext context)
        {
            _context = context;
        }

        public async Task<bool> HasEntryAsync(string idempotencyKey)
        {
            return await _context.JournalEntries.Find(e => e.IdempotencyKey == idempotencyKey).AnyAsync();
        }

        public async Task<JournalEntry?> RecordEntryAsync(JournalEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            // 1. Kiểm tra trước IdempotencyKey (không dựa exception trong luồng thường)
            if (await HasEntryAsync(entry.IdempotencyKey))
            {
                return null; // Đã tồn tại, bỏ qua an toàn
            }

            // 2. Validate bút toán kép (Double-entry validation)
            if (entry.Lines == null || entry.Lines.Count < 2)
            {
                throw new InvalidOperationException("Bút toán sổ cái phải chứa ít nhất 2 dòng (Nợ và Có).");
            }

            foreach (var line in entry.Lines)
            {
                if (line.Amount <= 0)
                {
                    throw new InvalidOperationException($"Số tiền trong dòng bút toán phải là số nguyên dương (> 0). Nhận: {line.Amount}");
                }
                if (line.Direction != LedgerDirection.DEBIT && line.Direction != LedgerDirection.CREDIT)
                {
                    throw new InvalidOperationException($"Chiều bút toán không hợp lệ: {line.Direction}");
                }
            }

            var totalDebit = entry.Lines.Where(l => l.Direction == LedgerDirection.DEBIT).Sum(l => l.Amount);
            var totalCredit = entry.Lines.Where(l => l.Direction == LedgerDirection.CREDIT).Sum(l => l.Amount);

            if (totalDebit != totalCredit)
            {
                throw new InvalidOperationException($"Bút toán không cân: Tổng Nợ (DEBIT = {totalDebit}) != Tổng Có (CREDIT = {totalCredit}).");
            }

            if (string.IsNullOrEmpty(entry.TransactionId))
            {
                entry.TransactionId = "TX" + DateTime.UtcNow.ToString("yyMMddHHmmss") + Guid.NewGuid().ToString("N").Substring(0, 6);
            }
            if (entry.CreatedAt == default)
            {
                entry.CreatedAt = DateTime.UtcNow;
            }

            try
            {
                await _context.JournalEntries.InsertOneAsync(entry);
                return entry;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                // Unique index collision on IdempotencyKey - idempotent return
                return null;
            }
        }

        public async Task<SitterEarningsDto> GetSitterEarningsAsync(string sitterUserId, DateTime nowUtc, int limit = 50)
        {
            var account = LedgerAccount.SitterEarnings(sitterUserId);

            // Aggregation pipeline: Match entries having the account in Lines, Unwind Lines, Match exact line
            var matchStage = new BsonDocument("$match", new BsonDocument("Lines.Account", account));
            var unwindStage = new BsonDocument("$unwind", "$Lines");
            var matchLineStage = new BsonDocument("$match", new BsonDocument("Lines.Account", account));

            var groupStage = new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "TotalCredit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.CREDIT }),
                        "$Lines.Amount",
                        0
                    }))
                },
                { "PendingHoldCredit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$and", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.CREDIT }),
                            new BsonDocument("$gt", new BsonArray { "$Lines.AvailableAt", nowUtc })
                        }),
                        "$Lines.Amount",
                        0
                    }))
                },
                { "AvailableCredit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$and", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.CREDIT }),
                            new BsonDocument("$lte", new BsonArray { "$Lines.AvailableAt", nowUtc })
                        }),
                        "$Lines.Amount",
                        0
                    }))
                },
                { "TotalDebit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.DEBIT }),
                        "$Lines.Amount",
                        0
                    }))
                }
            });

            var pipeline = new[] { matchStage, unwindStage, matchLineStage, groupStage };
            var aggResult = await _context.JournalEntries.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();

            long totalCredit = 0;
            long pendingHold = 0;
            long availableCredit = 0;
            long totalDebit = 0;

            if (aggResult != null)
            {
                totalCredit = aggResult.GetValue("TotalCredit", 0).ToInt64();
                pendingHold = aggResult.GetValue("PendingHoldCredit", 0).ToInt64();
                availableCredit = aggResult.GetValue("AvailableCredit", 0).ToInt64();
                totalDebit = aggResult.GetValue("TotalDebit", 0).ToInt64();
            }

            var availableBalance = Math.Max(0, availableCredit - totalDebit);

            // Lấy danh sách lịch sử biến động cho Sitter
            var recentEntries = await _context.JournalEntries
                .Find(e => e.Lines.Any(l => l.Account == account))
                .SortByDescending(e => e.CreatedAt)
                .Limit(limit)
                .ToListAsync();

            var history = new List<SitterLedgerItemDto>();
            foreach (var entry in recentEntries)
            {
                var line = entry.Lines.FirstOrDefault(l => l.Account == account);
                if (line == null) continue;

                var isAvailable = line.AvailableAt == null || line.AvailableAt <= nowUtc;
                history.Add(new SitterLedgerItemDto
                {
                    TransactionId = entry.TransactionId,
                    BookingId = entry.BookingId,
                    Type = entry.Type,
                    Direction = line.Direction,
                    Amount = line.Amount,
                    CreatedAt = entry.CreatedAt,
                    AvailableAt = line.AvailableAt,
                    IsAvailable = isAvailable
                });
            }

            return new SitterEarningsDto
            {
                TotalEarnings = totalCredit,
                AvailableBalance = availableBalance,
                PendingHoldBalance = pendingHold,
                History = history
            };
        }

        public async Task<AdminPaymentsOverviewDto> GetAdminPaymentsOverviewAsync(DateTime nowUtc)
        {
            // Aggregation unwind Lines across all accounts
            var unwindStage = new BsonDocument("$unwind", "$Lines");
            var groupStage = new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$Lines.Account" },
                { "TotalDebit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.DEBIT }),
                        "$Lines.Amount",
                        0
                    }))
                },
                { "TotalCredit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.CREDIT }),
                        "$Lines.Amount",
                        0
                    }))
                }
            });

            var pipeline = new[] { unwindStage, groupStage };
            var list = await _context.JournalEntries.Aggregate<BsonDocument>(pipeline).ToListAsync();

            long totalCollected = 0;
            long escrowHeldDebit = 0, escrowHeldCredit = 0;
            long platformRevenueDebit = 0, platformRevenueCredit = 0;
            long gatewayFeesDebit = 0;
            long sitterEarningsDebit = 0, sitterEarningsCredit = 0;
            long refundPendingDebit = 0, refundPendingCredit = 0;

            foreach (var doc in list)
            {
                var account = doc["_id"].AsString;
                var debit = doc.GetValue("TotalDebit", 0).ToInt64();
                var credit = doc.GetValue("TotalCredit", 0).ToInt64();

                if (account == LedgerAccount.ESCROW_HELD)
                {
                    escrowHeldDebit += debit;
                    escrowHeldCredit += credit;
                    totalCollected += credit; // Mọi khoản nạp vào Escrow
                }
                else if (account == LedgerAccount.PLATFORM_REVENUE)
                {
                    platformRevenueDebit += debit;
                    platformRevenueCredit += credit;
                }

                else if (account.StartsWith("SITTER_EARNINGS:"))
                {
                    sitterEarningsDebit += debit;
                    sitterEarningsCredit += credit;
                }
                else if (account == LedgerAccount.REFUND_PENDING)
                {
                    refundPendingDebit += debit;
                    refundPendingCredit += credit;
                }
            }

            var pendingRefundCount = (int)await _context.RefundRequests.CountDocumentsAsync(r => r.Status == RefundStatus.REFUND_PENDING);

            return new AdminPaymentsOverviewDto
            {
                TotalCollected = totalCollected,
                EscrowCurrentlyHeld = Math.Max(0, escrowHeldCredit - escrowHeldDebit),
                PlatformRevenue = Math.Max(0, platformRevenueCredit - platformRevenueDebit),
                GatewayFees = gatewayFeesDebit,
                SitterEarningsUnwithdrawn = Math.Max(0, sitterEarningsCredit - sitterEarningsDebit),
                RefundPendingAmount = Math.Max(0, refundPendingCredit - refundPendingDebit),
                RefundPendingCount = pendingRefundCount
            };
        }

        public async Task<(long TotalDebit, long TotalCredit, bool IsBalanced)> AuditLedgerBalanceAsync()
        {
            var unwindStage = new BsonDocument("$unwind", "$Lines");
            var groupStage = new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "TotalDebit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.DEBIT }),
                        "$Lines.Amount",
                        0
                    }))
                },
                { "TotalCredit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.CREDIT }),
                        "$Lines.Amount",
                        0
                    }))
                }
            });

            var pipeline = new[] { unwindStage, groupStage };
            var doc = await _context.JournalEntries.Aggregate<BsonDocument>(pipeline).FirstOrDefaultAsync();

            if (doc == null)
            {
                return (0, 0, true);
            }

            var totalDebit = doc.GetValue("TotalDebit", 0).ToInt64();
            var totalCredit = doc.GetValue("TotalCredit", 0).ToInt64();

            return (totalDebit, totalCredit, totalDebit == totalCredit);
        }

        public async Task<(bool IsValid, string Reason)> VerifyBookingSettlementInvariantAsync(string bookingId)
        {
            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null) return (false, $"Không tìm thấy đơn {bookingId}");

            var entries = await _context.JournalEntries.Find(j => j.BookingId == bookingId).ToListAsync();
            if (!entries.Any()) return (false, $"Chưa có bút toán nào cho đơn {bookingId}");

            var totalAmount = booking.TotalAmount > 0 ? booking.TotalAmount : (long)booking.TongTien;

            // 1. Số dư ESCROW_HELD của đơn = 0 (Total Credit ESCROW_HELD - Total Debit ESCROW_HELD == 0)
            var escrowCredit = entries.SelectMany(e => e.Lines)
                .Where(l => l.Account == LedgerAccounts.ESCROW_HELD && l.Direction == LedgerDirection.CREDIT)
                .Sum(l => l.Amount);
            var escrowDebit = entries.SelectMany(e => e.Lines)
                .Where(l => l.Account == LedgerAccounts.ESCROW_HELD && l.Direction == LedgerDirection.DEBIT)
                .Sum(l => l.Amount);
            var netEscrow = escrowCredit - escrowDebit;
            if (netEscrow != 0)
            {
                return (false, $"Số dư ESCROW_HELD của đơn chưa về 0: Nợ = {escrowDebit}, Có = {escrowCredit}, Số dư ròng = {netEscrow}");
            }

            // 2. Tổng ghi có (doanh thu + thu nhập Sitter + hoàn chủ nuôi) = TotalAmount
            var revenueCredit = entries.SelectMany(e => e.Lines)
                .Where(l => l.Account == LedgerAccounts.PLATFORM_REVENUE && l.Direction == LedgerDirection.CREDIT)
                .Sum(l => l.Amount);
            var sitterCredit = entries.SelectMany(e => e.Lines)
                .Where(l => l.Account.StartsWith("SITTER_EARNINGS:") && l.Direction == LedgerDirection.CREDIT)
                .Sum(l => l.Amount);
            var refundCredit = entries.SelectMany(e => e.Lines)
                .Where(l => l.Account == LedgerAccounts.REFUND_PENDING && l.Direction == LedgerDirection.CREDIT)
                .Sum(l => l.Amount);

            var totalSettledCredits = revenueCredit + sitterCredit + refundCredit;
            if (totalSettledCredits != totalAmount)
            {
                return (false, $"Tổng ghi có tất toán ({totalSettledCredits}) không khớp TotalAmount của đơn ({totalAmount}). Revenue={revenueCredit}, Sitter={sitterCredit}, Refund={refundCredit}");
            }

            return (true, "Bất biến tất toán theo đơn thỏa mãn tuyệt đối (ESCROW_HELD = 0, Tổng Có = TotalAmount).");
        }

        public async Task<(bool IsValid, long SystemEscrowHeld, long ExpectedEscrowHeld, string Reason)> VerifyGlobalEscrowInvariantAsync()
        {
            // Số dư ESCROW_HELD toàn hệ thống (CREDIT - DEBIT)
            var allEntries = await _context.JournalEntries.Find(_ => true).ToListAsync();
            var allEscrowCredit = allEntries.SelectMany(e => e.Lines)
                .Where(l => l.Account == LedgerAccounts.ESCROW_HELD && l.Direction == LedgerDirection.CREDIT)
                .Sum(l => l.Amount);
            var allEscrowDebit = allEntries.SelectMany(e => e.Lines)
                .Where(l => l.Account == LedgerAccounts.ESCROW_HELD && l.Direction == LedgerDirection.DEBIT)
                .Sum(l => l.Amount);
            var netSystemEscrow = allEscrowCredit - allEscrowDebit;

            // Tổng TotalAmount của các đơn đang ở trạng thái HELD
            var heldBookings = await _context.Bookings.Find(b => b.PaymentStatus == PaymentStatus.HELD).ToListAsync();
            var expectedEscrow = heldBookings.Sum(b => b.TotalAmount > 0 ? b.TotalAmount : (long)b.TongTien);

            if (netSystemEscrow != expectedEscrow)
            {
                return (false, netSystemEscrow, expectedEscrow, $"Bất biến toàn cục bị vi phạm! Số dư ESCROW_HELD toàn hệ thống ({netSystemEscrow:N0} ₫) != Tổng tiền các đơn đang HELD ({expectedEscrow:N0} ₫). Chênh lệch: {netSystemEscrow - expectedEscrow:N0} ₫");
            }

            return (true, netSystemEscrow, expectedEscrow, "Bất biến ESCROW_HELD toàn hệ thống thỏa mãn tuyệt đối.");
        }
    }
}
