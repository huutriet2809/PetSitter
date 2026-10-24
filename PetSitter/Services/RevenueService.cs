using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;

namespace PetSitter.Services
{
    public class RevenueService : IRevenueService
    {
        private readonly PetSitterContext _context;
        private readonly ILedgerService _ledgerService;
        private readonly IWithdrawalService _withdrawalService;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _config;
        private readonly ILogger<RevenueService> _logger;

        public RevenueService(
            PetSitterContext context,
            ILedgerService ledgerService,
            IWithdrawalService withdrawalService,
            INotificationService notificationService,
            IConfiguration config,
            ILogger<RevenueService> logger)
        {
            _context = context;
            _ledgerService = ledgerService;
            _withdrawalService = withdrawalService;
            _notificationService = notificationService;
            _config = config;
            _logger = logger;
        }

        private async Task<PlatformAccount> GetOrCreatePlatformAccountAsync()
        {
            var acc = await _context.PlatformAccounts.Find(p => p.AccountId == "platform_main").FirstOrDefaultAsync();
            if (acc == null)
            {
                try
                {
                    acc = new PlatformAccount
                    {
                        AccountId = "platform_main",
                        SweptTotal = 0,
                        PayosLinksCreatedCount = 0,
                        Version = 1,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await _context.PlatformAccounts.InsertOneAsync(acc);
                }
                catch (MongoWriteException)
                {
                    acc = await _context.PlatformAccounts.Find(p => p.AccountId == "platform_main").FirstOrDefaultAsync()!;
                }
            }
            return acc;
        }

        public async Task<RevenueOverviewDto> GetRevenueOverviewAsync(DateTime nowUtc)
        {
            var platformAcc = await GetOrCreatePlatformAccountAsync();

            // 1. Đọc số dư từ sổ cái
            var ledgerOverview = await _ledgerService.GetAdminPaymentsOverviewAsync(nowUtc);

            // 2. Tính số dư các tài khoản sổ cái
            var allLines = await _context.JournalEntries.Aggregate()
                .Unwind<JournalEntry, BsonDocument>(j => j.Lines)
                .Group(new BsonDocument
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
                })
                .ToListAsync();

            long totalDebitClearing = 0;
            long totalCreditClearing = 0;
            long platformRevenueCredit = 0;
            long gatewayFeesDebit = 0;
            long suspenseCredit = 0;
            long suspenseDebit = 0;
            long escrowHeldCredit = 0;
            long escrowHeldDebit = 0;
            long refundPendingCredit = 0;
            long refundPendingDebit = 0;

            foreach (var doc in allLines)
            {
                var accName = doc["_id"].AsString;
                var deb = doc["TotalDebit"].ToInt64();
                var cred = doc["TotalCredit"].ToInt64();

                if (accName == LedgerAccounts.GATEWAY_CLEARING)
                {
                    totalDebitClearing = deb;
                    totalCreditClearing = cred;
                }
                else if (accName == LedgerAccounts.PLATFORM_REVENUE)
                {
                    platformRevenueCredit = cred;
                }
                else if (accName == LedgerAccounts.GATEWAY_FEES)
                {
                    gatewayFeesDebit = deb;
                }
                else if (accName == LedgerAccounts.SUSPENSE)
                {
                    suspenseCredit = cred;
                    suspenseDebit = deb;
                }
                else if (accName == LedgerAccounts.ESCROW_HELD)
                {
                    escrowHeldCredit = cred;
                    escrowHeldDebit = deb;
                }
                else if (accName == LedgerAccounts.REFUND_PENDING)
                {
                    refundPendingCredit = cred;
                    refundPendingDebit = deb;
                }
            }

            // 3. Tính toán khoản đệm an toàn (RevenueBuffer)
            // Tính theo ClaimedAt hoặc UpdatedAt của các đơn COMPLETED trong RevenueReserveHours gần nhất
            var reserveHours = _config.GetValue<int>("Payments:RevenueReserveHours", 48);
            var reserveThreshold = nowUtc.AddHours(-reserveHours);

            var recentCompleted = await _context.Bookings
                .Find(b => b.TrangThai == BookingStatus.COMPLETED &&
                           (b.ClaimedAt >= reserveThreshold || b.UpdatedAt >= reserveThreshold))
                .ToListAsync();

            long revenueBuffer = recentCompleted.Sum(b => b.CommissionAmount);

            // 4. Doanh thu có thể chuyển (TransferableRevenue)
            // = max(0, PLATFORM_REVENUE - GATEWAY_FEES - SweptTotal - RevenueBuffer)
            long netPlatformRevenue = Math.Max(0, platformRevenueCredit - gatewayFeesDebit);
            long transferableRevenue = Math.Max(0, (netPlatformRevenue - platformAcc.SweptTotal) - revenueBuffer);

            // 5. Tính toán hạn mức PayOS
            long quota = _config.GetValue<long>("Payments:PayosPlanQuota", 100);
            long baseline = _config.GetValue<long>("Payments:PayosPlanUsedBaseline", 5);
            long totalUsed = baseline + platformAcc.PayosLinksCreatedCount;
            long remaining = Math.Max(0, quota - totalUsed);
            long warningLimit = _config.GetValue<long>("Payments:QuotaWarningRemaining", 15);

            // 6. Tổng thu nhập Sitter chưa rút
            long sitterUnwithdrawn = ledgerOverview.SitterEarningsUnwithdrawn;

            // 7. Payout Pending: Tổng tiền đang giữ trong các yêu cầu rút mở
            var openWithdrawals = await _context.WithdrawalRequests
                .Find(w => w.Status == WithdrawalStatus.REQUESTED ||
                           w.Status == WithdrawalStatus.APPROVED ||
                           w.Status == WithdrawalStatus.PROCESSING)
                .ToListAsync();
            long payoutPending = openWithdrawals.Sum(w => w.Amount);
            var recentSweeps = await GetRevenueSweepsAsync(10);

            return new RevenueOverviewDto
            {
                TotalCollected = totalDebitClearing,
                EscrowHeld = Math.Max(0, escrowHeldCredit - escrowHeldDebit),
                SitterUnwithdrawn = sitterUnwithdrawn,
                PayoutPending = payoutPending,
                RefundPending = Math.Max(0, refundPendingCredit - refundPendingDebit),
                SuspenseAmount = Math.Max(0, suspenseCredit - suspenseDebit),
                PlatformRevenue = platformRevenueCredit,
                GatewayFees = gatewayFeesDebit,
                NetPlatformRevenue = netPlatformRevenue,
                PlatformSwept = platformAcc.SweptTotal,
                RevenueBuffer = revenueBuffer,
                TransferableRevenue = transferableRevenue,
                PayosPlanQuota = quota,
                PayosLinksUsed = totalUsed,
                PayosLinksRemaining = remaining,
                IsQuotaWarning = remaining <= warningLimit,
                RecentSweeps = recentSweeps
            };
        }

