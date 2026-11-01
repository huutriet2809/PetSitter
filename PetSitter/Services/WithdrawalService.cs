using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Models;

namespace PetSitter.Services
{
    public class WithdrawalService : IWithdrawalService
    {
        private readonly PetSitterContext _context;
        private readonly ILedgerService _ledgerService;
        private readonly IPaymentGateway _gateway;
        private readonly INotificationService _notificationService;
        private readonly IDataProtector _protector;
        private readonly IConfiguration _config;
        private readonly ILogger<WithdrawalService> _logger;

        public WithdrawalService(
            PetSitterContext context,
            ILedgerService ledgerService,
            IPaymentGateway gateway,
            INotificationService notificationService,
            IDataProtectionProvider protectionProvider,
            IConfiguration config,
            ILogger<WithdrawalService> logger)
        {
            _context = context;
            _ledgerService = ledgerService;
            _gateway = gateway;
            _notificationService = notificationService;
            _protector = protectionProvider.CreateProtector("PetSitter.SitterBank.v1");
            _config = config;
            _logger = logger;
        }

        private static TimeZoneInfo GetVnTimeZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch
            {
                return TimeZoneInfo.CreateCustomTimeZone("VN_TZ", TimeSpan.FromHours(7), "Vietnam Standard Time", "Vietnam Standard Time");
            }
        }

