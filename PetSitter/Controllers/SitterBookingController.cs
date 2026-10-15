using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Models;
using PetSitter.Services;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PetSitter.Controllers
{
    [Route("api/sitter/bookings")]
    [ApiController]
    [Authorize(Roles = "SITTER")]
    public class SitterBookingController : ControllerBase
    {
        private readonly PetSitterContext _context;
        private readonly IBookingService _bookingService;
        private readonly INotificationService _notificationService;
        private readonly IPaymentService? _paymentService;
        private readonly ILogger<SitterBookingController> _logger;

        public SitterBookingController(
            PetSitterContext context,
            IBookingService bookingService,
            INotificationService notificationService,
            ILogger<SitterBookingController> logger,
            IPaymentService? paymentService = null)
        {
            _context = context;
            _bookingService = bookingService;
            _notificationService = notificationService;
            _paymentService = paymentService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetBookings([FromQuery] string? status)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitterProfile == null) return NotFound(new { message = "Không tìm thấy hồ sơ Sitter." });

            // Hết hạn lười cho các đơn của Sitter này (cả PENDING lẫn COMPLETION_PENDING)
            await _bookingService.ProcessLazyBookingUpdatesAsync(ownerId: null, sitterId: sitterProfile.SitterId);

            var filterBuilder = Builders<Booking>.Filter;
            var filter = filterBuilder.Eq(b => b.SitterId, sitterProfile.SitterId);
            
            if (!string.IsNullOrEmpty(status))
            {
                filter &= filterBuilder.Eq(b => b.TrangThai, status.ToUpper());
            }

            var bookings = await _context.Bookings.Find(filter).SortByDescending(b => b.CreatedAt).ToListAsync();
            
            var dtos = new List<BookingDetailDto>();
            foreach(var booking in bookings)
            {
                dtos.Add(await _bookingService.GetBookingByIdAsync(userId ?? string.Empty, booking.BookingId));
            }

            return Ok(dtos);
        }

        [HttpPatch("{id}/accept")]
        public async Task<IActionResult> AcceptBooking(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitterProfile == null) return NotFound(new { message = "Không tìm thấy hồ sơ Sitter." });

            var nowUtc = DateTime.UtcNow;
            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, id)
                       & Builders<Booking>.Filter.Eq(b => b.SitterId, sitterProfile.SitterId)
                       & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.PENDING)
                       & Builders<Booking>.Filter.Gt(b => b.ExpiresAt, nowUtc);

            var windowHours = 24;
            var paymentDeadline = nowUtc.AddHours(windowHours);

            var update = Builders<Booking>.Update
                .Set(b => b.TrangThai, BookingStatus.ACCEPTED)
                .Set(b => b.PaymentStatus, PaymentStatus.PENDING_PAYMENT)
                .Set(b => b.PaymentDeadline, paymentDeadline)
                .Set(b => b.IsActive, true)
                .Push(b => b.History, new InquiryHistory
                {
                    HanhDong = BookingStatus.ACCEPTED,
                    FromStatus = BookingStatus.PENDING,
                    NguoiThucHien = "SITTER",
                    ActorUserId = userId,
                    LyDo = null,
                    ThoiGian = nowUtc
                });

            var updatedBooking = await _context.Bookings.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After });

            if (updatedBooking == null)
            {
                var existing = await _context.Bookings.Find(b => b.BookingId == id).FirstOrDefaultAsync();
                if (existing == null || existing.SitterId != sitterProfile.SitterId)
                    return NotFound(new { message = "Không tìm thấy booking." });

                if (existing.TrangThai == BookingStatus.PENDING && (existing.ExpiresAt == null || existing.ExpiresAt <= nowUtc))
                {
                    // Lật sang EXPIRED nguyên tử
                    var expireFilter = Builders<Booking>.Filter.Eq(b => b.BookingId, id)
                                     & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.PENDING);

                    var expireUpdate = Builders<Booking>.Update
                        .Set(b => b.TrangThai, BookingStatus.EXPIRED)
                        .Set(b => b.IsActive, false)
                        .Push(b => b.History, new InquiryHistory
                        {
                            HanhDong = BookingStatus.EXPIRED,
                            FromStatus = BookingStatus.PENDING,
                            NguoiThucHien = "SYSTEM",
                            ActorUserId = null,
                            LyDo = "Quá hạn 48 giờ không được chấp nhận",
                            ThoiGian = nowUtc
                        });

                    await _context.Bookings.FindOneAndUpdateAsync(expireFilter, expireUpdate);
                    return Conflict(new { message = "Yêu cầu đặt lịch đã hết hạn phản hồi (48 giờ)." });
                }

                return Conflict(new { message = "Không thể chấp nhận booking. Trạng thái đã thay đổi hoặc không hợp lệ." });
            }

            // Best-effort tạo link thanh toán, lỗi cổng không rollback accept (Requirement 6)
            if (_paymentService != null)
            {
                try
                {
                    await _paymentService.CreateOrGetPaymentLinkAsync(updatedBooking.OwnerId, updatedBooking.BookingId, nowUtc);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Không thể tạo trước link PayOS cho đơn {BookingId} lúc Accept: {Message}", updatedBooking.BookingId, ex.Message);
                }
            }

            if (_notificationService != null && !string.IsNullOrEmpty(updatedBooking.OwnerId))
            {
                try
                {
                    var dedupeKey = $"{updatedBooking.BookingId}:{NotificationType.BOOKING_ACCEPTED}:{updatedBooking.OwnerId}";
                    await _notificationService.CreateNotificationAsync(
                        updatedBooking.OwnerId,
                        NotificationType.BOOKING_ACCEPTED,
                        updatedBooking.BookingId,
                        "Yêu cầu đặt lịch đã được chấp nhận",
                        $"Người chăm sóc đã chấp nhận yêu cầu đặt lịch #{updatedBooking.MaDatLich}. Vui lòng thanh toán trong vòng {windowHours} giờ.",
                        dedupeKey,
                        targetRole: "OWNER"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lỗi khi gửi thông báo BOOKING_ACCEPTED cho chủ nuôi {OwnerId}", updatedBooking.OwnerId);
                }
            }

            // Kiểm tra trùng ngày với đơn ACCEPTED khác (không chặn, chỉ cảnh báo)
            var hasOverlap = await _context.Bookings.Find(b =>
                b.SitterId == sitterProfile.SitterId &&
                b.BookingId != id &&
                b.TrangThai == BookingStatus.ACCEPTED &&
                b.NgayBatDau <= updatedBooking.NgayKetThuc &&
                b.NgayKetThuc >= updatedBooking.NgayBatDau
            ).AnyAsync();

            if (hasOverlap)
            {
                return Ok(new
                {
                    message = "Đã chấp nhận booking.",
                    warning = "Cảnh báo: Bạn đang có đơn đã chấp nhận khác trùng ngày với đơn này."
                });
            }

            return Ok(new { message = "Đã chấp nhận booking." });
        }

        [HttpPatch("{id}/reject")]
        public async Task<IActionResult> RejectBooking(string id, [FromBody] RejectBookingDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitterProfile == null) return NotFound(new { message = "Không tìm thấy hồ sơ Sitter." });

            if (string.IsNullOrWhiteSpace(dto?.LyDo))
                return BadRequest(new { message = "Lý do từ chối là bắt buộc." });

            if (dto.LyDo.Trim().Length > 500)
                return BadRequest(new { message = "Lý do tối đa 500 ký tự." });

            var nowUtc = DateTime.UtcNow;
            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, id)
                       & Builders<Booking>.Filter.Eq(b => b.SitterId, sitterProfile.SitterId)
                       & Builders<Booking>.Filter.Eq(b => b.TrangThai, BookingStatus.PENDING);

            var update = Builders<Booking>.Update
                .Set(b => b.TrangThai, BookingStatus.REJECTED)
                .Set(b => b.IsActive, false)
                .Set(b => b.LyDoTuChoi, dto.LyDo.Trim())
                .Push(b => b.History, new InquiryHistory
                {
                    HanhDong = BookingStatus.REJECTED,
                    FromStatus = BookingStatus.PENDING,
                    NguoiThucHien = "SITTER",
                    ActorUserId = userId,
                    LyDo = dto.LyDo.Trim(),
                    ThoiGian = nowUtc
                });

            var result = await _context.Bookings.FindOneAndUpdateAsync(filter, update, new FindOneAndUpdateOptions<Booking> { ReturnDocument = ReturnDocument.After });

            if (result == null)
            {
                var existing = await _context.Bookings.Find(b => b.BookingId == id).FirstOrDefaultAsync();
                if (existing == null || existing.SitterId != sitterProfile.SitterId)
                    return NotFound(new { message = "Không tìm thấy booking." });

                return Conflict(new { message = "Không thể từ chối booking. Trạng thái đã thay đổi." });
            }

            if (_notificationService != null && !string.IsNullOrEmpty(result.OwnerId))
            {
                try
                {
                    var dedupeKey = $"{result.BookingId}:{NotificationType.BOOKING_REJECTED}:{result.OwnerId}";
                    await _notificationService.CreateNotificationAsync(
                        result.OwnerId,
                        NotificationType.BOOKING_REJECTED,
                        result.BookingId,
                        "Yêu cầu đặt lịch bị từ chối",
                        $"Người chăm sóc đã từ chối yêu cầu đặt lịch #{result.MaDatLich}.",
                        dedupeKey,
                        targetRole: "OWNER"
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lỗi khi gửi thông báo BOOKING_REJECTED cho chủ nuôi {OwnerId}", result.OwnerId);
                }
            }

            return Ok(new { message = "Đã từ chối booking." });
        }

        [HttpPatch("{id}/cancel")]
        public async Task<IActionResult> CancelBooking(string id, [FromBody] CancelBookingRequestDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(dto?.LyDo))
                return BadRequest(new { message = "Lý do hủy đơn là bắt buộc đối với người chăm sóc." });

            await _bookingService.CancelBookingAsync(userId, id, dto.LyDo);
            return Ok(new { message = "Đã hủy đơn đặt lịch thành công." });
        }

        [HttpPatch("{id}/complete")]
        [EnableRateLimiting("booking-review")]
        public async Task<IActionResult> CompleteBooking(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await _bookingService.SitterCompleteBookingAsync(userId, id);
            return Ok(new { message = "Đã đánh dấu hoàn tất booking, đang chờ chủ nuôi xác nhận.", booking = result });
        }
    }
}