        public async Task<SweepItemDto> CreateRevenueSweepAsync(string adminUserId, CreateSweepDto dto, string? ipAddress, DateTime nowUtc)
        {
            if (dto.Amount <= 0)
            {
                throw new BadRequestException("Số tiền chuyển phải lớn hơn 0.");
            }

            var bankRef = dto.BankTransactionRef.Trim();

            // 1. Kiểm tra Idempotent theo BankTransactionRef
            var existingSweep = await _context.RevenueSweeps.Find(s => s.BankTransactionRef == bankRef).FirstOrDefaultAsync();
            if (existingSweep != null)
            {
                return new SweepItemDto
                {
                    SweepId = existingSweep.SweepId,
                    Amount = existingSweep.Amount,
                    BankTransactionRef = existingSweep.BankTransactionRef,
                    Note = existingSweep.Note,
                    AdminUserId = existingSweep.AdminUserId,
                    CreatedAt = existingSweep.CreatedAt
                };
            }

            // 2. Kiểm tra điều kiện doanh thu có thể chuyển và chốt nguyên tử (thử lại tối đa 5 lần khi xung đột Version)
            PlatformAccount? updatedAccount = null;
            int maxRetries = 5;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                var overview = await GetRevenueOverviewAsync(nowUtc);
                if (dto.Amount > overview.TransferableRevenue)
                {
                    // Vượt mức doanh thu khả dụng -> dừng ngay, không retry
                    throw new BadRequestException($"Số tiền chuyển ({dto.Amount:N0} ₫) vượt quá doanh thu có thể chuyển ({overview.TransferableRevenue:N0} ₫).");
                }

                var currentAcc = await GetOrCreatePlatformAccountAsync();

                var filter = Builders<PlatformAccount>.Filter.And(
                    Builders<PlatformAccount>.Filter.Eq(p => p.AccountId, "platform_main"),
                    Builders<PlatformAccount>.Filter.Eq(p => p.Version, currentAcc.Version)
                );
                var update = Builders<PlatformAccount>.Update
                    .Inc(p => p.SweptTotal, dto.Amount)
                    .Inc(p => p.Version, 1)
                    .Set(p => p.UpdatedAt, nowUtc);

                updatedAccount = await _context.PlatformAccounts.FindOneAndUpdateAsync(
                    filter,
                    update,
                    new FindOneAndUpdateOptions<PlatformAccount> { ReturnDocument = ReturnDocument.After }
                );

                if (updatedAccount != null)
                {
                    break;
                }

                // Chỉ thử lại khi xung đột Version
                if (attempt < maxRetries)
                {
                    await Task.Delay(Random.Shared.Next(10, 20 + (attempt * 10)));
                }
            }

