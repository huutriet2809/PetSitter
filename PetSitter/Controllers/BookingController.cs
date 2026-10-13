using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Services;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/bookings")]
    [Authorize]
    public class BookingController : ControllerBase
    {
        private readonly IBookingService _bookingService;

        public BookingController(IBookingService bookingService)
        {
            _bookingService = bookingService;
        }

        [HttpPost]
        [EnableRateLimiting("booking-review")]
        public async Task<IActionResult> CreateBooking([FromBody] CreateBookingDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var result = await _bookingService.CreateBookingAsync(userId, dto);
                return Ok(result);
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpGet("my")]
        public async Task<IActionResult> GetMyBookings()
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var result = await _bookingService.GetMyBookingsAsync(userId);
                return Ok(result);
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetBooking(string id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var result = await _bookingService.GetBookingByIdAsync(userId, id);
                return Ok(result);
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("{id}/cancel")]
        public async Task<IActionResult> CancelBooking(string id, [FromBody] CancelBookingRequestDto? dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                await _bookingService.CancelBookingAsync(userId, id, dto?.LyDo, dto?.ExpectedStatus);
                return Ok(new { message = "Đã hủy booking thành công" });
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPatch("{id}/confirm-completion")]
        [EnableRateLimiting("booking-review")]
        public async Task<IActionResult> ConfirmCompletion(string id)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var result = await _bookingService.OwnerConfirmCompletionAsync(userId, id);
                return Ok(new { message = "Đã xác nhận hoàn tất dịch vụ thành công.", booking = result });
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPatch("{id}/dispute")]
        [EnableRateLimiting("booking-review")]
        public async Task<IActionResult> DisputeBooking(string id, [FromBody] DisputeBookingDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                if (string.IsNullOrWhiteSpace(dto?.LyDo))
                    return BadRequest(new { message = "Lý do khiếu nại là bắt buộc." });

                var result = await _bookingService.OwnerDisputeBookingAsync(userId, id, dto.LyDo);
                return Ok(new { message = "Đã gửi khiếu nại thành công, vui lòng chờ Admin xử lý.", booking = result });
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("{id}/review")]
        [EnableRateLimiting("booking-review")]
        public async Task<IActionResult> AddReview(string id, [FromBody] CreateReviewDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var review = await _bookingService.AddReviewAsync(userId, id, dto);
                return Ok(review);
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }
    }
}
