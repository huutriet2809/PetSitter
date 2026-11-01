using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PetSitter.Models;

namespace PetSitter.Services
{
    public class BankReconciliationService : IBankReconciliationService
    {
        private readonly PetSitterContext _context;
        private readonly IBankStatementParser _parser;
        private readonly ILogger<BankReconciliationService> _logger;

        public BankReconciliationService(
            PetSitterContext context,
            IBankStatementParser parser,
            ILogger<BankReconciliationService> logger)
        {
            _context = context;
            _parser = parser;
            _logger = logger;
        }

        public async Task<BankReconciliationRun> ReconcileStatementAsync(Stream fileStream, string fileName, long fileSizeBytes, string adminUserId)
        {
            var nowUtc = DateTime.UtcNow;

            // 1. Phân tích tệp sao kê qua parser CSV
            var parseResult = _parser.Parse(fileStream);

            // 2. Thu thập các bút toán DEBIT GATEWAY_CLEARING từ JournalEntries
            // Chỉ đọc dữ liệu để đối chiếu và phân loại
            var clearingEntries = await _context.JournalEntries
                .Find(j => j.Lines.Any(l => l.Account == LedgerAccounts.GATEWAY_CLEARING && l.Direction == LedgerDirection.DEBIT))
                .ToListAsync();

            // Lấy danh sách Payment tương ứng để ánh xạ OrderCode <-> BookingId
            var allPayments = await _context.Payments
                .Find(p => p.OrderCode > 0)
                .ToListAsync();

            var paymentByBookingId = allPayments
                .Where(p => !string.IsNullOrEmpty(p.BookingId))
                .GroupBy(p => p.BookingId)
                .ToDictionary(g => g.Key, g => g.First());

            var paymentByOrderCode = allPayments
                .GroupBy(p => p.OrderCode)
                .ToDictionary(g => g.Key, g => g.First());

            // Xây dựng bản đồ sổ cái: OrderCode -> thông tin ghi sổ
            var ledgerItems = new Dictionary<long, (string? BookingId, long Amount, DateTime Date, string EntryId)>();

            foreach (var entry in clearingEntries)
            {
                var clearingLine = entry.Lines.FirstOrDefault(l => l.Account == LedgerAccounts.GATEWAY_CLEARING && l.Direction == LedgerDirection.DEBIT);
                if (clearingLine == null) continue;

                long? orderCode = null;

                // Thử tìm theo BookingId
                if (!string.IsNullOrEmpty(entry.BookingId) && paymentByBookingId.TryGetValue(entry.BookingId, out var pm))
                {
                    orderCode = pm.OrderCode;
                }
                // Thử trích xuất từ IdempotencyKey (ví dụ: SUSPENSE_{orderCode})
                else if (!string.IsNullOrEmpty(entry.IdempotencyKey))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(entry.IdempotencyKey, @"\b(\d{6,18})\b");
                    if (match.Success && long.TryParse(match.Groups[1].Value, out var parsedOc))
                    {
                        orderCode = parsedOc;
                    }
                }

                if (orderCode.HasValue && orderCode.Value > 0)
                {
                    ledgerItems[orderCode.Value] = (entry.BookingId, clearingLine.Amount, entry.CreatedAt, entry.EntryId);
                }
            }

            // Cũng bổ sung các Payment thành công nếu chưa có trong JournalEntries (phòng trường hợp thanh toán cũ)
            foreach (var p in allPayments)
            {
                if (p.Status == PaymentLinkStatus.PAID || p.Status == PaymentLinkStatus.ABNORMAL)
                {
                    if (!ledgerItems.ContainsKey(p.OrderCode))
                    {
                        var amt = (p.ReceivedAmount.HasValue && p.ReceivedAmount.Value > 0) ? p.ReceivedAmount.Value : p.Amount;
                        ledgerItems[p.OrderCode] = (p.BookingId, amt, p.PaidAt ?? p.CreatedAt, string.Empty);
                    }
                }
            }

            var matchedOrderCodes = new HashSet<long>();
            var items = new List<BankReconciliationItem>();

            int matchedCount = 0;
            int amountMismatchCount = 0;
            int missingInLedgerCount = 0;
            int suspiciousCount = 0;
            long totalBankAmount = 0;