            if (updatedAccount == null)
            {
                throw new ConflictException("Đang có giao dịch chuyển hoa hồng khác diễn ra đồng thời. Vui lòng thử lại sau giây lát.");
            }

            // 3. Ghi bản ghi RevenueSweep
            var sweep = new RevenueSweep
            {
                SweepId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                Amount = dto.Amount,
                BankTransactionRef = bankRef,
                Note = dto.Note?.Trim(),
                AdminUserId = adminUserId,
                IpAddress = ipAddress,
                CreatedAt = nowUtc
            };
            await _context.RevenueSweeps.InsertOneAsync(sweep);

            // 4. GHI SỔ KÉP: DEBIT PLATFORM_REVENUE, CREDIT GATEWAY_CLEARING
            var journalEntry = new JournalEntry
            {
                EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "SWP",
                BookingId = "SWEEP:" + bankRef,
                Type = JournalType.REVENUE_SWEEP,
                ActorUserId = adminUserId,
                CreatedAt = nowUtc,
                IdempotencyKey = LedgerIdempotencyKeys.RevenueSweep(bankRef),
                Lines = new List<JournalLine>
                {
                    new JournalLine
                    {
                        Account = LedgerAccounts.PLATFORM_REVENUE,
                        Direction = LedgerDirection.DEBIT,
                        Amount = dto.Amount
                    },
                    new JournalLine
                    {
                        Account = LedgerAccounts.GATEWAY_CLEARING,
                        Direction = LedgerDirection.CREDIT,
                        Amount = dto.Amount
                    }
                }
            };
            await _ledgerService.RecordEntryAsync(journalEntry);

            _logger.LogInformation("Admin {AdminUserId} đã ghi nhận sweep {Amount:N0} ₫ với mã GD {BankRef}.", adminUserId, dto.Amount, bankRef);

            return new SweepItemDto
            {
                SweepId = sweep.SweepId,
                Amount = sweep.Amount,
                BankTransactionRef = sweep.BankTransactionRef,
                Note = sweep.Note,
                AdminUserId = sweep.AdminUserId,
                CreatedAt = sweep.CreatedAt
            };
        }

        public async Task<List<SweepItemDto>> GetRevenueSweepsAsync(int limit = 50)
        {
            var sweeps = await _context.RevenueSweeps
                .Find(_ => true)
                .SortByDescending(s => s.CreatedAt)
                .Limit(limit)
                .ToListAsync();

            return sweeps.Select(s => new SweepItemDto
            {
                SweepId = s.SweepId,
                Amount = s.Amount,
                BankTransactionRef = s.BankTransactionRef,
                Note = s.Note,
                AdminUserId = s.AdminUserId,
                CreatedAt = s.CreatedAt
            }).ToList();
        }

