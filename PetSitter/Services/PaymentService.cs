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
        private readonly IEmailService? _emailService;
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
            ILogger<PaymentService> logger,
            IEmailService? emailService = null)
        {
            _context = context;
            _gateway = gateway;
            _ledgerService = ledgerService;
            _notificationService = notificationService;
            _emailService = emailService;
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

            // Kiểm tra hạn mức gói PayOS trong kỳ
            var quota = _config.GetValue<long>("Payments:PayosPlanQuota", 100);
            var baseline = _config.GetValue<long>("Payments:PayosPlanUsedBaseline", 5);
            var platformAcc = await _context.PlatformAccounts.Find(p => p.AccountId == "platform_main").FirstOrDefaultAsync();
            long linksCreated = platformAcc?.PayosLinksCreatedCount ?? 0;
            if (baseline + linksCreated >= quota)
            {
                throw new BadRequestException("Hệ thống thanh toán trực tuyến hiện đang tạm thời hết hạn mức giao dịch trong kỳ. Vui lòng liên hệ bộ phận hỗ trợ hoặc thanh toán sau.");
            }

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

            // Tăng bộ đếm link đã tạo trên PlatformAccount
            var updatedPlatformAcc = await _context.PlatformAccounts.FindOneAndUpdateAsync(
                p => p.AccountId == "platform_main",
                Builders<PlatformAccount>.Update
                    .Inc(p => p.PayosLinksCreatedCount, 1)
                    .Set(p => p.UpdatedAt, nowUtc),
                new FindOneAndUpdateOptions<PlatformAccount> { IsUpsert = true, ReturnDocument = ReturnDocument.After }
            );

            // Cảnh báo hạn mức PayOS nếu còn ít
            long remaining = quota - (baseline + (updatedPlatformAcc?.PayosLinksCreatedCount ?? 0));
            long warningRemaining = _config.GetValue<long>("Payments:QuotaWarningRemaining", 15);
            if (remaining <= warningRemaining)
            {
                _logger.LogWarning("CẢNH BÁO HẠN MỨC PAYOS: Số link thanh toán còn lại ({Remaining}/{Quota}) đã đạt ngưỡng cảnh báo ({WarningRemaining}).",
                    remaining, quota, warningRemaining);
            }

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
                _logger.LogWarning("Phát hiện giao dịch bất thường (sai lệch số tiền) cho OrderCode {OrderCode}: data.Amount={Amount}, Payment.Amount={PayAmount}, Booking.TotalAmount={BookAmount}",
                    orderCode, amount, payment.Amount, expectedBookingAmount);

                // Lưu bản ghi giao dịch bất thường trên Payment
                await _context.Payments.UpdateOneAsync(
                    p => p.OrderCode == orderCode,
                    Builders<Payment>.Update
                        .Set(p => p.IsAbnormal, true)
                        .Set(p => p.Status, PaymentLinkStatus.ABNORMAL)
                        .Set(p => p.ReceivedAmount, amount)
                        .Set(p => p.AbnormalReason, $"Số tiền thanh toán ({amount:N0} ₫) không khớp với số tiền đơn ({expectedBookingAmount:N0} ₫ / {payment.Amount:N0} ₫).")
                        .Set(p => p.RawWebhook, $"{{\"orderCode\":{orderCode},\"receivedAmount\":{amount},\"expectedAmount\":{expectedBookingAmount}}}")
                );

                // Ghi sổ kép vào tài khoản SUSPENSE để đối chiếu tiền thực tế từ ngân hàng mà không ghi nhận doanh thu
                var suspenseEntry = new JournalEntry
                {
                    EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    BookingId = payment.BookingId,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "SUSP",
                    Type = JournalType.PAYMENT_ABNORMAL_SUSPENSE,
                    ActorUserId = "GATEWAY_WEBHOOK",
                    CreatedAt = nowUtc,
                    IdempotencyKey = LedgerIdempotencyKeys.AbnormalSuspense(orderCode),
                    Lines = new List<JournalLine>
                    {
                        new JournalLine
                        {
                            Account = LedgerAccounts.GATEWAY_CLEARING,
                            Direction = LedgerDirection.DEBIT,
                            Amount = amount
                        },
                        new JournalLine
                        {
                            Account = LedgerAccounts.SUSPENSE,
                            Direction = LedgerDirection.CREDIT,
                            Amount = amount
                        }
                    }
                };
                await _ledgerService.RecordEntryAsync(suspenseEntry);

                // Báo Admin
                try
                {
                    var admins = await _context.Users.Find(u => u.Role == "ADMIN" || u.Role == "Admin").ToListAsync();
                    foreach (var admin in admins)
                    {
                        await _notificationService.CreateNotificationAsync(
                            admin.UserId,
                            NotificationType.PAYMENT_ABNORMAL,
                            payment.BookingId,
                            "Cảnh báo giao dịch bất thường",
                            $"Đơn {payment.BookingId} (OrderCode: {orderCode}) nhận thanh toán sai số tiền: nhận {amount:N0} ₫, mong đợi {expectedBookingAmount:N0} ₫. Tiền đã được chuyển vào tài khoản SUSPENSE.",
                            $"{orderCode}:ABNORMAL_PAYMENT:{admin.UserId}",
                            targetRole: "ADMIN"
                        );
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi thông báo giao dịch bất thường tới Admin.");
                }

                // Trả về không ném lỗi (Controller trả 200 OK), KHÔNG ghi sổ doanh thu!
                return;
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

                var latePlan = new SettlementPlan
                {
                    SettlementKey = LedgerIdempotencyKeys.LatePaymentRefund(booking.BookingId),
                    SettlementType = "LATE_PAYMENT_REFUND",
                    TotalAmount = amount,
                    RefundAmount = amount,
                    CommissionAmount = 0,
                    SitterAmount = 0,
                    ClaimedAt = nowUtc
                };

                await _context.Bookings.UpdateOneAsync(
                    b => b.BookingId == booking.BookingId,
                    Builders<Booking>.Update
                        .Set(b => b.PaymentStatus, PaymentStatus.REFUND_PENDING)
                        .Set(b => b.Settlement, latePlan)
                        .Set(b => b.ClaimedAt, nowUtc)
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
                    IdempotencyKey = LedgerIdempotencyKeys.LatePaymentRefund(booking.BookingId),
                    Lines = new List<JournalLine>
                    {
                        new JournalLine
                        {
                            Account = LedgerAccounts.GATEWAY_CLEARING,
                            Direction = LedgerDirection.DEBIT,
                            Amount = amount
                        },
                        new JournalLine
                        {
                            Account = LedgerAccounts.REFUND_PENDING,
                            Direction = LedgerDirection.CREDIT,
                            Amount = amount
                        }
                    }
                };
                await _ledgerService.RecordEntryAsync(lateRefundEntry);

                // Tạo yêu cầu hoàn tiền nếu chưa tồn tại (idempotent, an toàn đua luồng nhờ unique PaymentId index)
                var existingRefund = await _context.RefundRequests.Find(r => r.PaymentId == payment.PaymentId || r.BookingId == booking.BookingId).FirstOrDefaultAsync();
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
                    try
                    {
                        await _context.RefundRequests.InsertOneAsync(refundReq);
                    }
                    catch (MongoWriteException mwe) when (mwe.WriteError?.Category == ServerErrorCategory.DuplicateKey)
                    {
                        _logger.LogInformation("RefundRequest cho PaymentId {PaymentId} đã được tạo bởi luồng khác.", payment.PaymentId);
                    }
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

                    var admins = await _context.Users.Find(u => u.Role == "ADMIN" || u.Role == "Admin").ToListAsync();
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
                IdempotencyKey = LedgerIdempotencyKeys.PaymentHeld(payment.BookingId),
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

            // Ghi nhận phí cổng (GATEWAY_FEES) nếu có cấu hình
            var gatewayFee = _config.GetValue<long>("Payments:GatewayFeePerTransaction", 0);
            if (gatewayFee > 0)
            {
                var feeEntry = new JournalEntry
                {
                    EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    BookingId = payment.BookingId,
                    TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "FEE",
                    Type = JournalType.GATEWAY_FEE,
                    ActorUserId = "SYSTEM",
                    CreatedAt = nowUtc,
                    IdempotencyKey = LedgerIdempotencyKeys.GatewayFee(payment.OrderCode),
                    Lines = new List<JournalLine>
                    {
                        new JournalLine
                        {
                            Account = LedgerAccounts.GATEWAY_FEES,
                            Direction = LedgerDirection.DEBIT,
                            Amount = gatewayFee
                        },
                        new JournalLine
                        {
                            Account = LedgerAccounts.GATEWAY_CLEARING,
                            Direction = LedgerDirection.CREDIT,
                            Amount = gatewayFee
                        }
                    }
                };
                await _ledgerService.RecordEntryAsync(feeEntry);
            }

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

                if (_emailService != null && !string.IsNullOrEmpty(booking.OwnerId))
                {
                    var ownerUser = await _context.Users.Find(u => u.UserId == booking.OwnerId).FirstOrDefaultAsync();
                    if (ownerUser != null && !string.IsNullOrWhiteSpace(ownerUser.Email))
                    {
                        await _emailService.SendPaymentSuccessAsync(
                            ownerUser.Email,
                            ownerUser.FullName ?? "Quý khách",
                            booking,
                            amount
                        );
                    }
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

            // Kiểm tra và chặn Admin hoàn tiền đơn của chính mình (Requirement 3)
            var booking = await _context.Bookings.Find(b => b.BookingId == refund.BookingId).FirstOrDefaultAsync();
            if (booking != null)
            {
                var sitterProfile = await _context.SitterProfiles.Find(s => s.SitterId == booking.SitterId).FirstOrDefaultAsync();
                if (booking.OwnerId == adminUserId || sitterProfile?.UserId == adminUserId)
                {
                    throw new ForbiddenException("Quản trị viên không được phép hoàn tiền cho đơn đặt lịch của chính mình.");
                }
            }

            // BƯỚC 1: Claim nguyên tử RefundRequest PENDING/REFUND_PENDING -> COMPLETED
            var refundClaimFilter = Builders<RefundRequest>.Filter.And(
                Builders<RefundRequest>.Filter.Eq(r => r.RefundId, refundId),
                Builders<RefundRequest>.Filter.In(r => r.Status, new[] { RefundStatus.REFUND_PENDING, "PENDING" })
            );
            var refundClaimUpdate = Builders<RefundRequest>.Update
                .Set(r => r.Status, RefundStatus.COMPLETED)
                .Set(r => r.BankTransactionRef, dto.BankTransactionRef.Trim())
                .Set(r => r.AdminUserId, adminUserId)
                .Set(r => r.CompletedAt, nowUtc);

            var claimedRefund = await _context.RefundRequests.FindOneAndUpdateAsync(
                refundClaimFilter,
                refundClaimUpdate,
                new FindOneAndUpdateOptions<RefundRequest> { ReturnDocument = ReturnDocument.After }
            );

            // Nếu không claim được (do đã COMPLETED từ lần gọi trước bị ngắt giữa các bước), tiếp tục thực hiện các bước còn lại
            if (claimedRefund == null)
            {
                var existingRefund = await _context.RefundRequests.Find(r => r.RefundId == refundId).FirstOrDefaultAsync();
                if (existingRefund == null || existingRefund.Status != RefundStatus.COMPLETED)
                {
                    throw new BadRequestException("Yêu cầu hoàn tiền không ở trạng thái hợp lệ để hoàn tất.");
                }
                claimedRefund = existingRefund;
            }

            // BƯỚC 2: Ghi sổ kép: DEBIT REFUND_PENDING, CREDIT GATEWAY_CLEARING (Idempotent theo RefundId)
            var idempotencyKey = LedgerIdempotencyKeys.RefundCompleted(claimedRefund.RefundId);
            var entry = new JournalEntry
            {
                EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                BookingId = claimedRefund.BookingId,
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
                        Amount = claimedRefund.Amount
                    },
                    new JournalLine
                    {
                        Account = LedgerAccount.GATEWAY_CLEARING,
                        Direction = LedgerDirection.CREDIT,
                        Amount = claimedRefund.Amount
                    }
                }
            };
            await _ledgerService.RecordEntryAsync(entry);

            // BƯỚC 3: Cập nhật Booking PaymentStatus sang REFUNDED hoặc PARTIALLY_REFUNDED
            if (booking != null)
            {
                var totalAmount = booking.TotalAmount > 0 ? booking.TotalAmount : (long)booking.TongTien;
                var newStatus = (totalAmount > 0 && claimedRefund.Amount < totalAmount) ? PaymentStatus.PARTIALLY_REFUNDED : PaymentStatus.REFUNDED;
                await _context.Bookings.UpdateOneAsync(
                    b => b.BookingId == booking.BookingId,
                    Builders<Booking>.Update
                        .Set(b => b.PaymentStatus, newStatus)
                        .Set(b => b.UpdatedAt, nowUtc)
                        .Push(b => b.History, new InquiryHistory
                        {
                            HanhDong = newStatus,
                            FromStatus = booking.TrangThai,
                            NguoiThucHien = "ADMIN",
                            ActorUserId = adminUserId,
                            LyDo = $"Hoàn tiền thành công (Mã GD: {dto.BankTransactionRef.Trim()})",
                            ThoiGian = nowUtc
                        })
                );

                // Log kiểm toán (Audit log)
                _logger.LogInformation("[AUDIT] Admin {AdminUserId} hoàn tất hoàn tiền {RefundId} cho đơn {BookingId}, số tiền {Amount} ₫, Mã GD NH {BankRef} tại {NowUtc}",
                    adminUserId, refundId, claimedRefund.BookingId, claimedRefund.Amount, dto.BankTransactionRef.Trim(), nowUtc);

                // Gửi thông báo cho Owner
                try
                {
                    await _notificationService.CreateNotificationAsync(
                        claimedRefund.OwnerUserId,
                        NotificationType.REFUND_COMPLETED,
                        booking.BookingId,
                        "Hoàn tiền thành công",
                        $"Hệ thống đã hoàn tất chuyển khoản hoàn tiền {claimedRefund.Amount:N0} ₫ cho đơn {booking.MaDatLich} (Mã GD: {dto.BankTransactionRef.Trim()}).",
                        $"{booking.BookingId}:{NotificationType.REFUND_COMPLETED}:{claimedRefund.RefundId}",
                        targetRole: "OWNER"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi gửi thông báo hoàn tiền thành công cho đơn {BookingId}", claimedRefund.BookingId);
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

        public async Task AssignSuspensePaymentAsync(string adminUserId, long orderCode, AssignSuspenseDto dto, DateTime nowUtc)
        {
            var payment = await _context.Payments.Find(p => p.OrderCode == orderCode && p.IsAbnormal == true).FirstOrDefaultAsync();
            if (payment == null)
            {
                throw new NotFoundException($"Không tìm thấy giao dịch bất thường (SUSPENSE) với OrderCode {orderCode}.");
            }

            var booking = await _context.Bookings.Find(b => b.BookingId == dto.BookingId.Trim()).FirstOrDefaultAsync();
            if (booking == null)
            {
                throw new NotFoundException($"Không tìm thấy đơn đặt lịch {dto.BookingId}.");
            }

            if (booking.TrangThai != BookingStatus.ACCEPTED || booking.PaymentStatus != PaymentStatus.PENDING_PAYMENT)
            {
                throw new BadRequestException($"Đơn {booking.MaDatLich} không ở trạng thái chờ thanh toán (hiện tại: {booking.TrangThai}/{booking.PaymentStatus}).");
            }

            var expectedAmount = booking.TotalAmount > 0 ? booking.TotalAmount : (long)booking.TongTien;
            if (expectedAmount != payment.ReceivedAmount)
            {
                throw new BadRequestException($"Số tiền đơn ({expectedAmount:N0} ₫) không khớp với số tiền thực nhận ({payment.ReceivedAmount:N0} ₫).");
            }

            // Chuyển booking sang HELD
            await _context.Bookings.UpdateOneAsync(
                b => b.BookingId == booking.BookingId && b.PaymentStatus == PaymentStatus.PENDING_PAYMENT,
                Builders<Booking>.Update
                    .Set(b => b.PaymentStatus, PaymentStatus.HELD)
                    .Push(b => b.History, new InquiryHistory
                    {
                        HanhDong = "PAYMENT_HELD_FROM_SUSPENSE",
                        FromStatus = booking.TrangThai,
                        NguoiThucHien = "ADMIN",
                        ActorUserId = adminUserId,
                        LyDo = $"Gán từ giao dịch bất thường OrderCode {orderCode}: {dto.Note}",
                        ThoiGian = nowUtc
                    })
            );

            // Cập nhật Payment sang PAID hợp lệ
            await _context.Payments.UpdateOneAsync(
                p => p.OrderCode == orderCode,
                Builders<Payment>.Update
                    .Set(p => p.Status, PaymentLinkStatus.PAID)
                    .Set(p => p.IsAbnormal, false)
                    .Set(p => p.BookingId, booking.BookingId)
                    .Set(p => p.PaidAt, nowUtc)
            );

            var receivedAmt = payment.ReceivedAmount ?? payment.Amount;

            // Ghi sổ chuyển từ SUSPENSE -> ESCROW_HELD
            var journalEntry = new JournalEntry
            {
                EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                BookingId = booking.BookingId,
                TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "ASGN",
                Type = JournalType.SUSPENSE_ASSIGN_BOOKING,
                ActorUserId = adminUserId,
                CreatedAt = nowUtc,
                IdempotencyKey = LedgerIdempotencyKeys.SuspenseAssign(orderCode),
                Lines = new List<JournalLine>
                {
                    new JournalLine
                    {
                        Account = LedgerAccounts.SUSPENSE,
                        Direction = LedgerDirection.DEBIT,
                        Amount = receivedAmt
                    },
                    new JournalLine
                    {
                        Account = LedgerAccounts.ESCROW_HELD,
                        Direction = LedgerDirection.CREDIT,
                        Amount = receivedAmt
                    }
                }
            };
            await _ledgerService.RecordEntryAsync(journalEntry);

            _logger.LogInformation("[AUDIT] Admin {AdminUserId} đã gán giao dịch bất thường {OrderCode} ({Amount} ₫) vào đơn {BookingId}. Note: {Note}",
                adminUserId, orderCode, receivedAmt, booking.BookingId, dto.Note);
        }

        public async Task RefundSuspensePaymentAsync(string adminUserId, long orderCode, RefundSuspenseDto dto, DateTime nowUtc)
        {
            var payment = await _context.Payments.Find(p => p.OrderCode == orderCode && p.IsAbnormal == true).FirstOrDefaultAsync();
            if (payment == null)
            {
                throw new NotFoundException($"Không tìm thấy giao dịch bất thường (SUSPENSE) với OrderCode {orderCode}.");
            }

            var receivedAmt = payment.ReceivedAmount ?? payment.Amount;
            var settlementKey = LedgerIdempotencyKeys.SuspenseRefund(orderCode);

            // Kiểm tra đã có RefundRequest chưa (idempotent)
            var existingRefund = await _context.RefundRequests.Find(r => r.SettlementKey == settlementKey).FirstOrDefaultAsync();
            if (existingRefund != null)
            {
                return; // Đã xử lý
            }

            var refund = new RefundRequest
            {
                RefundId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                BookingId = "SUSPENSE:" + orderCode,
                PaymentId = payment.PaymentId,
                SettlementKey = settlementKey,
                OwnerUserId = "SUSPENSE_PAYER",
                Amount = receivedAmt,
                Reason = dto.Reason.Trim(),
                Status = RefundStatus.REFUND_PENDING,
                AdminUserId = adminUserId,
                AdminNote = dto.Reason.Trim(),
                CreatedAt = nowUtc
            };
            await _context.RefundRequests.InsertOneAsync(refund);

            // Cập nhật Payment sang REFUND_PENDING
            await _context.Payments.UpdateOneAsync(
                p => p.OrderCode == orderCode,
                Builders<Payment>.Update.Set(p => p.Status, PaymentLinkStatus.REFUND_PENDING)
            );

            // Ghi sổ: DEBIT SUSPENSE, CREDIT REFUND_PENDING
            var journalEntry = new JournalEntry
            {
                EntryId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                BookingId = "SUSPENSE:" + orderCode,
                TransactionId = "TX" + nowUtc.ToString("yyMMddHHmmss") + "SRFN",
                Type = JournalType.SUSPENSE_REFUND_OWNER,
                ActorUserId = adminUserId,
                CreatedAt = nowUtc,
                IdempotencyKey = settlementKey,
                Lines = new List<JournalLine>
                {
                    new JournalLine
                    {
                        Account = LedgerAccounts.SUSPENSE,
                        Direction = LedgerDirection.DEBIT,
                        Amount = receivedAmt
                    },
                    new JournalLine
                    {
                        Account = LedgerAccounts.REFUND_PENDING,
                        Direction = LedgerDirection.CREDIT,
                        Amount = receivedAmt
                    }
                }
            };
            await _ledgerService.RecordEntryAsync(journalEntry);

            _logger.LogInformation("[AUDIT] Admin {AdminUserId} đã tạo yêu cầu hoàn tiền cho giao dịch bất thường {OrderCode} ({Amount} ₫). Lý do: {Reason}",
                adminUserId, orderCode, receivedAmt, dto.Reason);
        }

        public async Task<DecryptedAccountDto> GetDecryptedRefundAccountAsync(string adminUserId, string refundId, string? ipAddress, DateTime nowUtc)
        {
            var refund = await _context.RefundRequests.Find(r => r.RefundId == refundId).FirstOrDefaultAsync();
            if (refund == null)
            {
                throw new NotFoundException("Yêu cầu hoàn tiền không tồn tại.");
            }

            // Ghi log kiểm toán (Audit log)
            _logger.LogInformation("[AUDIT] Admin {AdminUserId} đã xem thông tin tài khoản hoàn tiền giải mã cho RefundId {RefundId} từ IP {IpAddress} lúc {NowUtc}",
                adminUserId, refundId, ipAddress, nowUtc);

            string? json = null;
            if (!string.IsNullOrEmpty(refund.EncryptedRefundAccount))
            {
                try
                {
                    json = _protector.Unprotect(refund.EncryptedRefundAccount);
                }
                catch { }
            }

            if (json == null && !string.IsNullOrEmpty(refund.PaymentId))
            {
                var pay = await _context.Payments.Find(p => p.PaymentId == refund.PaymentId).FirstOrDefaultAsync();
                if (pay != null && !string.IsNullOrEmpty(pay.EncryptedPayerInfo))
                {
                    try
                    {
                        json = _protector.Unprotect(pay.EncryptedPayerInfo);
                    }
                    catch { }
                }
            }

            if (string.IsNullOrEmpty(json))
            {
                return new DecryptedAccountDto
                {
                    BankName = "Chưa có thông tin",
                    AccountNumber = "Chưa cung cấp",
                    AccountHolderName = "Chưa cung cấp"
                };
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            return new DecryptedAccountDto
            {
                BankName = root.TryGetProperty("BankName", out var bn) ? bn.GetString() ?? "" : (root.TryGetProperty("bankName", out var bn2) ? bn2.GetString() ?? "" : ""),
                BankBin = root.TryGetProperty("BankBin", out var bb) ? bb.GetString() ?? "" : (root.TryGetProperty("bankBin", out var bb2) ? bb2.GetString() ?? "" : (root.TryGetProperty("BankId", out var bi) ? bi.GetString() ?? "" : "")),
                AccountNumber = root.TryGetProperty("AccountNumber", out var an) ? an.GetString() ?? "" : (root.TryGetProperty("accountNumber", out var an2) ? an2.GetString() ?? "" : ""),
                AccountHolderName = root.TryGetProperty("AccountName", out var ah) ? ah.GetString() ?? "" : (root.TryGetProperty("accountName", out var ah2) ? ah2.GetString() ?? "" : (root.TryGetProperty("AccountHolderName", out var ah3) ? ah3.GetString() ?? "" : ""))
            };
        }

        public async Task<SuspensePaymentsPagedResult> GetSuspensePaymentsAsync(string? status, int page, int pageSize)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);

            var filterBuilder = Builders<Payment>.Filter;
            // Giao dịch SUSPENSE: IsAbnormal == true hoặc Status == ABNORMAL
            var filter = filterBuilder.Eq(p => p.IsAbnormal, true) | filterBuilder.Eq(p => p.Status, PaymentLinkStatus.ABNORMAL);

            if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                filter &= filterBuilder.Eq(p => p.Status, status.Trim().ToUpper());
            }

            var total = await _context.Payments.CountDocumentsAsync(filter);
            var items = await _context.Payments.Find(filter)
                .SortByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            var dtos = items.Select(p => new SuspensePaymentDto
            {
                PaymentId = p.PaymentId,
                BookingId = p.BookingId,
                OrderCode = p.OrderCode,
                Amount = p.Amount,
                ReceivedAmount = p.ReceivedAmount,
                Status = p.Status,
                IsAbnormal = p.IsAbnormal,
                AbnormalReason = p.AbnormalReason,
                CreatedAt = p.CreatedAt,
                PaidAt = p.PaidAt
            }).ToList();

            return new SuspensePaymentsPagedResult
            {
                Total = total,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)total / pageSize),
                Data = dtos
            };
        }
    }
}