            // 3. Phân loại các dòng từ Sao kê ngân hàng
            foreach (var row in parseResult.Rows)
            {
                // Bỏ qua dòng có số tiền <= 0 nếu là dòng chi / phí không phải tiền nhận
                if (row.Amount <= 0) continue;

                totalBankAmount += row.Amount;
                var item = new BankReconciliationItem
                {
                    LineNumber = row.LineNumber,
                    TransactionDate = row.TransactionDate,
                    Description = row.Description,
                    StatementAmount = row.Amount,
                    ReferenceCode = row.ReferenceCode,
                    OrderCode = row.ExtractedOrderCode
                };

                if (row.ExtractedOrderCode.HasValue && ledgerItems.TryGetValue(row.ExtractedOrderCode.Value, out var ledgerInfo))
                {
                    item.BookingId = ledgerInfo.BookingId;
                    item.LedgerAmount = ledgerInfo.Amount;

                    // Kiểm tra độ tin cậy khi trích xuất mã đơn từ nội dung chuyển khoản (Description)
                    bool isExtractedFromDescriptionOnly = !string.IsNullOrWhiteSpace(row.Description) &&
                                                          (string.IsNullOrWhiteSpace(row.ReferenceCode) ||
                                                           !row.ReferenceCode.Contains(row.ExtractedOrderCode.Value.ToString()));

                    bool isDateTooFar = false;
                    if (row.TransactionDate.HasValue && ledgerInfo.Date != default)
                    {
                        var diffDays = Math.Abs((row.TransactionDate.Value.Date - ledgerInfo.Date.Date).TotalDays);
                        if (diffDays > 3)
                        {
                            isDateTooFar = true;
                        }
                    }

                    if (row.Amount == ledgerInfo.Amount)
                    {
                        if (isExtractedFromDescriptionOnly && isDateTooFar)
                        {
                            item.MatchStatus = BankMatchStatus.SUSPICIOUS;
                            item.DiscrepancyNote = $"Nghi ngờ trùng ngẫu nhiên mã #{row.ExtractedOrderCode}: Số tiền khớp ({row.Amount:N0} ₫) nhưng ngày giao dịch ngân hàng ({row.TransactionDate:dd/MM/yyyy}) lệch quá 3 ngày so với ngày ghi sổ ({ledgerInfo.Date:dd/MM/yyyy}). Cần xác nhận thủ công.";
                            suspiciousCount++;
                        }
                        else
                        {
                            matchedOrderCodes.Add(row.ExtractedOrderCode.Value);
                            item.MatchStatus = BankMatchStatus.MATCHED;
                            item.DiscrepancyNote = "Khớp hoàn toàn giữa sao kê ngân hàng và sổ cái.";
                            matchedCount++;
                        }
                    }
                    else
                    {
                        matchedOrderCodes.Add(row.ExtractedOrderCode.Value);
                        if (isExtractedFromDescriptionOnly && isDateTooFar)
                        {
                            item.MatchStatus = BankMatchStatus.SUSPICIOUS;
                            item.DiscrepancyNote = $"Nghi ngờ mã #{row.ExtractedOrderCode} trong mô tả: Lệch cả số tiền ({row.Amount:N0} ₫ vs {ledgerInfo.Amount:N0} ₫) và ngày giao dịch lệch quá 3 ngày.";
                            suspiciousCount++;
                        }
                        else
                        {
                            item.MatchStatus = BankMatchStatus.AMOUNT_MISMATCH;
                            var diff = row.Amount - ledgerInfo.Amount;
                            item.DiscrepancyNote = $"Lệch số tiền: Sao kê ngân hàng ghi {row.Amount:N0} ₫, Sổ cái ghi {ledgerInfo.Amount:N0} ₫ (chênh lệch {diff:+0;-0;0} ₫).";
                            amountMismatchCount++;
                        }
                    }
                }
                else
                {
                    item.MatchStatus = BankMatchStatus.MISSING_IN_LEDGER;
                    item.DiscrepancyNote = "Giao dịch có trong sao kê ngân hàng nhưng KHÔNG tìm thấy trong sổ cái GATEWAY_CLEARING.";
                    missingInLedgerCount++;
                }

                items.Add(item);
            }