        public async Task<ReconciliationRun> RunReconciliationAsync(string adminUserId, ReconciliationRequestDto? requestDto, DateTime nowUtc)
        {
            var details = new List<string>();
            bool isWarning = false;

            // 1. Tổng Nợ = Tổng Có toàn sổ cái
            var (totalDebit, totalCredit, isBalanced) = await _ledgerService.AuditLedgerBalanceAsync();
            if (!isBalanced)
            {
                isWarning = true;
                details.Add($"[LỆCH SỔ CÁI] Tổng Nợ ({totalDebit:N0} ₫) != Tổng Có ({totalCredit:N0} ₫). Chênh lệch: {Math.Abs(totalDebit - totalCredit):N0} ₫.");
            }
            else
            {
                details.Add($"[SỔ CÁI CÂN BẰNG] Tổng Nợ = Tổng Có = {totalDebit:N0} ₫.");
            }

            // 1.5. Tự động kiểm tra và bù bút toán cho các yêu cầu rút đã PAID nhưng thiếu bút toán sổ cái
            var paidWithdrawals = await _context.WithdrawalRequests
                .Find(w => w.Status == WithdrawalStatus.PAID)
                .ToListAsync();
            int autoRepairedPaidCount = 0;
            foreach (var pw in paidWithdrawals)
            {
                var paidIdemKey = LedgerIdempotencyKeys.WithdrawalPaid(pw.ReferenceId);
                var existingEntry = await _context.JournalEntries
                    .Find(j => j.IdempotencyKey == paidIdemKey || (j.BookingId == "WITHDRAWAL:" + pw.ReferenceId && j.Type == JournalType.WITHDRAWAL_PAID))
                    .FirstOrDefaultAsync();

                if (existingEntry == null)
                {
                    var recoveryEntry = new JournalEntry
                    {
                        EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "REC",
                        BookingId = "WITHDRAWAL:" + pw.ReferenceId,
                        Type = JournalType.WITHDRAWAL_PAID,
                        ActorUserId = pw.PaidByAdminUserId ?? "system_reconcile",
                        CreatedAt = pw.PaidAt ?? nowUtc,
                        IdempotencyKey = paidIdemKey,
                        Lines = new List<JournalLine>
                        {
                            new JournalLine
                            {
                                Account = LedgerAccounts.SitterEarnings(pw.SitterUserId),
                                Direction = LedgerDirection.DEBIT,
                                Amount = pw.Amount
                            },
                            new JournalLine
                            {
                                Account = LedgerAccounts.GATEWAY_CLEARING,
                                Direction = LedgerDirection.CREDIT,
                                Amount = pw.Amount
                            }
                        }
                    };
                    await _ledgerService.RecordEntryAsync(recoveryEntry);
                    autoRepairedPaidCount++;
                    details.Add($"[TỰ SỬA BÚT TOÁN] Yêu cầu rút {pw.ReferenceId} ({pw.Amount:N0} ₫) ở trạng thái PAID nhưng thiếu bút toán sổ cái. Đã tự động tạo bù.");
                }
            }

            // 2. ReservedAmount == Tổng yêu cầu rút đang mở (Chạy hàm tự động sửa)
            var (fixedCount, fixLogs) = await _withdrawalService.ReconcileReservedAmountAsync();
            details.AddRange(fixLogs);

            var allAccounts = await _context.EarningsAccounts.Find(_ => true).ToListAsync();
            long totalReserved = allAccounts.Sum(a => a.ReservedAmount);
            var openWithdrawals = await _context.WithdrawalRequests
                .Find(w => w.Status == WithdrawalStatus.REQUESTED ||
                           w.Status == WithdrawalStatus.APPROVED ||
                           w.Status == WithdrawalStatus.PROCESSING)
                .ToListAsync();
            long totalOpen = openWithdrawals.Sum(w => w.Amount);
            bool isReservedMatches = totalReserved == totalOpen;
            if (!isReservedMatches)
            {
                isWarning = true;
                details.Add($"[LỆCH RESERVED] Tổng ReservedAmount ({totalReserved:N0} ₫) != Tổng đơn mở ({totalOpen:N0} ₫).");
            }
            else
            {
                details.Add($"[RESERVED KHỚP] Tổng ReservedAmount = Tổng đơn mở = {totalReserved:N0} ₫.");
            }

            // 3. ESCROW_HELD == Tổng TotalAmount các đơn HELD
            var (isGlobalEscrowValid, sysEscrow, expEscrow, escrowReason) = await _ledgerService.VerifyGlobalEscrowInvariantAsync();
            if (!isGlobalEscrowValid)
            {
                isWarning = true;
                details.Add($"[LỆCH ESCROW] {escrowReason}");
            }
            else
            {
                details.Add($"[ESCROW KHỚP] ESCROW_HELD = {sysEscrow:N0} ₫ trùng khớp các đơn HELD.");
            }

            // 4. Đơn COMPLETED mà PaymentStatus còn HELD
            var invalidCompleted = await _context.Bookings
                .Find(b => b.TrangThai == BookingStatus.COMPLETED && b.PaymentStatus == PaymentStatus.HELD)
                .ToListAsync();
            int invalidCompletedCount = invalidCompleted.Count;
            if (invalidCompletedCount > 0)
            {
                isWarning = true;
                details.Add($"[CẢNH BÁO ĐƠN HOÀN TẤT] Phát hiện {invalidCompletedCount} đơn COMPLETED nhưng PaymentStatus vẫn còn HELD.");
            }

            // 5. Payment PAID mà Booking không HELD/RELEASED/đã tất toán
            var invalidPayments = await _context.Payments
                .Find(p => p.Status == PaymentLinkStatus.PAID)
                .ToListAsync();
            int invalidPaymentCount = 0;
            foreach (var pay in invalidPayments)
            {
                var book = await _context.Bookings.Find(b => b.BookingId == pay.BookingId).FirstOrDefaultAsync();
                if (book != null && book.PaymentStatus == PaymentStatus.PENDING_PAYMENT)
                {
                    invalidPaymentCount++;
                }
            }
            if (invalidPaymentCount > 0)
            {
                isWarning = true;
                details.Add($"[CẢNH BÁO PAYMENT] Phát hiện {invalidPaymentCount} Payment đã PAID nhưng Booking chưa cập nhật.");
            }

            // 6. Yêu cầu rút PROCESSING quá 48h
            var staleThreshold = nowUtc.AddHours(-48);
            var staleProcessing = await _context.WithdrawalRequests
                .Find(w => w.Status == WithdrawalStatus.PROCESSING && w.UpdatedAt < staleThreshold)
                .ToListAsync();
            int staleCount = staleProcessing.Count;
            if (staleCount > 0)
            {
                isWarning = true;
                details.Add($"[RÚT TIỀN QUÁ HẠN] Phát hiện {staleCount} yêu cầu rút ở trạng thái PROCESSING quá 48 giờ chưa hoàn tất.");
            }

            // 7. So sánh số dư ngân hàng thực tế (Admin nhập) với số dư GATEWAY_CLEARING
            long? actualBankBalance = requestDto?.ActualBankBalance;
            long? expectedBankBalance = null;
            long? discrepancy = null;
            bool? isBankMatched = null;

            // Số dư tiền mặt ngân hàng theo sổ cái = Net(GATEWAY_CLEARING)
            var clearingLines = await _context.JournalEntries.Aggregate()
                .Unwind<JournalEntry, BsonDocument>(j => j.Lines)
                .Match(new BsonDocument("Lines.Account", LedgerAccounts.GATEWAY_CLEARING))
                .Group(new BsonDocument
                {
                    { "_id", "$Lines.Account" },
                    { "Debit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.DEBIT }),
                            "$Lines.Amount",
                            0
                        }))
                    },
                    { "Credit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.CREDIT }),
                            "$Lines.Amount",
                            0
                        }))
                    }
                })
                .FirstOrDefaultAsync();

            long netClearing = 0;
            if (clearingLines != null)
            {
                netClearing = clearingLines["Debit"].ToInt64() - clearingLines["Credit"].ToInt64();
            }
            expectedBankBalance = netClearing;

            if (actualBankBalance.HasValue)
            {
                discrepancy = actualBankBalance.Value - expectedBankBalance.Value;
                isBankMatched = discrepancy.Value == 0;
                if (!isBankMatched.Value)
                {
                    isWarning = true;
                    details.Add($"[LỆCH NGÂN HÀNG THỰC TẾ] Số dư thực tế ({actualBankBalance.Value:N0} ₫) != Số dư sổ cái ({expectedBankBalance.Value:N0} ₫). Lệch: {discrepancy.Value:N0} ₫.");
                }
                else
                {
                    details.Add($"[NGÂN HÀNG KHỚP 100%] Số dư thực tế ({actualBankBalance.Value:N0} ₫) khớp hoàn toàn với số dư sổ cái.");
                }
            }

            // 8. Kiểm tra Đẳng thức đối soát toàn thể sổ cái:
            // GATEWAY_CLEARING (Net Debit) == ESCROW_HELD + SitterUnwithdrawn + REFUND_PENDING + SUSPENSE + (PLATFORM_REVENUE - SweptTotal)
            var allLinesForIdentity = await _context.JournalEntries.Aggregate()
                .Unwind<JournalEntry, BsonDocument>(j => j.Lines)
                .Group(new BsonDocument
                {
                    { "_id", "$Lines.Account" },
                    { "Debit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.DEBIT }),
                            "$Lines.Amount",
                            0
                        }))
                    },
                    { "Credit", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                        {
                            new BsonDocument("$eq", new BsonArray { "$Lines.Direction", LedgerDirection.CREDIT }),
                            "$Lines.Amount",
                            0
                        }))
                    }
                })
                .ToListAsync();

            long netClearingBal = 0;
            long netEscrowBal = 0;
            long netSitterBal = 0;
            long netRefundBal = 0;
            long netSuspenseBal = 0;
            long netPlatformBal = 0;
            long netGatewayFeesBal = 0;

            foreach (var doc in allLinesForIdentity)
            {
                var acc = doc["_id"].AsString;
                var deb = doc["Debit"].ToInt64();
                var cred = doc["Credit"].ToInt64();

                if (acc == LedgerAccounts.GATEWAY_CLEARING)
                {
                    netClearingBal = deb - cred; // Normal DEBIT
                }
                else if (acc == LedgerAccounts.ESCROW_HELD)
                {
                    netEscrowBal += (cred - deb); // Normal CREDIT
                }
                else if (acc.StartsWith("SITTER_EARNINGS:"))
                {
                    netSitterBal += (cred - deb); // Normal CREDIT
                }
                else if (acc == LedgerAccounts.REFUND_PENDING)
                {
                    netRefundBal += (cred - deb); // Normal CREDIT
                }
                else if (acc == LedgerAccounts.SUSPENSE)
                {
                    netSuspenseBal += (cred - deb); // Normal CREDIT
                }
                else if (acc == LedgerAccounts.PLATFORM_REVENUE)
                {
                    netPlatformBal += (cred - deb); // Normal CREDIT (đã trừ Debit khi Sweep)
                }
                else if (acc == LedgerAccounts.GATEWAY_FEES)
                {
                    netGatewayFeesBal += (deb - cred); // Normal DEBIT (chi phí cổng thanh toán)
                }
            }

            long unwithdrawnPlatformRevenue = netPlatformBal - netGatewayFeesBal;
            long expectedClearingSum = netEscrowBal + netSitterBal + netRefundBal + netSuspenseBal + unwithdrawnPlatformRevenue;
            bool isIdentityMatched = netClearingBal == expectedClearingSum;
            if (isIdentityMatched)
            {
                details.Add($"[ĐẲNG THỨC ĐỐI SOÁT KHỚP 100%] GATEWAY_CLEARING ({netClearingBal:N0} ₫) == ESCROW_HELD ({netEscrowBal:N0} ₫) + Sitter chưa rút ({netSitterBal:N0} ₫) + REFUND_PENDING ({netRefundBal:N0} ₫) + SUSPENSE ({netSuspenseBal:N0} ₫) + Doanh thu sàn chưa chuyển ({unwithdrawnPlatformRevenue:N0} ₫).");
            }
            else
            {
                isWarning = true;
                details.Add($"[LỆCH ĐẲNG THỨC ĐỐI SOÁT] GATEWAY_CLEARING ({netClearingBal:N0} ₫) != Vế phải ({expectedClearingSum:N0} ₫). Chênh lệch: {Math.Abs(netClearingBal - expectedClearingSum):N0} ₫.");
            }

            var run = new ReconciliationRun
            {
                RunId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                RunAt = nowUtc,
                AdminUserId = adminUserId,
                IsBalancedTotalDebitCredit = isBalanced,
                TotalDebit = totalDebit,
                TotalCredit = totalCredit,
                IsReservedAmountMatchesOpenWithdrawals = isReservedMatches,
                TotalReservedAmount = totalReserved,
                TotalOpenWithdrawals = totalOpen,
                AutoFixedAccountsCount = fixedCount,
                IsEscrowHeldMatchesHeldBookings = isGlobalEscrowValid,
                EscrowHeldBalance = sysEscrow,
                HeldBookingsTotal = expEscrow,
                HasCompletedBookingsWithHeldStatus = invalidCompletedCount > 0,
                CompletedBookingsWithHeldStatusCount = invalidCompletedCount,
                HasPaidPaymentsWithoutProperStatus = invalidPaymentCount > 0,
                PaidPaymentsWithoutProperStatusCount = invalidPaymentCount,
                HasStaleProcessingWithdrawals = staleCount > 0,
                StaleProcessingWithdrawalsCount = staleCount,
                ActualBankBalance = actualBankBalance,
                ExpectedBankBalance = expectedBankBalance,
                BankBalanceDiscrepancy = discrepancy,
                IsBankBalanceMatched = isBankMatched,
                Status = isWarning ? "WARNING" : "SUCCESS",
                Details = details
            };

            await _context.ReconciliationRuns.InsertOneAsync(run);

            // Nếu phát hiện cảnh báo hoặc lệch số dư, gửi thông báo khẩn tới toàn bộ Admin
            if (isWarning)
            {
                try
                {
                    var admins = await _context.Users.Find(u => u.Role == "ADMIN" || u.Role == "Admin").ToListAsync();
                    foreach (var adm in admins)
                    {
                        await _notificationService.CreateNotificationAsync(
                            adm.UserId,
                            NotificationType.RECONCILIATION_ALERT,
                            run.RunId,
                            "Cảnh báo lệch đối soát hệ thống",
                            $"Lần chạy đối soát lúc {nowUtc:HH:mm dd/MM/yyyy} phát hiện sai lệch. Vui lòng kiểm tra báo cáo chi tiết.",
                            $"{run.RunId}:ALERT:{adm.UserId}",
                            targetRole: "ADMIN"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi thông báo cảnh báo đối soát tới Admin.");
                }
            }

            return run;
        }

        public async Task<List<ReconciliationRun>> GetReconciliationHistoryAsync(int limit = 30)
        {
            return await _context.ReconciliationRuns
                .Find(_ => true)
                .SortByDescending(r => r.RunAt)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task<MonthlyReportDto> GetMonthlyReportAsync(int year, int month, DateTime nowUtc)
        {
            var startUtc = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var endUtc = startUtc.AddMonths(1);

            // Lấy danh sách bút toán trong kỳ
            var entries = await _context.JournalEntries
                .Find(j => j.CreatedAt >= startUtc && j.CreatedAt < endUtc)
                .SortByDescending(j => j.CreatedAt)
                .ToListAsync();

            // Lấy danh sách sweeps trong kỳ để map ghi chú
            var sweeps = await _context.RevenueSweeps
                .Find(s => s.CreatedAt >= startUtc && s.CreatedAt < endUtc)
                .ToListAsync();
            var sweepNotes = sweeps.ToDictionary(s => s.BankTransactionRef, s => s.Note);

            long grossCommission = 0;
            long gatewayFees = 0;
            long totalSwept = 0;

            var items = new List<MonthlyTransactionItemDto>();

            foreach (var entry in entries)
            {
                long comm = 0;
                long fee = 0;

                var revLine = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.PLATFORM_REVENUE && l.Direction == LedgerDirection.CREDIT);
                if (revLine != null)
                {
                    comm += revLine.Amount;
                    grossCommission += revLine.Amount;
                }

                var feeLine = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.GATEWAY_FEES && l.Direction == LedgerDirection.DEBIT);
                if (feeLine != null)
                {
                    fee += feeLine.Amount;
                    gatewayFees += feeLine.Amount;
                }

                var swpLine = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.PLATFORM_REVENUE && l.Direction == LedgerDirection.DEBIT && entry.Type == JournalType.REVENUE_SWEEP);
                if (swpLine != null)
                {
                    totalSwept += swpLine.Amount;
                }

                string? note = null;
                if (entry.Type == JournalType.REVENUE_SWEEP && entry.BookingId != null && entry.BookingId.StartsWith("SWEEP:"))
                {
                    var refKey = entry.BookingId.Substring("SWEEP:".Length);
                    sweepNotes.TryGetValue(refKey, out note);
                }

                var firstLine = entry.Lines.FirstOrDefault();
                items.Add(new MonthlyTransactionItemDto
                {
                    TransactionId = entry.TransactionId,
                    OrderCode = entry.BookingId,
                    BookingId = entry.BookingId,
                    Type = entry.Type,
                    Amount = firstLine?.Amount ?? 0,
                    Fee = fee,
                    Net = comm - fee,
                    Status = "COMPLETED",
                    Note = note,
                    CreatedAt = entry.CreatedAt
                });
            }

            long netRevenue = Math.Max(0, grossCommission - gatewayFees);
            long retained = Math.Max(0, netRevenue - totalSwept);

            return new MonthlyReportDto
            {
                Year = year,
                Month = month,
                GrossCommission = grossCommission,
                GatewayFees = gatewayFees,
                NetRevenue = netRevenue,
                TotalSwept = totalSwept,
                RetainedRevenue = retained,
                Transactions = items
            };
        }

        public async Task<string> ExportMonthlyReportCsvAsync(int year, int month, DateTime nowUtc)
        {
            var report = await GetMonthlyReportAsync(year, month, nowUtc);

            var sb = new StringBuilder();
            // Header
            sb.AppendLine("TransactionId,OrderCode,BookingId,Type,Amount,Fee,Net,Status,Note,CreatedAt");

            foreach (var item in report.Transactions)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    EscapeCsvCell(item.TransactionId),
                    EscapeCsvCell(item.OrderCode),
                    EscapeCsvCell(item.BookingId),
                    EscapeCsvCell(item.Type),
                    item.Amount.ToString(),
                    item.Fee.ToString(),
                    item.Net.ToString(),
                    EscapeCsvCell(item.Status),
                    EscapeCsvCell(item.Note ?? ""),
                    EscapeCsvCell(item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"))
                }));
            }

            return sb.ToString();
        }

        private static string EscapeCsvCell(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            var cell = value;

            // Xử lý CR và Tab
            cell = cell.Replace("\r\n", " ").Replace("\r", " ").Replace("\t", " ");

            // Nếu là số thuần túy (kể cả số âm như -50000 hoặc số thập phân), KHÔNG chèn dấu '
            bool isNumeric = decimal.TryParse(cell, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out _);

            // Chống CSV Injection: CHỈ chèn tiền tố ' cho cột chữ nếu bắt đầu bằng =, +, -, @
            if (!isNumeric && (cell.StartsWith("=") || cell.StartsWith("+") || cell.StartsWith("-") || cell.StartsWith("@")))
            {
                cell = "'" + cell;
            }

            cell = cell.Replace("\"", "\"\"");
            return $"\"{cell}\"";
        }
    }
}