        private static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var text = name.Trim().ToUpperInvariant();
            var regex = new Regex("\\p{IsCombiningDiacriticalMarks}+");
            var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
            var clean = regex.Replace(normalized, string.Empty)
                             .Replace('Đ', 'D')
                             .Replace('đ', 'd');
            return Regex.Replace(clean, @"\s+", " ").Trim();
        }

        public async Task<SitterBankAccountDto> GetSitterBankAccountAsync(string sitterUserId, DateTime nowUtc)
        {
            var profile = await _context.SitterProfiles.Find(p => p.UserId == sitterUserId).FirstOrDefaultAsync();
            if (profile?.BankAccount == null)
            {
                return new SitterBankAccountDto
                {
                    CanWithdraw = false
                };
            }

            var bank = profile.BankAccount;
            var cooldownHours = _config.GetValue<int>("Payments:BankChangeCooldownHours", 24);
            var cooldownUntil = bank.UpdatedAt.AddHours(cooldownHours);
            var canWithdraw = nowUtc >= cooldownUntil;

            return new SitterBankAccountDto
            {
                BankBin = bank.BankBin,
                BankName = bank.BankName,
                MaskedAccountNumber = bank.MaskedAccountNumber,
                AccountHolderName = bank.AccountHolderName,
                NameMatchesRegisteredName = bank.NameMatchesRegisteredName,
                UpdatedAt = bank.UpdatedAt,
                CanWithdraw = canWithdraw,
                CooldownUntil = canWithdraw ? null : cooldownUntil
            };
        }

        public async Task<SitterBankAccountDto> UpdateSitterBankAccountAsync(string sitterUserId, UpdateBankAccountDto dto, DateTime nowUtc)
        {
            var user = await _context.Users.Find(u => u.UserId == sitterUserId).FirstOrDefaultAsync();
            if (user == null)
            {
                throw new NotFoundException("Người dùng không tồn tại.");
            }

            // 1. Xác thực mật khẩu
            if (string.IsNullOrWhiteSpace(dto.Password) || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                throw new BadRequestException("Mật khẩu xác nhận không chính xác. Vui lòng thử lại.");
            }

            var profile = await _context.SitterProfiles.Find(p => p.UserId == sitterUserId).FirstOrDefaultAsync();
            if (profile == null)
            {
                throw new NotFoundException("Hồ sơ Sitter không tồn tại.");
            }

            // 2. Mã hóa số tài khoản
            var rawAcc = dto.AccountNumber.Trim();
            var encryptedAcc = _protector.Protect(rawAcc);
            var last4 = rawAcc.Length >= 4 ? rawAcc.Substring(rawAcc.Length - 4) : rawAcc;
            var maskedAcc = $"****{last4}";

            // 3. Kiểm tra so khớp tên chủ tài khoản với họ tên đăng ký
            var normHolder = NormalizeName(dto.AccountHolderName);
            var normUser = NormalizeName(user.FullName);
            var nameMatches = normHolder == normUser;

            var bankAccount = new SitterBankAccount
            {
                BankBin = dto.BankBin.Trim(),
                BankName = dto.BankName.Trim(),
                EncryptedAccountNumber = encryptedAcc,
                MaskedAccountNumber = maskedAcc,
                AccountHolderName = dto.AccountHolderName.Trim().ToUpperInvariant(),
                NameMatchesRegisteredName = nameMatches,
                UpdatedAt = nowUtc
            };

            await _context.SitterProfiles.UpdateOneAsync(
                p => p.UserId == sitterUserId,
                Builders<SitterProfile>.Update.Set(p => p.BankAccount, bankAccount)
            );

            // 4. Gửi thông báo cho Sitter
            try
            {
                await _notificationService.CreateNotificationAsync(
                    sitterUserId,
                    NotificationType.BANK_ACCOUNT_UPDATED,
                    profile.SitterId,
                    "Cập nhật tài khoản ngân hàng",
                    $"Tài khoản ngân hàng của bạn đã được cập nhật sang {bankAccount.BankName} ({maskedAcc}). Vì lý do an toàn, yêu cầu rút tiền sẽ được mở lại sau 24 giờ.",
                    $"{sitterUserId}:BANK_UPDATED:{nowUtc.Ticks}",
                    targetRole: "SITTER"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi thông báo đổi tài khoản ngân hàng cho {UserId}", sitterUserId);
            }

            var cooldownHours = _config.GetValue<int>("Payments:BankChangeCooldownHours", 24);
            return new SitterBankAccountDto
            {
                BankBin = bankAccount.BankBin,
                BankName = bankAccount.BankName,
                MaskedAccountNumber = bankAccount.MaskedAccountNumber,
                AccountHolderName = bankAccount.AccountHolderName,
                NameMatchesRegisteredName = bankAccount.NameMatchesRegisteredName,
                UpdatedAt = bankAccount.UpdatedAt,
                CanWithdraw = false,
                CooldownUntil = nowUtc.AddHours(cooldownHours)
            };
        }

        public async Task<WithdrawalItemDto> CreateWithdrawalRequestAsync(string sitterUserId, CreateWithdrawalDto dto, string idempotencyKey, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length < 3 || idempotencyKey.Length > 120)
            {
                throw new BadRequestException("Header Idempotency-Key là bắt buộc (từ 3 đến 120 ký tự).");
            }

            // 1. Kiểm tra lặp Idempotency-Key
            var existingReq = await _context.WithdrawalRequests
                .Find(w => w.IdempotencyKey == idempotencyKey)
                .FirstOrDefaultAsync();
            if (existingReq != null)
            {
                if (existingReq.SitterUserId != sitterUserId)
                {
                    throw new ConflictException("Idempotency-Key đã được sử dụng bởi yêu cầu khác.");
                }
                return MapToDto(existingReq);
            }

            // 2. Kiểm tra thông tin ngân hàng Sitter & Cooldown
            var profile = await _context.SitterProfiles.Find(p => p.UserId == sitterUserId).FirstOrDefaultAsync();
            if (profile?.BankAccount == null)
            {
                throw new BadRequestException("Vui lòng thiết lập tài khoản ngân hàng nhận tiền trước khi rút.");
            }

            var bank = profile.BankAccount;
            var cooldownHours = _config.GetValue<int>("Payments:BankChangeCooldownHours", 24);
            var cooldownUntil = bank.UpdatedAt.AddHours(cooldownHours);
            if (nowUtc < cooldownUntil)
            {
                throw new BadRequestException($"Tài khoản ngân hàng vừa được thay đổi. Vì lý do an toàn, vui lòng đợi đến {cooldownUntil:HH:mm dd/MM/yyyy} trước khi gửi yêu cầu rút tiền.");
            }

            // 3. Kiểm tra hạn mức rút
            var withdrawMin = _config.GetValue<long>("Payments:WithdrawMin", 50000);
            var dailyLimit = _config.GetValue<long>("Payments:WithdrawDailyLimit", 20000000);
            if (dto.Amount < withdrawMin)
            {
                throw new BadRequestException($"Số tiền rút tối thiểu là {withdrawMin:N0} ₫.");
            }

            // 4. Đọc thu nhập khả dụng từ sổ cái (nguồn sự thật duy nhất)
            var earningsDto = await _ledgerService.GetSitterEarningsAsync(sitterUserId, nowUtc);
            var grossAvailable = earningsDto.AvailableBalance; // Tổng ghi CÓ đã qua AvailableAt - tổng đã PAID theo sổ cái

            // 5. Cửa sổ ngày theo giờ Việt Nam (UTC+7)
            var vnNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, GetVnTimeZone());
            var currentVnDateStr = vnNow.ToString("yyyy-MM-dd");

            // 6. Cổng kiểm soát nguyên tử (Atomic Gate) trên EarningsAccounts (thử lại tối đa 5 lần khi xung đột Version)
            EarningsAccount? claimedAccount = null;
            int maxRetries = 5;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                // Đọc hoặc khởi tạo document EarningsAccounts cho Sitter
                var account = await _context.EarningsAccounts.Find(a => a.UserId == sitterUserId).FirstOrDefaultAsync();
                if (account == null)
                {
                    try
                    {
                        account = new EarningsAccount
                        {
                            AccountId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                            UserId = sitterUserId,
                            ReservedAmount = 0,
                            DailyWithdrawn = 0,
                            DailyWindowDate = currentVnDateStr,
                            Version = 1,
                            UpdatedAt = nowUtc
                        };
                        await _context.EarningsAccounts.InsertOneAsync(account);
                    }
                    catch (MongoWriteException)
                    {
                        // Luồng khác đã insert đồng thời -> đọc lại
                        account = await _context.EarningsAccounts.Find(a => a.UserId == sitterUserId).FirstOrDefaultAsync();
                    }
                }

                if (account == null) continue;

                // Kiểm tra hạn mức ngày (nếu qua ngày VN mới thì coi như đã reset)
                long effectiveDaily = account.DailyWindowDate == currentVnDateStr ? account.DailyWithdrawn : 0;
                if (effectiveDaily + dto.Amount > dailyLimit)
                {
                    // Vượt hạn mức ngày -> dừng ngay, không retry
                    throw new BadRequestException($"Số tiền yêu cầu vượt quá hạn mức rút trong ngày ({dailyLimit:N0} ₫). Đã rút hôm nay: {effectiveDaily:N0} ₫.");
                }

                // Kiểm tra số dư khả dụng thực tế
                long netAvailable = grossAvailable - account.ReservedAmount;
                if (dto.Amount > netAvailable)
                {
                    // Thiếu số dư -> dừng ngay, không retry
                    throw new BadRequestException($"Số tiền yêu cầu ({dto.Amount:N0} ₫) vượt quá thu nhập khả dụng hiện có ({netAvailable:N0} ₫).");
                }

                // Thực hiện FindOneAndUpdate nguyên tử
                FilterDefinition<EarningsAccount> filter;
                UpdateDefinition<EarningsAccount> update;

                if (account.DailyWindowDate != currentVnDateStr)
                {
                    filter = Builders<EarningsAccount>.Filter.And(
                        Builders<EarningsAccount>.Filter.Eq(a => a.UserId, sitterUserId),
                        Builders<EarningsAccount>.Filter.Eq(a => a.Version, account.Version),
                        Builders<EarningsAccount>.Filter.Lte(a => a.ReservedAmount, grossAvailable - dto.Amount)
                    );
                    update = Builders<EarningsAccount>.Update
                        .Inc(a => a.ReservedAmount, dto.Amount)
                        .Inc(a => a.Version, 1)
                        .Set(a => a.DailyWindowDate, currentVnDateStr)
                        .Set(a => a.DailyWithdrawn, dto.Amount)
                        .Set(a => a.UpdatedAt, nowUtc);
                }
                else
                {
                    filter = Builders<EarningsAccount>.Filter.And(
                        Builders<EarningsAccount>.Filter.Eq(a => a.UserId, sitterUserId),
                        Builders<EarningsAccount>.Filter.Eq(a => a.Version, account.Version),
                        Builders<EarningsAccount>.Filter.Lte(a => a.ReservedAmount, grossAvailable - dto.Amount),
                        Builders<EarningsAccount>.Filter.Lte(a => a.DailyWithdrawn, dailyLimit - dto.Amount)
                    );
                    update = Builders<EarningsAccount>.Update
                        .Inc(a => a.ReservedAmount, dto.Amount)
                        .Inc(a => a.DailyWithdrawn, dto.Amount)
                        .Inc(a => a.Version, 1)
                        .Set(a => a.UpdatedAt, nowUtc);
                }

                claimedAccount = await _context.EarningsAccounts.FindOneAndUpdateAsync(
                    filter,
                    update,
                    new FindOneAndUpdateOptions<EarningsAccount> { ReturnDocument = ReturnDocument.After }
                );

                if (claimedAccount != null)
                {
                    break; // Thành công vượt qua gate
                }

                // Chỉ thử lại khi xung đột Version (FindOneAndUpdate trả về null)
                if (attempt < maxRetries)
                {
                    await Task.Delay(Random.Shared.Next(10, 20 + (attempt * 10)));
                }
            }

            if (claimedAccount == null)
            {
                throw new ConflictException("Hệ thống đang xử lý giao dịch đồng thời trên tài khoản. Vui lòng thử lại sau giây lát.");
            }

            // 7. Tạo bản ghi WithdrawalRequest
            var referenceId = "WTH" + nowUtc.ToString("yyMMddHHmmss") + Guid.NewGuid().ToString("N").Substring(0, 6).ToUpperInvariant();
            var bankSnapshotJson = JsonSerializer.Serialize(new
            {
                bank.BankBin,
                bank.BankName,
                AccountNumber = _protector.Unprotect(bank.EncryptedAccountNumber),
                bank.AccountHolderName
            });
            var encryptedSnapshot = _protector.Protect(bankSnapshotJson);

            var req = new WithdrawalRequest
            {
                WithdrawalId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                SitterUserId = sitterUserId,
                Amount = dto.Amount,
                EncryptedBankSnapshot = encryptedSnapshot,
                MaskedAccountNumber = bank.MaskedAccountNumber,
                BankBin = bank.BankBin,
                BankName = bank.BankName,
                AccountHolderName = bank.AccountHolderName,
                NameMatchesRegisteredName = bank.NameMatchesRegisteredName,
                Status = WithdrawalStatus.REQUESTED,
                ReferenceId = referenceId,
                IdempotencyKey = idempotencyKey,
                SettlementKey = $"{referenceId}:PAID",
                CreatedAt = nowUtc,
                UpdatedAt = nowUtc
            };

            try
            {
                await _context.WithdrawalRequests.InsertOneAsync(req);
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                _logger.LogWarning(ex, "Trùng lặp khóa WithdrawalRequest {ReferenceId}, rollback ReservedAmount.", referenceId);
                await _context.EarningsAccounts.UpdateOneAsync(
                    a => a.UserId == sitterUserId,
                    Builders<EarningsAccount>.Update
                        .Inc(a => a.ReservedAmount, -dto.Amount)
                        .Inc(a => a.DailyWithdrawn, -dto.Amount)
                        .Inc(a => a.Version, 1)
                );
                throw new ConflictException("Yêu cầu rút tiền bị trùng lặp hoặc xung đột đồng thời. Vui lòng thử lại.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi tạo WithdrawalRequest {ReferenceId}, rollback ReservedAmount.", referenceId);
                // Rollback gate nếu gặp lỗi lưu dữ liệu
                await _context.EarningsAccounts.UpdateOneAsync(
                    a => a.UserId == sitterUserId,
                    Builders<EarningsAccount>.Update
                        .Inc(a => a.ReservedAmount, -dto.Amount)
                        .Inc(a => a.DailyWithdrawn, -dto.Amount)
                        .Inc(a => a.Version, 1)
                );
                throw;
            }

            return MapToDto(req);
        }

        public async Task<List<WithdrawalItemDto>> GetMyWithdrawalsAsync(string sitterUserId, int limit = 50)
        {
            var list = await _context.WithdrawalRequests
                .Find(w => w.SitterUserId == sitterUserId)
                .SortByDescending(w => w.CreatedAt)
                .Limit(limit)
                .ToListAsync();

            return list.Select(MapToDto).ToList();
        }

        public async Task<WithdrawalItemDto> CancelWithdrawalRequestAsync(string sitterUserId, string withdrawalId, DateTime nowUtc)
        {
            var filter = Builders<WithdrawalRequest>.Filter.And(
                Builders<WithdrawalRequest>.Filter.Eq(w => w.WithdrawalId, withdrawalId),
                Builders<WithdrawalRequest>.Filter.Eq(w => w.SitterUserId, sitterUserId),
                Builders<WithdrawalRequest>.Filter.Eq(w => w.Status, WithdrawalStatus.REQUESTED)
            );
            var update = Builders<WithdrawalRequest>.Update
                .Set(w => w.Status, WithdrawalStatus.CANCELLED)
                .Set(w => w.CancelledAt, nowUtc)
                .Set(w => w.UpdatedAt, nowUtc);

            var cancelled = await _context.WithdrawalRequests.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<WithdrawalRequest> { ReturnDocument = ReturnDocument.After }
            );

            if (cancelled == null)
            {
                var existing = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
                if (existing == null || existing.SitterUserId != sitterUserId)
                {
                    throw new NotFoundException("Yêu cầu rút tiền không tồn tại.");
                }
                throw new BadRequestException($"Không thể hủy yêu cầu ở trạng thái '{existing.Status}'. Chỉ có thể hủy khi đang chờ duyệt (REQUESTED).");
            }

            // Nhả ReservedAmount và hoàn lại DailyWithdrawn nếu trong cùng cửa sổ ngày VN
            var reqVnDate = TimeZoneInfo.ConvertTimeFromUtc(cancelled.CreatedAt, GetVnTimeZone()).ToString("yyyy-MM-dd");
            var account = await _context.EarningsAccounts.Find(a => a.UserId == sitterUserId).FirstOrDefaultAsync();

            var updateBuilder = Builders<EarningsAccount>.Update
                .Inc(a => a.ReservedAmount, -cancelled.Amount)
                .Inc(a => a.Version, 1)
                .Set(a => a.UpdatedAt, nowUtc);

            if (account != null && account.DailyWindowDate == reqVnDate)
            {
                var refundDaily = Math.Min(account.DailyWithdrawn, cancelled.Amount);
                if (refundDaily > 0)
                {
                    updateBuilder = updateBuilder.Inc(a => a.DailyWithdrawn, -refundDaily);
                }
            }

            await _context.EarningsAccounts.UpdateOneAsync(
                a => a.UserId == sitterUserId,
                updateBuilder
            );

            return MapToDto(cancelled);
        }

        public async Task<List<WithdrawalItemDto>> GetAdminWithdrawalsAsync(string? status = null, string? sitterUserId = null, int limit = 50)
        {
            var builder = Builders<WithdrawalRequest>.Filter;
            var filter = builder.Empty;

            if (!string.IsNullOrWhiteSpace(status))
            {
                filter &= builder.Eq(w => w.Status, status);
            }
            if (!string.IsNullOrWhiteSpace(sitterUserId))
            {
                filter &= builder.Eq(w => w.SitterUserId, sitterUserId);
            }

            var list = await _context.WithdrawalRequests
                .Find(filter)
                .SortByDescending(w => w.CreatedAt)
                .Limit(limit)
                .ToListAsync();

            return list.Select(MapToDto).ToList();
        }

        public async Task<WithdrawalItemDto> ApproveWithdrawalAsync(string adminUserId, string withdrawalId, DateTime nowUtc)
        {
            var req = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
            if (req == null)
            {
                throw new NotFoundException("Yêu cầu rút tiền không tồn tại.");
            }

            // Chặn Admin duyệt yêu cầu của chính mình
            if (req.SitterUserId == adminUserId)
            {
                throw new BadRequestException("Admin không được tự duyệt yêu cầu rút tiền của chính mình.");
            }

            var filter = Builders<WithdrawalRequest>.Filter.And(
                Builders<WithdrawalRequest>.Filter.Eq(w => w.WithdrawalId, withdrawalId),
                Builders<WithdrawalRequest>.Filter.Eq(w => w.Status, WithdrawalStatus.REQUESTED)
            );
            var update = Builders<WithdrawalRequest>.Update
                .Set(w => w.Status, WithdrawalStatus.APPROVED)
                .Set(w => w.ApprovedByAdminUserId, adminUserId)
                .Set(w => w.ApprovedAt, nowUtc)
                .Set(w => w.UpdatedAt, nowUtc);

            var approved = await _context.WithdrawalRequests.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<WithdrawalRequest> { ReturnDocument = ReturnDocument.After }
            );

            if (approved == null)
            {
                var existing = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
                throw new ConflictException($"Yêu cầu rút tiền đã được cập nhật sang trạng thái '{existing?.Status}'.");
            }

            // Gửi thông báo cho Sitter
            try
            {
                await _notificationService.CreateNotificationAsync(
                    approved.SitterUserId,
                    NotificationType.WITHDRAWAL_APPROVED,
                    approved.WithdrawalId,
                    "Yêu cầu rút tiền đã được duyệt",
                    $"Yêu cầu rút {approved.Amount:N0} ₫ (Mã: {approved.ReferenceId}) đã được duyệt và đang chuẩn bị chuyển khoản.",
                    $"{approved.WithdrawalId}:APPROVED",
                    targetRole: "SITTER"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi thông báo APPROVED cho yêu cầu rút {WithdrawalId}", approved.WithdrawalId);
            }

            return MapToDto(approved);
        }

        public async Task<WithdrawalItemDto> ClaimProcessingAsync(string adminUserId, string adminName, string withdrawalId, DateTime nowUtc)
        {
            var claimTimeoutMinutes = _config.GetValue<int>("Payments:ClaimTimeoutMinutes", 30);
            var timeoutThreshold = nowUtc.AddMinutes(-claimTimeoutMinutes);

            var filter = Builders<WithdrawalRequest>.Filter.Or(
                // 1. Trạng thái APPROVED
                Builders<WithdrawalRequest>.Filter.And(
                    Builders<WithdrawalRequest>.Filter.Eq(w => w.WithdrawalId, withdrawalId),
                    Builders<WithdrawalRequest>.Filter.Eq(w => w.Status, WithdrawalStatus.APPROVED)
                ),
                // 2. Trạng thái PROCESSING nhưng đã quá thời hạn claim (timeout)
                Builders<WithdrawalRequest>.Filter.And(
                    Builders<WithdrawalRequest>.Filter.Eq(w => w.WithdrawalId, withdrawalId),
                    Builders<WithdrawalRequest>.Filter.Eq(w => w.Status, WithdrawalStatus.PROCESSING),
                    Builders<WithdrawalRequest>.Filter.Lt(w => w.ProcessingAt, timeoutThreshold)
                )
            );
            var update = Builders<WithdrawalRequest>.Update
                .Set(w => w.Status, WithdrawalStatus.PROCESSING)
                .Set(w => w.ProcessingByAdminUserId, adminUserId)
                .Set(w => w.ProcessingByAdminName, adminName)
                .Set(w => w.ProcessingAt, nowUtc)
                .Set(w => w.UpdatedAt, nowUtc);

            var claimed = await _context.WithdrawalRequests.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<WithdrawalRequest> { ReturnDocument = ReturnDocument.After }
            );

            if (claimed == null)
            {
                var existing = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
                if (existing == null) throw new NotFoundException("Yêu cầu rút tiền không tồn tại.");

                if (existing.Status == WithdrawalStatus.PROCESSING)
                {
                    if (existing.ProcessingByAdminUserId == adminUserId)
                    {
                        return MapToDto(existing); // Đã claim bởi chính Admin này
                    }
                    throw new ConflictException($"Yêu cầu này đang được xử lý bởi Admin {existing.ProcessingByAdminName ?? existing.ProcessingByAdminUserId}.");
                }
                throw new BadRequestException($"Không thể nhận xử lý yêu cầu ở trạng thái '{existing.Status}'. Yêu cầu phải ở trạng thái APPROVED hoặc quá hạn xử lý.");
            }

            return MapToDto(claimed);
        }

        public async Task<WithdrawalItemDto> UnclaimProcessingAsync(string adminUserId, string withdrawalId, DateTime nowUtc)
        {
            var req = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
            if (req == null) throw new NotFoundException("Yêu cầu rút tiền không tồn tại.");

            if (req.Status != WithdrawalStatus.PROCESSING)
            {
                throw new BadRequestException($"Không thể nhả xử lý yêu cầu ở trạng thái '{req.Status}'. Chỉ có thể nhả khi đang ở trạng thái PROCESSING.");
            }

            var claimTimeoutMinutes = _config.GetValue<int>("Payments:ClaimTimeoutMinutes", 30);
            var timeoutThreshold = nowUtc.AddMinutes(-claimTimeoutMinutes);
            bool isExpired = req.ProcessingAt.HasValue && req.ProcessingAt.Value < timeoutThreshold;

            if (req.ProcessingByAdminUserId != adminUserId && !isExpired)
            {
                throw new BadRequestException("Chỉ Admin đang nhận claim hoặc sau khi hết hạn claim (30 phút) mới có thể nhả yêu cầu.");
            }

            var filter = Builders<WithdrawalRequest>.Filter.And(
                Builders<WithdrawalRequest>.Filter.Eq(w => w.WithdrawalId, withdrawalId),
                Builders<WithdrawalRequest>.Filter.Eq(w => w.Status, WithdrawalStatus.PROCESSING)
            );
            var update = Builders<WithdrawalRequest>.Update
                .Set(w => w.Status, WithdrawalStatus.APPROVED)
                .Set(w => w.ProcessingByAdminUserId, (string?)null)
                .Set(w => w.ProcessingByAdminName, (string?)null)
                .Set(w => w.ProcessingAt, (DateTime?)null)
                .Set(w => w.UpdatedAt, nowUtc);

            var unclaimed = await _context.WithdrawalRequests.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<WithdrawalRequest> { ReturnDocument = ReturnDocument.After }
            );

            if (unclaimed == null)
            {
                throw new ConflictException("Yêu cầu rút tiền đã thay đổi trạng thái đồng thời.");
            }

            return MapToDto(unclaimed);
        }

        public async Task<WithdrawalItemDto> ConfirmPaidAsync(string adminUserId, string withdrawalId, ConfirmPaidDto dto, DateTime nowUtc)
        {
            var bankRef = dto.BankTransactionRef.Trim();

            // Kiểm tra trùng mã tham chiếu giao dịch ngân hàng
            var duplicateRef = await _context.WithdrawalRequests
                .Find(w => w.BankTransactionRef == bankRef && w.WithdrawalId != withdrawalId)
                .FirstOrDefaultAsync();
            if (duplicateRef != null)
            {
                throw new ConflictException($"Mã tham chiếu ngân hàng '{bankRef}' đã được sử dụng cho yêu cầu {duplicateRef.ReferenceId}.");
            }

            var filter = Builders<WithdrawalRequest>.Filter.And(
                Builders<WithdrawalRequest>.Filter.Eq(w => w.WithdrawalId, withdrawalId),
                Builders<WithdrawalRequest>.Filter.Eq(w => w.Status, WithdrawalStatus.PROCESSING)
            );
            var update = Builders<WithdrawalRequest>.Update
                .Set(w => w.Status, WithdrawalStatus.PAID)
                .Set(w => w.BankTransactionRef, bankRef)
                .Set(w => w.PaidByAdminUserId, adminUserId)
                .Set(w => w.PaidAt, nowUtc)
                .Set(w => w.UpdatedAt, nowUtc);

            var paid = await _context.WithdrawalRequests.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<WithdrawalRequest> { ReturnDocument = ReturnDocument.After }
            );

            if (paid == null)
            {
                var existing = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
                if (existing == null) throw new NotFoundException("Yêu cầu rút tiền không tồn tại.");
                if (existing.Status == WithdrawalStatus.PAID && existing.BankTransactionRef == bankRef)
                {
                    return MapToDto(existing); // Idempotent retry
                }
                throw new BadRequestException($"Không thể xác nhận chuyển khoản cho yêu cầu ở trạng thái '{existing.Status}'. Yêu cầu phải ở trạng thái PROCESSING.");
            }

            // GHI SỔ KÉP: DEBIT SITTER_EARNINGS:{SitterUserId}, CREDIT GATEWAY_CLEARING
            var journalEntry = new JournalEntry
            {
                EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "WTH",
                BookingId = "WITHDRAWAL:" + paid.ReferenceId,
                Type = JournalType.WITHDRAWAL_PAID,
                ActorUserId = adminUserId,
                CreatedAt = nowUtc,
                IdempotencyKey = LedgerIdempotencyKeys.WithdrawalPaid(paid.ReferenceId),
                Lines = new List<JournalLine>
                {
                    new JournalLine
                    {
                        Account = LedgerAccounts.SitterEarnings(paid.SitterUserId),
                        Direction = LedgerDirection.DEBIT,
                        Amount = paid.Amount
                    },
                    new JournalLine
                    {
                        Account = LedgerAccounts.GATEWAY_CLEARING,
                        Direction = LedgerDirection.CREDIT,
                        Amount = paid.Amount
                    }
                }
            };
            await _ledgerService.RecordEntryAsync(journalEntry);

            // Nhả ReservedAmount trên EarningsAccounts
            await _context.EarningsAccounts.UpdateOneAsync(
                a => a.UserId == paid.SitterUserId,
                Builders<EarningsAccount>.Update
                    .Inc(a => a.ReservedAmount, -paid.Amount)
                    .Inc(a => a.Version, 1)
                    .Set(a => a.UpdatedAt, nowUtc)
            );

            // Gửi thông báo cho Sitter
            try
            {
                await _notificationService.CreateNotificationAsync(
                    paid.SitterUserId,
                    NotificationType.WITHDRAWAL_PAID,
                    paid.WithdrawalId,
                    "Tiền đã được chuyển vào tài khoản",
                    $"Yêu cầu rút {paid.Amount:N0} ₫ (Mã: {paid.ReferenceId}) đã hoàn tất chuyển tiền. Mã GD ngân hàng: {paid.BankTransactionRef}.",
                    $"{paid.WithdrawalId}:PAID",
                    targetRole: "SITTER"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi thông báo PAID cho yêu cầu rút {WithdrawalId}", paid.WithdrawalId);
            }

            return MapToDto(paid);
        }

        public async Task<WithdrawalItemDto> RejectWithdrawalAsync(string adminUserId, string withdrawalId, RejectWithdrawalDto dto, DateTime nowUtc)
        {
            var req = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
            if (req == null) throw new NotFoundException("Yêu cầu rút tiền không tồn tại.");

            if (req.SitterUserId == adminUserId)
            {
                throw new BadRequestException("Admin không được thao tác trên yêu cầu rút tiền của chính mình.");
            }

            var filter = Builders<WithdrawalRequest>.Filter.And(
                Builders<WithdrawalRequest>.Filter.Eq(w => w.WithdrawalId, withdrawalId),
                Builders<WithdrawalRequest>.Filter.In(w => w.Status, new[] { WithdrawalStatus.REQUESTED, WithdrawalStatus.APPROVED, WithdrawalStatus.PROCESSING })
            );
            var update = Builders<WithdrawalRequest>.Update
                .Set(w => w.Status, WithdrawalStatus.REJECTED)
                .Set(w => w.RejectReason, dto.Reason.Trim())
                .Set(w => w.RejectedByAdminUserId, adminUserId)
                .Set(w => w.RejectedAt, nowUtc)
                .Set(w => w.UpdatedAt, nowUtc);

            var rejected = await _context.WithdrawalRequests.FindOneAndUpdateAsync(
                filter,
                update,
                new FindOneAndUpdateOptions<WithdrawalRequest> { ReturnDocument = ReturnDocument.After }
            );

            if (rejected == null)
            {
                var existing = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
                throw new ConflictException($"Không thể từ chối yêu cầu ở trạng thái '{existing?.Status}'.");
            }

            // Nhả ReservedAmount & hoàn lại DailyWithdrawn nếu trong cùng ngày VN
            var reqVnDate = TimeZoneInfo.ConvertTimeFromUtc(rejected.CreatedAt, GetVnTimeZone()).ToString("yyyy-MM-dd");
            var account = await _context.EarningsAccounts.Find(a => a.UserId == rejected.SitterUserId).FirstOrDefaultAsync();

            var updateBuilder = Builders<EarningsAccount>.Update
                .Inc(a => a.ReservedAmount, -rejected.Amount)
                .Inc(a => a.Version, 1)
                .Set(a => a.UpdatedAt, nowUtc);

            if (account != null && account.DailyWindowDate == reqVnDate)
            {
                var refundDaily = Math.Min(account.DailyWithdrawn, rejected.Amount);
                if (refundDaily > 0)
                {
                    updateBuilder = updateBuilder.Inc(a => a.DailyWithdrawn, -refundDaily);
                }
            }

            await _context.EarningsAccounts.UpdateOneAsync(
                a => a.UserId == rejected.SitterUserId,
                updateBuilder
            );

            // Gửi thông báo cho Sitter
            try
            {
                await _notificationService.CreateNotificationAsync(
                    rejected.SitterUserId,
                    NotificationType.WITHDRAWAL_REJECTED,
                    rejected.WithdrawalId,
                    "Yêu cầu rút tiền bị từ chối",
                    $"Yêu cầu rút {rejected.Amount:N0} ₫ (Mã: {rejected.ReferenceId}) bị từ chối. Lý do: {rejected.RejectReason}. Số dư đã được hoàn lại đầy đủ.",
                    $"{rejected.WithdrawalId}:REJECTED",
                    targetRole: "SITTER"
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi gửi thông báo REJECTED cho yêu cầu rút {WithdrawalId}", rejected.WithdrawalId);
            }

            return MapToDto(rejected);
        }

        public async Task<WithdrawalItemDto> QueryAndProcessPayoutAsync(string adminUserId, string withdrawalId, DateTime nowUtc)
        {
            var req = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
            if (req == null) throw new NotFoundException("Yêu cầu rút tiền không tồn tại.");

            if (req.Status != WithdrawalStatus.PROCESSING)
            {
                return MapToDto(req);
            }

            var statusRes = await _gateway.GetPayoutStatusAsync(req.ReferenceId);
            if (statusRes == null) return MapToDto(req);

            if (statusRes.Status == "SUCCESS")
            {
                var confirmDto = new ConfirmPaidDto
                {
                    BankTransactionRef = statusRes.TransactionRef ?? $"PAYOS_{req.ReferenceId}"
                };
                return await ConfirmPaidAsync(adminUserId, withdrawalId, confirmDto, nowUtc);
            }

            if (statusRes.Status == "FAILED")
            {
                var rejectDto = new RejectWithdrawalDto
                {
                    Reason = statusRes.ErrorMessage ?? "Cổng chi tự động thông báo giao dịch thất bại."
                };
                return await RejectWithdrawalAsync(adminUserId, withdrawalId, rejectDto, nowUtc);
            }

            return MapToDto(req);
        }

        public async Task<(int FixedAccountsCount, List<string> Logs)> ReconcileReservedAmountAsync(string? specificUserId = null)
        {
            var logs = new List<string>();
            int fixedCount = 0;

            FilterDefinition<EarningsAccount> filter = Builders<EarningsAccount>.Filter.Empty;
            if (!string.IsNullOrWhiteSpace(specificUserId))
            {
                filter = Builders<EarningsAccount>.Filter.Eq(a => a.UserId, specificUserId);
            }

            var accounts = await _context.EarningsAccounts.Find(filter).ToListAsync();

            foreach (var acc in accounts)
            {
                // Tổng các yêu cầu rút đang mở (REQUESTED, APPROVED, PROCESSING)
                var openReqs = await _context.WithdrawalRequests
                    .Find(w => w.SitterUserId == acc.UserId &&
                               (w.Status == WithdrawalStatus.REQUESTED ||
                                w.Status == WithdrawalStatus.APPROVED ||
                                w.Status == WithdrawalStatus.PROCESSING))
                    .ToListAsync();

                long actualOpenTotal = openReqs.Sum(w => w.Amount);

                if (acc.ReservedAmount != actualOpenTotal)
                {
                    var logMsg = $"[Tự động sửa] UserId={acc.UserId}: ReservedAmount={acc.ReservedAmount:N0} ₫ != Thực tế={actualOpenTotal:N0} ₫. Đã hiệu chỉnh lại về {actualOpenTotal:N0} ₫.";
                    _logger.LogWarning("{Message}", logMsg);
                    logs.Add(logMsg);

                    await _context.EarningsAccounts.UpdateOneAsync(
                        a => a.UserId == acc.UserId && a.Version == acc.Version,
                        Builders<EarningsAccount>.Update
                            .Set(a => a.ReservedAmount, actualOpenTotal)
                            .Inc(a => a.Version, 1)
                            .Set(a => a.UpdatedAt, DateTime.UtcNow)
                    );
                    fixedCount++;
                }
            }

            return (fixedCount, logs);
        }

        public async Task<WalletSummaryDto> GetWalletSummaryAsync(string sitterUserId, DateTime nowUtc)
        {
            var earnings = await _ledgerService.GetSitterEarningsAsync(sitterUserId, nowUtc);
            var bankAccount = await GetSitterBankAccountAsync(sitterUserId, nowUtc);

            var account = await _context.EarningsAccounts.Find(a => a.UserId == sitterUserId).FirstOrDefaultAsync();

            var vnNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, GetVnTimeZone());
            var currentVnDateStr = vnNow.ToString("yyyy-MM-dd");

            long dailyWithdrawn = 0;
            if (account != null && account.DailyWindowDate == currentVnDateStr)
            {
                dailyWithdrawn = account.DailyWithdrawn;
            }

            var dailyLimit = _config.GetValue<long>("Payments:WithdrawDailyLimit", 20000000);
            var dailyRemaining = Math.Max(0, dailyLimit - dailyWithdrawn);
            var reservedAmount = account?.ReservedAmount ?? 0;
            var availableBalance = Math.Max(0, earnings.AvailableBalance - reservedAmount);

            var paidReqs = await _context.WithdrawalRequests
                .Find(w => w.SitterUserId == sitterUserId && w.Status == WithdrawalStatus.PAID)
                .ToListAsync();
            long totalPaid = paidReqs.Sum(w => w.Amount);

            var cooldownHours = _config.GetValue<int>("Payments:BankChangeCooldownHours", 24);
            DateTime? freezeUntil = null;
            var profile = await _context.SitterProfiles.Find(p => p.UserId == sitterUserId).FirstOrDefaultAsync();
            if (profile?.BankAccount != null)
            {
                var coolUntil = profile.BankAccount.UpdatedAt.AddHours(cooldownHours);
                if (nowUtc < coolUntil) freezeUntil = coolUntil;
            }

            return new WalletSummaryDto
            {
                TotalEarnings = earnings.TotalEarnings,
                AvailableBalance = availableBalance,
                PendingHoldBalance = earnings.PendingHoldBalance,
                ReservedAmount = reservedAmount,
                WithdrawnTotal = totalPaid,
                DailyLimitRemaining = dailyRemaining,
                CanWithdraw = bankAccount.CanWithdraw && availableBalance >= 50000,
                FreezeUntil = freezeUntil,
                BankAccount = bankAccount
            };
        }

        public async Task<DecryptedAccountDto?> GetDecryptedBankAccountAsync(string sitterUserId)
        {
            var profile = await _context.SitterProfiles.Find(p => p.UserId == sitterUserId).FirstOrDefaultAsync();
            if (profile?.BankAccount == null) return null;

            var unprotectAcc = _protector.Unprotect(profile.BankAccount.EncryptedAccountNumber);
            return new DecryptedAccountDto
            {
                BankBin = profile.BankAccount.BankBin,
                BankName = profile.BankAccount.BankName,
                AccountNumber = unprotectAcc,
                AccountHolderName = profile.BankAccount.AccountHolderName
            };
        }

        public async Task<DecryptedAccountDto?> GetDecryptedWithdrawalBankAsync(string withdrawalId)
        {
            var req = await _context.WithdrawalRequests.Find(w => w.WithdrawalId == withdrawalId).FirstOrDefaultAsync();
            if (req == null) return null;

            if (!string.IsNullOrEmpty(req.EncryptedBankSnapshot))
            {
                try
                {
                    var json = _protector.Unprotect(req.EncryptedBankSnapshot);
                    var doc = JsonSerializer.Deserialize<JsonElement>(json);
                    return new DecryptedAccountDto
                    {
                        BankBin = doc.GetProperty("BankBin").GetString() ?? req.BankBin,
                        BankName = doc.GetProperty("BankName").GetString() ?? req.BankName,
                        AccountNumber = doc.GetProperty("AccountNumber").GetString() ?? string.Empty,
                        AccountHolderName = doc.GetProperty("AccountHolderName").GetString() ?? req.AccountHolderName
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi giải mã EncryptedBankSnapshot của đơn rút {WithdrawalId}", withdrawalId);
                }
            }

            return await GetDecryptedBankAccountAsync(req.SitterUserId);
        }

        private static WithdrawalItemDto MapToDto(WithdrawalRequest req)
        {
            return new WithdrawalItemDto
            {
                WithdrawalId = req.WithdrawalId,
                SitterUserId = req.SitterUserId,
                Amount = req.Amount,
                BankName = req.BankName,
                MaskedAccountNumber = req.MaskedAccountNumber,
                AccountHolderName = req.AccountHolderName,
                NameMatchesRegisteredName = req.NameMatchesRegisteredName,
                Status = req.Status,
                ReferenceId = req.ReferenceId,
                BankTransactionRef = req.BankTransactionRef,
                ProcessingByAdminUserId = req.ProcessingByAdminUserId,
                ProcessingByAdminName = req.ProcessingByAdminName,
                ProcessingAt = req.ProcessingAt,
                RejectReason = req.RejectReason,
                CreatedAt = req.CreatedAt,
                PaidAt = req.PaidAt,
                CanClaimProcessing = req.Status == WithdrawalStatus.APPROVED,
                CanConfirmPaid = req.Status == WithdrawalStatus.PROCESSING
            };
        }
    }
}
