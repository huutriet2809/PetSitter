using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    public class PaymentService : IPaymentService
    {
        private readonly PetSitterContext _context;
        private readonly IPaymentGateway _gateway;
        private readonly ILedgerService _ledgerService;
        private readonly INotificationService _notificationService;
        private readonly IDataProtector _protector;
        private readonly IConfiguration _config;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(
            PetSitterContext context,
            IPaymentGateway gateway,
            ILedgerService ledgerService,
            INotificationService notificationService,
            IDataProtectionProvider protectionProvider,
            IConfiguration config,
            ILogger<PaymentService> logger)
        {
            _context = context;
            _gateway = gateway;
            _ledgerService = ledgerService;
            _notificationService = notificationService;
            _protector = protectionProvider.CreateProtector("PetSitter.Payments.PayerInfo.v1");
            _config = config;
            _logger = logger;
        }

        public async Task<PaymentLinkDto> CreateOrGetPaymentLinkAsync(string ownerUserId, string bookingId, DateTime nowUtc)
        {
            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null || booking.OwnerId != ownerUserId)
            {
                throw new NotFoundException("Đơn đặt lịch không tồn tại hoặc bạn không có quyền truy cập.");
            }

            if (booking.PaymentStatus == PaymentStatus.HELD || booking.PaymentStatus == PaymentStatus.RELEASED)
            {
                throw new BadRequestException("Đơn đặt lịch này đã được thanh toán.");
            }

            if (booking.TrangThai != BookingStatus.ACCEPTED)
            {
                throw new BadRequestException("Chỉ có thể tạo link thanh toán khi đơn ở trạng thái đã được chấp nhận (ACCEPTED).");
            }

            // 1. Kiểm tra xem có payment còn hiệu lực không
            var existingPayment = await _context.Payments
                .Find(p => p.BookingId == bookingId && p.Status == PaymentLinkStatus.PENDING && p.ExpiresAt > nowUtc)
                .SortByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            if (existingPayment != null && !string.IsNullOrEmpty(existingPayment.CheckoutUrl))
            {
                return new PaymentLinkDto
                {
                    PaymentId = existingPayment.PaymentId,
                    BookingId = existingPayment.BookingId,
                    OrderCode = existingPayment.OrderCode,
                    Amount = existingPayment.Amount,
                    CheckoutUrl = existingPayment.CheckoutUrl,
                    QrCode = existingPayment.QrCode,
                    Status = existingPayment.Status,
                    ExpiresAt = existingPayment.ExpiresAt
                };
            }

            // 2. Tính toán hạn thanh toán (tối đa tới booking.PaymentDeadline hoặc +24h)
            var windowHours = _config.GetValue<int>("Payments:PaymentWindowHours", 24);
            var expiresAt = booking.PaymentDeadline ?? nowUtc.AddHours(windowHours);
            if (expiresAt <= nowUtc)
            {
                throw new BadRequestException("Đã quá hạn thanh toán cho đơn đặt lịch này.");
            }

            var returnUrl = _config["Payments:ReturnUrl"] ?? "http://localhost:5138/confirmation.html";
            var cancelUrl = _config["Payments:CancelUrl"] ?? "http://localhost:5138/my-bookings.html";

            // Sinh orderCode duy nhất an toàn (timestamp mili-giây dạng 53-bit int)
            // Ví dụ: YYMMddHHmmss + 3 số ngẫu nhiên (<= 15 chữ số, an toàn tuyệt đối trong MAX_SAFE_INTEGER 9007199254740991)
            var randomSuffix = Random.Shared.Next(100, 999);
            var orderCodeStr = DateTime.UtcNow.ToString("yyMMddHHmmss") + randomSuffix;
            var orderCode = long.Parse(orderCodeStr);

            var amount = booking.TotalAmount;
            if (amount <= 0 && booking.TongTien > 0)
            {
                amount = (long)booking.TongTien;
            }

            var minOrderAmount = _config.GetValue<long>("Payments:MinOrderAmount", 2000);
            if (amount < minOrderAmount)
            {
                throw new BadRequestException($"Số tiền đơn hàng ({amount:N0} ₫) nhỏ hơn mức tối thiểu ({minOrderAmount:N0} ₫) để thanh toán.");
            }

            var unixExpiresAt = new DateTimeOffset(expiresAt).ToUnixTimeSeconds();
            var desc = $"PS {booking.MaDatLich}";
            if (desc.Length > 25) desc = desc.Substring(0, 25);

            var gatewayReq = new CreatePaymentLinkRequest
            {
                OrderCode = orderCode,
                Amount = amount,
                Description = desc,
                ReturnUrl = $"{returnUrl}?bookingId={bookingId}&orderCode={orderCode}",
                CancelUrl = $"{cancelUrl}?bookingId={bookingId}&cancelled=true",
                ExpiredAt = unixExpiresAt
            };

            var gatewayRes = await _gateway.CreatePaymentLinkAsync(gatewayReq);

            var payment = new Payment
            {
                PaymentId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                BookingId = bookingId,
                OrderCode = orderCode,
                PayosPaymentLinkId = gatewayRes.PaymentLinkId,
                CheckoutUrl = gatewayRes.CheckoutUrl,
                QrCode = gatewayRes.QrCode,
                Amount = amount,
                Status = PaymentLinkStatus.PENDING,
                ExpiresAt = expiresAt,
                CreatedAt = nowUtc
            };

            await _context.Payments.InsertOneAsync(payment);

            return new PaymentLinkDto
            {
                PaymentId = payment.PaymentId,
                BookingId = payment.BookingId,
                OrderCode = payment.OrderCode,
                Amount = payment.Amount,
                CheckoutUrl = payment.CheckoutUrl,
                QrCode = payment.QrCode,
                Status = payment.Status,
                ExpiresAt = payment.ExpiresAt
            };
        }

        public async Task ProcessWebhookAsync(string rawBody, string signature, DateTime nowUtc)
        {
            // BƯỚC 1: XÁC THỰC CHỮ KÝ TRƯỚC MỌI TRUY VẤN DATABASE (Requirement 5 & 8)
            if (!_gateway.VerifyWebhookSignature(rawBody, signature))
            {
                _logger.LogWarning("PayOS Webhook rejected: Invalid signature.");
                throw new BadRequestException("Chữ ký webhook không hợp lệ.");
            }

            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            JsonElement dataElem;
            if (root.TryGetProperty("data", out var d))
            {
                dataElem = d;
            }
            else
            {
                dataElem = root;
            }

            if (!dataElem.TryGetProperty("orderCode", out var orderCodeProp) || !dataElem.TryGetProperty("amount", out var amountProp))
            {
                throw new BadRequestException("Webhook payload thiếu trường orderCode hoặc amount.");
            }

            var orderCode = orderCodeProp.GetInt64();
            var amount = amountProp.GetInt64();

            // BƯỚC 2: TRA CỨU PAYMENT
            var payment = await _context.Payments.Find(p => p.OrderCode == orderCode).FirstOrDefaultAsync();
            if (payment == null)
            {
                // Requirement 5: Chữ ký đúng nhưng OrderCode không tồn tại -> 200 + log cảnh báo, không ghi sổ
                // (PayOS gửi payload thử nghiệm khi thêm URL webhook trên cổng quản trị)
                _logger.LogWarning("Webhook signature valid but OrderCode {OrderCode} not found in system (PayOS test probe). Returning OK.", orderCode);
                return;
            }

            // BƯỚC 3: TRA CỨU VÀ KIỂM TRA SỐ TIỀN VỚI BOOKING (Requirement 5)
            var booking = await _context.Bookings.Find(b => b.BookingId == payment.BookingId).FirstOrDefaultAsync();
            if (booking == null)
            {
                throw new BadRequestException("Không tìm thấy đơn đặt lịch gắn với giao dịch.");
            }

            var expectedBookingAmount = booking.TotalAmount > 0 ? booking.TotalAmount : (long)booking.TongTien;
            if (payment.Amount != amount || expectedBookingAmount != amount)
            {
                _logger.LogError("Webhook amount mismatch for OrderCode {OrderCode}: data.Amount={Amount}, Payment.Amount={PayAmount}, Booking.TotalAmount={BookAmount}",
                    orderCode, amount, payment.Amount, expectedBookingAmount);
                throw new BadRequestException("Số tiền thanh toán không khớp với hóa đơn hoặc đơn hàng.");
            }

            // BƯỚC 4: TRÍCH XUẤT VÀ MÃ HÓA THÔNG TIN NGƯỜI TRẢ (PayerInfo)
            var payerInfo = new PayerInfo
            {
                AccountNumber = dataElem.TryGetProperty("counterAccountNumber", out var can) ? can.GetString() : null,
                AccountName = dataElem.TryGetProperty("counterAccountName", out var cn) ? cn.GetString() : null,
                BankId = dataElem.TryGetProperty("counterAccountBankId", out var cbi) ? cbi.GetString() : null,
                BankName = dataElem.TryGetProperty("counterAccountBankName", out var cbn) ? cbn.GetString() : null,
                Reference = dataElem.TryGetProperty("reference", out var rf) ? rf.GetString() : null
            };

            var payerInfoJson = JsonSerializer.Serialize(payerInfo);
            var encryptedPayerInfo = _protector.Protect(payerInfoJson);

            // BƯỚC 5: CLAIM NGUYÊN TỬ PAYMENT (PENDING / EXPIRED / CANCELLED) -> PAID BẰNG FindOneAndUpdate TRƯỚC (Requirement 1 & 4)
            var paymentClaimFilter = Builders<Payment>.Filter.And(
                Builders<Payment>.Filter.Eq(p => p.OrderCode, orderCode),
                Builders<Payment>.Filter.In(p => p.Status, new[] { PaymentLinkStatus.PENDING, PaymentLinkStatus.EXPIRED, PaymentLinkStatus.CANCELLED })
            );
            var paymentClaimUpdate = Builders<Payment>.Update
                .Set(p => p.Status, PaymentLinkStatus.PAID)
                .Set(p => p.PaidAt, nowUtc)
                .Set(p => p.EncryptedPayerInfo, encryptedPayerInfo)
                .Set(p => p.RawWebhook, $"{{\"orderCode\":{orderCode},\"amount\":{amount},\"reference\":\"{payerInfo.Reference}\"}}");

            var claimedPayment = await _context.Payments.FindOneAndUpdateAsync(
                paymentClaimFilter,
                paymentClaimUpdate,
                new FindOneAndUpdateOptions<Payment> { ReturnDocument = ReturnDocument.After }
            );

            // Nếu không claim được (do đã PAID từ lần gọi trước bị đứt giữa chừng), xác nhận payment đã ở trạng thái PAID
            if (claimedPayment == null)
            {
                var existingPay = await _context.Payments.Find(p => p.OrderCode == orderCode).FirstOrDefaultAsync();
                if (existingPay == null || existingPay.Status != PaymentLinkStatus.PAID)
                {
                    _logger.LogWarning("Payment for OrderCode {OrderCode} is not in PENDING or PAID state (current status: {Status}).", orderCode, existingPay?.Status);
                    return;
                }
            }

            // BƯỚC 6: XÁC ĐỊNH ĐƠN ĐÃ ĐÓNG THẬT HAY CHƯA (Requirement 1)
            // Chỉ vào nhánh hoàn tiền khi đơn đã đóng thật (EXPIRED hoặc CANCELLED)
            var isTrulyClosed = (booking.TrangThai == BookingStatus.EXPIRED || booking.TrangThai == BookingStatus.CANCELLED ||
                                 booking.PaymentStatus == PaymentStatus.EXPIRED || booking.PaymentStatus == PaymentStatus.CANCELLED);

            if (isTrulyClosed)
            {
                // KỊCH BẢN B: ĐƠN ĐÃ ĐÓNG THẬT (Lazy EXPIRED hoặc CANCELLED đã thắng trước đó) -> THANH TOÁN ĐẾN MUỘN
                _logger.LogWarning("Late payment received for truly closed booking {BookingId} (OrderCode {OrderCode}). Moving to REFUND_PENDING.", payment.BookingId, orderCode);

                await _context.Bookings.UpdateOneAsync(
                    b => b.BookingId == booking.BookingId,
                    Builders<Booking>.Update.Set(b => b.PaymentStatus, PaymentStatus.REFUND_PENDING)
                );

                // Ghi sổ kép: DEBIT GATEWAY_CLEARING, CREDIT REFUND_PENDING (Idempotent theo key)
                var lateRefundEntry = new JournalEntry
                {
                    EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    BookingId = payment.BookingId,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "LATE",
                    Type = JournalType.LATE_PAYMENT_REFUND,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc,
                    IdempotencyKey = $"{payment.BookingId}:LATE_PAYMENT_REFUND:1",
                    Lines = new List<JournalLine>
                    {
                        new JournalLine
                        {
                            Account = LedgerAccount.GATEWAY_CLEARING,
                            Direction = LedgerDirection.DEBIT,
                            Amount = amount
                        },
                        new JournalLine
                        {
                            Account = LedgerAccount.REFUND_PENDING,
                            Direction = LedgerDirection.CREDIT,
                            Amount = amount
                        }
                    }
                };
                await _ledgerService.RecordEntryAsync(lateRefundEntry);

                // Tạo yêu cầu hoàn tiền nếu chưa tồn tại
                var existingRefund = await _context.RefundRequests.Find(r => r.BookingId == booking.BookingId).FirstOrDefaultAsync();
                if (existingRefund == null)
                {
                    var refundReq = new RefundRequest
                    {
                        RefundId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        BookingId = booking.BookingId,
                        PaymentId = payment.PaymentId,
                        OwnerUserId = booking.OwnerId,
                        Amount = amount,
                        Reason = "LATE_PAYMENT_ON_CLOSED_BOOKING",
                        Status = RefundStatus.REFUND_PENDING,
                        EncryptedRefundAccount = encryptedPayerInfo,
                        CreatedAt = nowUtc
                    };
                    await _context.RefundRequests.InsertOneAsync(refundReq);
                }

                // Gửi thông báo cho Owner và Admin
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        booking.OwnerId,
                        NotificationType.REFUND_PENDING,
                        booking.BookingId,
                        "Hoàn tiền do thanh toán muộn",
                        $"Thanh toán {amount:N0} ₫ cho đơn {booking.MaDatLich} được nhận sau khi đơn đã hết hạn. Hệ thống đang tiến hành hoàn tiền cho bạn.",
                        $"{booking.BookingId}:{NotificationType.REFUND_PENDING}:{booking.OwnerId}",
                        targetRole: "OWNER"
                    );

                    var admins = await _context.Users.Find(u => u.Role == "Admin").ToListAsync();
                    foreach (var admin in admins)
                    {
                        await _notificationService.CreateNotificationAsync(
                            admin.UserId,
                            NotificationType.REFUND_PENDING,
                            payment.BookingId,
                            "Yêu cầu hoàn tiền thanh toán muộn",
                            $"Đơn {payment.BookingId} thanh toán sau khi hết hạn ({amount:N0} ₫). Đã đưa vào hàng chờ hoàn tiền.",
                            $"{payment.BookingId}:{NotificationType.REFUND_PENDING}:{admin.UserId}",
                            targetRole: "ADMIN"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi thông báo thanh toán muộn cho đơn {BookingId}", payment.BookingId);
                }

                return;
            }

            // BƯỚC 7: ĐƠN CHƯA ĐÓNG -> HOÀN THIỆN ĐƠN (Booking -> HELD, Ghi sổ kép)
            // Chạy lặp lại được (idempotent): Chuyển PENDING_PAYMENT sang HELD nếu đơn vẫn ở PENDING_PAYMENT
            var bookingFilter = Builders<Booking>.Filter.And(
                Builders<Booking>.Filter.Eq(b => b.BookingId, payment.BookingId),
                Builders<Booking>.Filter.Eq(b => b.PaymentStatus, PaymentStatus.PENDING_PAYMENT)
            );
            var bookingUpdate = Builders<Booking>.Update
                .Set(b => b.PaymentStatus, PaymentStatus.HELD)
                .Push(b => b.History, new InquiryHistory
                {
                    HanhDong = "PAYMENT_HELD",
                    FromStatus = BookingStatus.ACCEPTED,
                    NguoiThucHien = "SYSTEM",
                    ActorUserId = null,
                    LyDo = "Thanh toán thành công qua PayOS",
                    ThoiGian = nowUtc
                });

            await _context.Bookings.UpdateOneAsync(bookingFilter, bookingUpdate);

            // Ghi sổ kép: DEBIT GATEWAY_CLEARING, CREDIT ESCROW_HELD (Bắt buộc đúng một bút toán, idempotent theo IdempotencyKey)
            var journalEntry = new JournalEntry
            {
                EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                BookingId = payment.BookingId,
                TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + orderCode.ToString().Substring(Math.Max(0, orderCode.ToString().Length - 4)),
                Type = JournalType.PAYMENT_HELD,
                ActorUserId = "SYSTEM",
                CreatedAt = nowUtc,
                IdempotencyKey = $"{payment.BookingId}:PAYMENT_HELD:1",
                Lines = new List<JournalLine>
                {
                    new JournalLine
                    {
                        Account = LedgerAccount.GATEWAY_CLEARING,
                        Direction = LedgerDirection.DEBIT,
                        Amount = amount
                    },
                    new JournalLine
                    {
                        Account = LedgerAccount.ESCROW_HELD,
                        Direction = LedgerDirection.CREDIT,
                        Amount = amount
                    }
                }
            };
            await _ledgerService.RecordEntryAsync(journalEntry);

            // Gửi thông báo in-app (an toàn với DedupeKey)
            try
            {
                // Cho Owner
                await _notificationService.CreateNotificationAsync(
                    booking.OwnerId,
                    NotificationType.PAYMENT_SUCCESS,
                    booking.BookingId,
                    "Thanh toán thành công",
                    $"Đơn {booking.MaDatLich} đã thanh toán thành công {amount:N0} ₫. Tiền được tạm giữ an toàn cho tới khi hoàn tất.",
                    $"{booking.BookingId}:{NotificationType.PAYMENT_SUCCESS}:{booking.OwnerId}",
                    targetRole: "OWNER"
                );

                // Cho Sitter
                var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == booking.SitterId).FirstOrDefaultAsync();
                if (sitterProfile != null && !string.IsNullOrEmpty(sitterProfile.UserId))
                {
                    await _notificationService.CreateNotificationAsync(
                        sitterProfile.UserId,
                        NotificationType.PAYMENT_SUCCESS,
                        booking.BookingId,
                        "Khách hàng đã thanh toán",
                        $"Khách hàng đã thanh toán {amount:N0} ₫ cho đơn {booking.MaDatLich}. Bạn có thể an tâm thực hiện dịch vụ.",
                        $"{booking.BookingId}:{NotificationType.PAYMENT_SUCCESS}:{sitterProfile.UserId}",
                        targetRole: "SITTER"
                    );
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gửi thông báo thanh toán thành công cho đơn {BookingId}", payment.BookingId);
            }
        }

        public async Task<PaymentStatusDto> GetBookingPaymentStatusAsync(string currentUserId, string userRole, string bookingId)
        {
            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null)
            {
                throw new NotFoundException("Đơn đặt lịch không tồn tại.");
            }

            var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == booking.SitterId).FirstOrDefaultAsync();
            var isSitter = sitterProfile != null && sitterProfile.UserId == currentUserId;
            var isOwner = booking.OwnerId == currentUserId;
            var isAdmin = userRole == "Admin";

            if (!isOwner && !isSitter && !isAdmin)
            {
                throw new NotFoundException("Bạn không có quyền xem thông tin thanh toán của đơn này.");
            }

            var latestPayment = await _context.Payments
                .Find(p => p.BookingId == bookingId)
                .SortByDescending(p => p.CreatedAt)
                .FirstOrDefaultAsync();

            var totalAmount = booking.TotalAmount > 0 ? booking.TotalAmount : (long)booking.TongTien;
            var commissionAmount = booking.CommissionAmount > 0 ? booking.CommissionAmount : (totalAmount * (booking.CommissionBps > 0 ? booking.CommissionBps : 1000) / 10000);
            var sitterAmount = booking.SitterAmount > 0 ? booking.SitterAmount : (totalAmount - commissionAmount);

            var canPay = isOwner && booking.TrangThai == BookingStatus.ACCEPTED && booking.PaymentStatus == PaymentStatus.PENDING_PAYMENT;

            return new PaymentStatusDto
            {
                PaymentStatus = booking.PaymentStatus,
                PaymentDeadline = booking.PaymentDeadline,
                TotalAmount = totalAmount,
                CommissionAmount = commissionAmount,
                SitterAmount = sitterAmount,
                CheckoutUrl = isOwner ? latestPayment?.CheckoutUrl : null, // Sitter không cần url thanh toán
                QrCode = isOwner ? latestPayment?.QrCode : null,
                PaidAt = latestPayment?.PaidAt,
                CanPay = canPay
            };
        }

        public async Task<List<RefundQueueItemDto>> GetRefundQueueAsync(int limit = 50)
        {
            var list = await _context.RefundRequests
                .Find(r => r.Status == RefundStatus.REFUND_PENDING)
                .SortByDescending(r => r.CreatedAt)
                .Limit(limit)
                .ToListAsync();

            var ownerIds = list.Select(r => r.OwnerUserId).Distinct().ToList();
            var users = await _context.Users.Find(u => ownerIds.Contains(u.UserId)).ToListAsync();
            var userMap = users.ToDictionary(u => u.UserId, u => u);

            var result = new List<RefundQueueItemDto>();
            foreach (var item in list)
            {
                userMap.TryGetValue(item.OwnerUserId, out var owner);

                string? maskedPayer = null;
                if (!string.IsNullOrEmpty(item.EncryptedRefundAccount))
                {
                    try
                    {
                        var decrypted = _protector.Unprotect(item.EncryptedRefundAccount);
                        // Che số tài khoản khi hiển thị tổng quan
                        var payer = JsonSerializer.Deserialize<PayerInfo>(decrypted);
                        if (payer != null && !string.IsNullOrEmpty(payer.AccountNumber))
                        {
                            var acc = payer.AccountNumber;
                            var masked = acc.Length > 4 ? new string('*', acc.Length - 4) + acc.Substring(acc.Length - 4) : acc;
                            maskedPayer = $"{payer.BankName ?? payer.BankId ?? "NH"}: {masked} ({payer.AccountName ?? "Khách"})";
                        }
                    }
                    catch
                    {
                        maskedPayer = "Thông tin đã mã hóa";
                    }
                }

                result.Add(new RefundQueueItemDto
                {
                    RefundId = item.RefundId,
                    BookingId = item.BookingId,
                    OwnerUserId = item.OwnerUserId,
                    OwnerName = owner?.FullName ?? "Chủ nuôi",
                    OwnerEmail = owner?.Email ?? "",
                    Amount = item.Amount,
                    Reason = item.Reason,
                    Status = item.Status,
                    CreatedAt = item.CreatedAt,
                    PayerInfoMasked = maskedPayer,
                    BankTransactionRef = item.BankTransactionRef
                });
            }

            return result;
        }

        public async Task CompleteRefundAsync(string adminUserId, string refundId, CompleteRefundDto dto, DateTime nowUtc)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.BankTransactionRef))
            {
                throw new BadRequestException("Mã tham chiếu giao dịch ngân hàng là bắt buộc.");
            }

            var refund = await _context.RefundRequests.Find(r => r.RefundId == refundId).FirstOrDefaultAsync();
            if (refund == null)
            {
                throw new NotFoundException("Yêu cầu hoàn tiền không tồn tại.");
            }

            if (refund.Status == RefundStatus.COMPLETED)
            {
                return; // Idempotent
            }

            // Ghi sổ kép: DEBIT REFUND_PENDING, CREDIT GATEWAY_CLEARING
            var idempotencyKey = $"{refund.BookingId}:REFUND_COMPLETED:{refund.RefundId}";
            var entry = new JournalEntry
            {
                EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                BookingId = refund.BookingId,
                TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "REFUND",
                Type = JournalType.REFUND_COMPLETED,
                ActorUserId = adminUserId,
                CreatedAt = nowUtc,
                IdempotencyKey = idempotencyKey,
                Lines = new List<JournalLine>
                {
                    new JournalLine
                    {
                        Account = LedgerAccount.REFUND_PENDING,
                        Direction = LedgerDirection.DEBIT,
                        Amount = refund.Amount
                    },
                    new JournalLine
                    {
                        Account = LedgerAccount.GATEWAY_CLEARING,
                        Direction = LedgerDirection.CREDIT,
                        Amount = refund.Amount
                    }
                }
            };
            await _ledgerService.RecordEntryAsync(entry);

            // Cập nhật trạng thái hoàn tiền
            await _context.RefundRequests.UpdateOneAsync(
                r => r.RefundId == refundId,
                Builders<RefundRequest>.Update
                    .Set(r => r.Status, RefundStatus.COMPLETED)
                    .Set(r => r.BankTransactionRef, dto.BankTransactionRef)
                    .Set(r => r.AdminUserId, adminUserId)
                    .Set(r => r.CompletedAt, nowUtc)
            );

            // Cập nhật Booking PaymentStatus
            var booking = await _context.Bookings.Find(b => b.BookingId == refund.BookingId).FirstOrDefaultAsync();
            if (booking != null)
            {
                var newStatus = (booking.TotalAmount > 0 && refund.Amount < booking.TotalAmount) ? PaymentStatus.PARTIALLY_REFUNDED : PaymentStatus.REFUNDED;
                await _context.Bookings.UpdateOneAsync(
                    b => b.BookingId == booking.BookingId,
                    Builders<Booking>.Update
                        .Set(b => b.PaymentStatus, newStatus)
                        .Push(b => b.History, new InquiryHistory
                        {
                            HanhDong = newStatus,
                            FromStatus = booking.TrangThai,
                            NguoiThucHien = "ADMIN",
                            ActorUserId = adminUserId,
                            LyDo = $"Hoàn tiền thành công (GD: {dto.BankTransactionRef})",
                            ThoiGian = nowUtc
                        })
                );

                // Gửi thông báo cho Owner
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        refund.OwnerUserId,
                        NotificationType.REFUND_COMPLETED,
                        booking.BookingId,
                        "Hoàn tiền thành công",
                        $"Hệ thống đã hoàn tất chuyển khoản hoàn tiền {refund.Amount:N0} ₫ cho đơn {booking.MaDatLich} (Mã GD: {dto.BankTransactionRef}).",
                        $"{booking.BookingId}:{NotificationType.REFUND_COMPLETED}:{refund.RefundId}",
                        targetRole: "OWNER"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi thông báo hoàn tiền thành công cho đơn {BookingId}", refund.BookingId);
                }
            }
        }

        public async Task UpdateRefundAccountAsync(string ownerUserId, string bookingId, UpdateRefundAccountDto dto)
        {
            var booking = await _context.Bookings.Find(b => b.BookingId == bookingId).FirstOrDefaultAsync();
            if (booking == null || booking.OwnerId != ownerUserId)
            {
                throw new NotFoundException("Đơn đặt lịch không tồn tại hoặc bạn không có quyền cập nhật.");
            }

            var refundRequest = await _context.RefundRequests
                .Find(r => r.BookingId == bookingId && r.Status == RefundStatus.REFUND_PENDING)
                .FirstOrDefaultAsync();

            if (refundRequest == null)
            {
                throw new BadRequestException("Không tìm thấy yêu cầu hoàn tiền đang chờ cho đơn này.");
            }

            var accountInfo = new PayerInfo
            {
                BankName = dto.BankName,
                AccountNumber = dto.AccountNumber,
                AccountName = dto.AccountName
            };

            var encrypted = _protector.Protect(JsonSerializer.Serialize(accountInfo));
            await _context.RefundRequests.UpdateOneAsync(
                r => r.RefundId == refundRequest.RefundId,
                Builders<RefundRequest>.Update.Set(r => r.EncryptedRefundAccount, encrypted)
            );
        }
    }
}