            // 4. Phân loại các giao dịch trong Sổ cái nhưng THIẾU trong sao kê ngân hàng (MISSING_IN_BANK)
            // CHỈ XÉT các bút toán nằm trong khoảng ngày [min, max] của file sao kê
            DateTime? minStatementDate = parseResult.Rows.Where(r => r.TransactionDate.HasValue).Select(r => r.TransactionDate!.Value).DefaultIfEmpty().Min();
            DateTime? maxStatementDate = parseResult.Rows.Where(r => r.TransactionDate.HasValue).Select(r => r.TransactionDate!.Value).DefaultIfEmpty().Max();

            int missingInBankCount = 0;
            long totalLedgerAmount = 0;

            foreach (var kvp in ledgerItems)
            {
                var oc = kvp.Key;
                var info = kvp.Value;
                totalLedgerAmount += info.Amount;

                if (!matchedOrderCodes.Contains(oc))
                {
                    // MISSING_IN_BANK CHỈ xét bút toán nằm trong khoảng ngày [min, max] của file sao kê
                    if (minStatementDate.HasValue && maxStatementDate.HasValue && minStatementDate.Value != default)
                    {
                        var minDate = minStatementDate.Value.Date;
                        var maxDate = maxStatementDate.Value.Date.AddDays(1).AddTicks(-1);
                        if (info.Date < minDate || info.Date > maxDate)
                        {
                            continue; // Bỏ qua các bút toán nằm ngoài khoảng ngày của file sao kê
                        }
                    }

                    missingInBankCount++;
                    items.Add(new BankReconciliationItem
                    {
                        LineNumber = 0,
                        TransactionDate = info.Date,
                        Description = $"Đơn hàng #{oc} trên hệ thống",
                        StatementAmount = 0,
                        ReferenceCode = oc.ToString(),
                        OrderCode = oc,
                        BookingId = info.BookingId,
                        LedgerAmount = info.Amount,
                        MatchStatus = BankMatchStatus.MISSING_IN_BANK,
                        DiscrepancyNote = $"Bút toán ghi nhận trong sổ cái ({info.Amount:N0} ₫) nhưng KHÔNG tìm thấy trên sao kê ngân hàng đợt này."
                    });
                }
            }

            // 5. Tạo và lưu bản ghi lịch sử đối soát (Chỉ lưu kết quả phân loại, KHÔNG tự sửa sổ cái)
            var run = new BankReconciliationRun
            {
                RunId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                RunAt = nowUtc,
                AdminUserId = adminUserId,
                FileName = fileName,
                FileSizeBytes = fileSizeBytes,
                TotalBankRows = parseResult.Rows.Count,
                MatchedCount = matchedCount,
                AmountMismatchCount = amountMismatchCount,
                MissingInLedgerCount = missingInLedgerCount,
                MissingInBankCount = missingInBankCount,
                SuspiciousCount = suspiciousCount,
                TotalBankAmount = totalBankAmount,
                TotalLedgerAmount = totalLedgerAmount,
                Items = items
            };

            await _context.BankReconciliationRuns.InsertOneAsync(run);

            // 6. Ghi Audit log chi tiết
            _logger.LogInformation(
                "[AUDIT] Admin {AdminUserId} hoàn thành đối soát 3 bên với tệp {FileName} ({SizeBytes:N0} bytes): Khớp={Matched}, Lệch tiền={Mismatch}, Thiếu trên sổ={MissingLedger}, Thiếu tại ngân hàng={MissingBank}, Tổng tiền sao kê={BankAmount:N0} ₫",
                adminUserId, fileName, fileSizeBytes, matchedCount, amountMismatchCount, missingInLedgerCount, missingInBankCount, totalBankAmount);

            return run;
        }

        public async Task<List<BankReconciliationRun>> GetReconciliationHistoryAsync(int limit = 20)
        {
            return await _context.BankReconciliationRuns
                .Find(_ => true)
                .SortByDescending(r => r.RunAt)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task<BankReconciliationRun?> GetReconciliationDetailsAsync(string runId)
        {
            return await _context.BankReconciliationRuns
                .Find(r => r.RunId == runId)
                .FirstOrDefaultAsync();
        }
    }
}
