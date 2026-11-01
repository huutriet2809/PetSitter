using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using PetSitter.Models;
using System;
using System.Net;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public class EmailService : IEmailService
    {
        private readonly IEmailQueue _queue;
        private readonly PetSitterContext _context;
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(
            IEmailQueue queue,
            PetSitterContext context,
            IConfiguration config,
            ILogger<EmailService> logger)
        {
            _queue = queue;
            _context = context;
            _config = config;
            _logger = logger;
        }

        public async Task QueueEmailAsync(string toEmail, string toName, string subject, string bodyHtml, string dedupeKey, string type)
        {
            if (string.IsNullOrWhiteSpace(toEmail)) return;

            // Kiểm tra DedupeKey trong MongoDB để tránh enqueue trùng
            if (!string.IsNullOrWhiteSpace(dedupeKey))
            {
                var existing = await _context.EmailLogs.Find(e => e.DedupeKey == dedupeKey && e.Status == "SENT").AnyAsync();
                if (existing)
                {
                    _logger.LogDebug("[Email] Email với DedupeKey {DedupeKey} đã gửi thành công trước đó. Bỏ qua.", dedupeKey);
                    return;
                }
            }

            var message = new EmailMessage
            {
                ToEmail = toEmail.Trim(),
                ToName = toName?.Trim() ?? string.Empty,
                Subject = subject,
                BodyHtml = bodyHtml,
                DedupeKey = dedupeKey ?? string.Empty,
                Type = type
            };

            await _queue.EnqueueAsync(message);
        }

        private string WrapBaseTemplate(string title, string contentHtml)
        {
            var appUrl = _config["AppUrl"] ?? "http://localhost:5138";
            return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"" />
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
    <title>{WebUtility.HtmlEncode(title)}</title>
</head>
<body style=""margin:0; padding:20px; background-color:#f8fafc; font-family:'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; color:#1e293b;"">
    <table role=""presentation"" style=""max-width:600px; margin:0 auto; background:#ffffff; border-radius:12px; border:1px solid #e2e8f0; overflow:hidden; box-shadow:0 4px 6px -1px rgba(0,0,0,0.05);"">
        <tr>
            <td style=""background:#3b82f6; padding:24px; text-align:center;"">
                <h1 style=""color:#ffffff; margin:0; font-size:24px; font-weight:700;"">🐾 PawStay</h1>
                <p style=""color:#dbeafe; margin:6px 0 0; font-size:14px;"">Nền tảng chăm sóc thú cưng tận tâm</p>
            </td>
        </tr>
        <tr>
            <td style=""padding:32px 24px;"">
                <h2 style=""margin-top:0; color:#0f172a; font-size:20px;"">{WebUtility.HtmlEncode(title)}</h2>
                {contentHtml}
                <div style=""margin-top:32px; padding-top:20px; border-top:1px solid #e2e8f0; text-align:center;"">
                    <a href=""{appUrl}/my-bookings.html"" style=""display:inline-block; background:#3b82f6; color:#ffffff; text-decoration:none; padding:12px 24px; border-radius:8px; font-weight:600; font-size:14px;"">Xem lịch sử đặt lịch trên PawStay</a>
                </div>
            </td>
        </tr>
        <tr>
            <td style=""background:#f1f5f9; padding:20px; text-align:center; font-size:12px; color:#64748b;"">
                <p style=""margin:0;"">Email này được gửi tự động từ hệ thống PawStay. Vui lòng không trả lời trực tiếp email này.</p>
                <p style=""margin:4px 0 0;"">Cần hỗ trợ? <a href=""{appUrl}/contact.html"" style=""color:#3b82f6; text-decoration:none;"">Liên hệ PawStay Care Team</a></p>
            </td>
        </tr>
    </table>
</body>
</html>";
        }

        public async Task SendBookingCreatedAsync(string ownerEmail, string ownerName, string sitterEmail, string sitterName, Booking booking)
        {
            var petName = WebUtility.HtmlEncode(booking.Pet?.TenPet ?? "Bé cưng");
            var bookingCode = WebUtility.HtmlEncode(booking.MaDatLich);
            var safeOwner = WebUtility.HtmlEncode(ownerName);
            var safeSitter = WebUtility.HtmlEncode(sitterName);
            var startDate = booking.NgayBatDau.ToString("dd/MM/yyyy");
            var endDate = booking.NgayKetThuc.ToString("dd/MM/yyyy");
            var amount = booking.TotalAmount > 0 ? booking.TotalAmount : (long)booking.TongTien;

            // 1. Gửi cho Chủ nuôi
            var ownerContent = $@"
                <p>Xin chào <strong>{safeOwner}</strong>,</p>
                <p>Yêu cầu đặt lịch chăm sóc cho bé <strong>{petName}</strong> của bạn đã được gửi thành công đến người chăm sóc <strong>{safeSitter}</strong>.</p>
                <div style=""background:#f8fafc; padding:16px; border-radius:8px; margin:20px 0; border-left:4px solid #3b82f6;"">
                    <p style=""margin:4px 0;""><strong>Mã đặt lịch:</strong> #{bookingCode}</p>
                    <p style=""margin:4px 0;""><strong>Thời gian chăm sóc:</strong> {startDate} đến {endDate} ({booking.SoNgay} ngày)</p>
                    <p style=""margin:4px 0;""><strong>Chi phí tạm tính:</strong> {amount:N0} ₫</p>
                    <p style=""margin:4px 0;""><strong>Trạng thái:</strong> Chờ người chăm sóc phản hồi</p>
                </div>
                <p>Người chăm sóc có tối đa 48 giờ để phản hồi. Khi người chăm sóc chấp nhận, bạn sẽ nhận được thông báo để hoàn tất thanh toán.</p>";

            await QueueEmailAsync(
                ownerEmail,
                ownerName,
                $"[PawStay] Đã gửi yêu cầu đặt lịch #{bookingCode}",
                WrapBaseTemplate("Yêu cầu đặt lịch thành công", ownerContent),
                $"{booking.BookingId}:EMAIL_CREATED:OWNER",
                "BOOKING_CREATED"
            );

            // 2. Gửi cho Sitter
            if (!string.IsNullOrEmpty(sitterEmail))
            {
                var sitterContent = $@"
                    <p>Xin chào <strong>{safeSitter}</strong>,</p>
                    <p>Bạn vừa nhận được một yêu cầu đặt lịch chăm sóc mới từ khách hàng <strong>{safeOwner}</strong>!</p>
                    <div style=""background:#f8fafc; padding:16px; border-radius:8px; margin:20px 0; border-left:4px solid #16a34a;"">
                        <p style=""margin:4px 0;""><strong>Mã đặt lịch:</strong> #{bookingCode}</p>
                        <p style=""margin:4px 0;""><strong>Thú cưng:</strong> {petName}</p>
                        <p style=""margin:4px 0;""><strong>Thời gian chăm sóc:</strong> {startDate} đến {endDate} ({booking.SoNgay} ngày)</p>
                        <p style=""margin:4px 0;""><strong>Giá trị đơn:</strong> {amount:N0} ₫</p>
                    </div>
                    <p>Vui lòng đăng nhập vào tài khoản PawStay và phản hồi chấp nhận hoặc từ chối đơn trong vòng <strong>48 giờ</strong>.</p>";

                await QueueEmailAsync(
                    sitterEmail,
                    sitterName,
                    $"[PawStay] Bạn có yêu cầu đặt lịch mới #{bookingCode}",
                    WrapBaseTemplate("Yêu cầu đặt lịch mới", sitterContent),
                    $"{booking.BookingId}:EMAIL_CREATED:SITTER",
                    "BOOKING_NEW"
                );
            }
        }

        public async Task SendPaymentSuccessAsync(string ownerEmail, string ownerName, Booking booking, long amount)
        {
            var bookingCode = WebUtility.HtmlEncode(booking.MaDatLich);
            var safeOwner = WebUtility.HtmlEncode(ownerName);
            var petName = WebUtility.HtmlEncode(booking.Pet?.TenPet ?? "Bé cưng");
            var startDate = booking.NgayBatDau.ToString("dd/MM/yyyy");
            var endDate = booking.NgayKetThuc.ToString("dd/MM/yyyy");

            var content = $@"
                <p>Xin chào <strong>{safeOwner}</strong>,</p>
                <p>PawStay đã nhận được thanh toán thành công cho đơn đặt lịch chăm sóc <strong>#{bookingCode}</strong>.</p>
                <div style=""background:#f0fdf4; padding:16px; border-radius:8px; margin:20px 0; border-left:4px solid #16a34a;"">
                    <p style=""margin:4px 0;""><strong>Số tiền thanh toán:</strong> <span style=""color:#16a34a; font-weight:700; font-size:16px;"">{amount:N0} ₫</span></p>
                    <p style=""margin:4px 0;""><strong>Thú cưng:</strong> {petName}</p>
                    <p style=""margin:4px 0;""><strong>Thời gian lưu trú:</strong> {startDate} đến {endDate}</p>
                    <p style=""margin:4px 0;""><strong>Trạng thái tiền:</strong> Được giữ an toàn tại tài khoản tạm giữ PawStay Escrow</p>
                </div>
                <p>Tiền thanh toán của bạn sẽ được giữ an toàn và chỉ giải ngân cho người chăm sóc sau khi dịch vụ kết thúc tốt đẹp và bạn xác nhận hoàn tất.</p>";

            await QueueEmailAsync(
                ownerEmail,
                ownerName,
                $"[PawStay] Xác nhận thanh toán thành công đơn #{bookingCode}",
                WrapBaseTemplate("Thanh toán thành công", content),
                $"{booking.BookingId}:EMAIL_PAYMENT_SUCCESS",
                "PAYMENT_SUCCESS"
            );
        }

        public async Task SendBookingAcceptedAsync(string ownerEmail, string ownerName, Booking booking, DateTime paymentDeadline)
        {
            var bookingCode = WebUtility.HtmlEncode(booking.MaDatLich);
            var safeOwner = WebUtility.HtmlEncode(ownerName);
            var deadlineStr = paymentDeadline.AddHours(7).ToString("HH:mm dd/MM/yyyy");
            var amount = booking.TotalAmount > 0 ? booking.TotalAmount : (long)booking.TongTien;

            var content = $@"
                <p>Xin chào <strong>{safeOwner}</strong>,</p>
                <p>Tin vui! Người chăm sóc đã <strong style=""color:#16a34a;"">chấp nhận</strong> yêu cầu đặt lịch <strong>#{bookingCode}</strong> của bạn.</p>
                <div style=""background:#fefce8; padding:16px; border-radius:8px; margin:20px 0; border-left:4px solid #ca8a04;"">
                    <p style=""margin:4px 0;""><strong>Mã đặt lịch:</strong> #{bookingCode}</p>
                    <p style=""margin:4px 0;""><strong>Số tiền cần thanh toán:</strong> {amount:N0} ₫</p>
                    <p style=""margin:4px 0;""><strong>Hạn chót thanh toán:</strong> <strong style=""color:#dc2626;"">{deadlineStr}</strong></p>
                </div>
                <p>Vui lòng đăng nhập vào PawStay và hoàn tất thanh toán trước thời hạn để xác nhận giữ chỗ dịch vụ cho bé.</p>";

            await QueueEmailAsync(
                ownerEmail,
                ownerName,
                $"[PawStay] Sitter đã chấp nhận đơn #{bookingCode} - Vui lòng thanh toán",
                WrapBaseTemplate("Đơn đặt lịch đã được chấp nhận", content),
                $"{booking.BookingId}:EMAIL_ACCEPTED",
                "BOOKING_ACCEPTED"
            );
        }

        public async Task SendBookingRejectedAsync(string ownerEmail, string ownerName, Booking booking, string reason)
        {
            var bookingCode = WebUtility.HtmlEncode(booking.MaDatLich);
            var safeOwner = WebUtility.HtmlEncode(ownerName);
            var safeReason = WebUtility.HtmlEncode(reason ?? "Người chăm sóc bận lịch đột xuất");

            var content = $@"
                <p>Xin chào <strong>{safeOwner}</strong>,</p>
                <p>Rất tiếc, người chăm sóc không thể tiếp nhận yêu cầu đặt lịch <strong>#{bookingCode}</strong> lần này.</p>
                <div style=""background:#fef2f2; padding:16px; border-radius:8px; margin:20px 0; border-left:4px solid #ef4444;"">
                    <p style=""margin:4px 0;""><strong>Lý do từ chối:</strong> {safeReason}</p>
                </div>
                <p>Bạn có thể duyệt qua danh sách các người chăm sóc uy tín khác tại khu vực của mình để tìm kiếm lịch trống phù hợp.</p>";

            await QueueEmailAsync(
                ownerEmail,
                ownerName,
                $"[PawStay] Thông báo về yêu cầu đặt lịch #{bookingCode}",
                WrapBaseTemplate("Yêu cầu đặt lịch không được chấp nhận", content),
                $"{booking.BookingId}:EMAIL_REJECTED",
                "BOOKING_REJECTED"
            );
        }

        public async Task SendBookingCompletedAsync(string ownerEmail, string ownerName, string sitterEmail, string sitterName, Booking booking, bool isAuto = false)
        {
            var bookingCode = WebUtility.HtmlEncode(booking.MaDatLich);
            var safeOwner = WebUtility.HtmlEncode(ownerName);
            var safeSitter = WebUtility.HtmlEncode(sitterName);
            var statusDesc = isAuto ? "đã được hệ thống tự động hoàn tất do hết thời hạn khiếu nại" : "đã được xác nhận hoàn tất thành công";

            // Cho Chủ nuôi
            var ownerContent = $@"
                <p>Xin chào <strong>{safeOwner}</strong>,</p>
                <p>Chuyến đi <strong>#{bookingCode}</strong> {statusDesc}. Cảm ơn bạn đã tin tưởng dịch vụ của PawStay!</p>
                <p>Đánh giá của bạn sẽ giúp cộng đồng yêu thú cưng có thêm thông tin tham khảo hữu ích. Đừng quên dành 1 phút để để lại đánh giá cho người chăm sóc <strong>{safeSitter}</strong> nhé.</p>";

            await QueueEmailAsync(
                ownerEmail,
                ownerName,
                $"[PawStay] Chuyến đi #{bookingCode} đã hoàn tất",
                WrapBaseTemplate("Chuyến đi hoàn tất", ownerContent),
                $"{booking.BookingId}:EMAIL_COMPLETED:OWNER",
                "BOOKING_COMPLETED"
            );

            // Cho Sitter
            if (!string.IsNullOrEmpty(sitterEmail))
            {
                var sitterContent = $@"
                    <p>Xin chào <strong>{safeSitter}</strong>,</p>
                    <p>Đơn đặt lịch <strong>#{bookingCode}</strong> {statusDesc}.</p>
                    <p>Thu nhập từ chuyến đi đã được chuyển vào số dư chờ khả dụng trên Ví PawStay của bạn và sẽ khả dụng sau 48 giờ theo chính sách giữ an toàn.</p>";

                await QueueEmailAsync(
                    sitterEmail,
                    sitterName,
                    $"[PawStay] Đơn #{bookingCode} đã hoàn tất - Thu nhập đã ghi nhận",
                    WrapBaseTemplate("Chuyến đi hoàn tất", sitterContent),
                    $"{booking.BookingId}:EMAIL_COMPLETED:SITTER",
                    "BOOKING_COMPLETED"
                );
            }
        }

        public async Task SendRefundNoticeAsync(string ownerEmail, string ownerName, Booking booking, long refundAmount, string reason)
        {
            var bookingCode = WebUtility.HtmlEncode(booking.MaDatLich);
            var safeOwner = WebUtility.HtmlEncode(ownerName);
            var safeReason = WebUtility.HtmlEncode(reason ?? "Hủy chuyến theo chính sách dịch vụ");

            var content = $@"
                <p>Xin chào <strong>{safeOwner}</strong>,</p>
                <p>Yêu cầu hoàn tiền cho đơn đặt lịch <strong>#{bookingCode}</strong> đã được ghi nhận.</p>
                <div style=""background:#f8fafc; padding:16px; border-radius:8px; margin:20px 0; border-left:4px solid #ea580c;"">
                    <p style=""margin:4px 0;""><strong>Số tiền hoàn trả:</strong> <strong style=""color:#ea580c; font-size:16px;"">{refundAmount:N0} ₫</strong></p>
                    <p style=""margin:4px 0;""><strong>Lý do:</strong> {safeReason}</p>
                    <p style=""margin:4px 0;""><strong>Trạng thái:</strong> Đang xử lý hoàn tiền về tài khoản ngân hàng của bạn</p>
                </div>
                <p>Bộ phận kế toán PawStay sẽ xử lý lệnh chuyển tiền hoàn trong vòng 1-3 ngày làm việc.</p>";

            await QueueEmailAsync(
                ownerEmail,
                ownerName,
                $"[PawStay] Thông báo hoàn tiền cho đơn #{bookingCode}",
                WrapBaseTemplate("Xử lý hoàn tiền", content),
                $"{booking.BookingId}:EMAIL_REFUND:{refundAmount}",
                "REFUND_NOTICE"
            );
        }

        public async Task SendOverdueReminderAsync(string sitterEmail, string sitterName, Booking booking, int daysOverdue)
        {
            var bookingCode = WebUtility.HtmlEncode(booking.MaDatLich);
            var safeSitter = WebUtility.HtmlEncode(sitterName);
            var endDate = booking.NgayKetThuc.ToString("dd/MM/yyyy");

            var content = $@"
                <p>Xin chào <strong>{safeSitter}</strong>,</p>
                <p>Đơn đặt lịch <strong>#{bookingCode}</strong> đã qua ngày kết thúc ({endDate}) được <strong>{daysOverdue} ngày</strong> nhưng bạn chưa bấm xác nhận hoàn tất trên hệ thống.</p>
                <p>Vui lòng đăng nhập vào ứng dụng và bấm <strong>""Hoàn tất""</strong> để chủ nuôi có thể xác nhận bàn giao thú cưng và giải ngân thu nhập về ví cho bạn.</p>";

            await QueueEmailAsync(
                sitterEmail,
                sitterName,
                $"[PawStay] Nhắc nhở: Xác nhận hoàn tất đơn #{bookingCode}",
                WrapBaseTemplate("Nhắc nhở hoàn tất dịch vụ", content),
                $"{booking.BookingId}:EMAIL_OVERDUE_T{daysOverdue}",
                "REMINDER_OVERDUE"
            );
        }

        public async Task SendAccountStatusNoticeAsync(string userEmail, string userName, bool isActive)
        {
            var safeName = WebUtility.HtmlEncode(userName);
            string title;
            string content;

            if (isActive)
            {
                title = "Tài khoản của bạn đã được mở khóa";
                content = $@"
                    <p>Xin chào <strong>{safeName}</strong>,</p>
                    <p>Tài khoản PawStay của bạn đã được Quản trị viên <strong style=""color:#16a34a;"">mở khóa hoạt động</strong>.</p>
                    <p>Bạn hiện có thể đăng nhập và tiếp tục sử dụng tất cả dịch vụ của PawStay như bình thường.</p>";
            }
            else
            {
                title = "Thông báo tạm ngừng hoạt động tài khoản";
                content = $@"
                    <p>Xin chào <strong>{safeName}</strong>,</p>
                    <p>Tài khoản PawStay của bạn đã bị <strong style=""color:#ef4444;"">vô hiệu hóa</strong> do vi phạm điều khoản dịch vụ hoặc theo quyết định của Quản trị viên.</p>
                    <p>Nếu bạn cho rằng đây là sự nhầm lẫn, vui lòng liên hệ bộ phận hỗ trợ PawStay để được hỗ trợ giải quyết.</p>";
            }

            var nowStr = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            await QueueEmailAsync(
                userEmail,
                userName,
                $"[PawStay] {title}",
                WrapBaseTemplate(title, content),
                $"{userEmail}:ACCOUNT_STATUS_{isActive}_{nowStr}",
                "ACCOUNT_STATUS"
            );
        }
    }
}

