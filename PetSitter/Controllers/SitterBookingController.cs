using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Models;
using PetSitter.Services;
using System;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace PetSitter.Controllers
{
    [Route("api/sitter/bookings")]
    [ApiController]
    [Authorize(Roles = "SITTER")]
    public class SitterBookingController : ControllerBase
    {
        private readonly PetSitterContext _context;
        private readonly IBookingService _bookingService;

        public SitterBookingController(PetSitterContext context, IBookingService bookingService)
        {
            _context = context;
            _bookingService = bookingService;
        }

        [HttpGet]
        public async Task<IActionResult> GetBookings([FromQuery] string? status)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitterProfile == null) return NotFound(new { message = "Không tìm thấy hồ sơ Sitter." });

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
                // We map them fully using GetBookingByIdAsync or internal logic.
                dtos.Add(await _bookingService.GetBookingByIdAsync(userId, booking.BookingId));
            }

            return Ok(dtos);
        }

        [HttpPatch("{id}/accept")]
        public async Task<IActionResult> AcceptBooking(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitterProfile == null) return NotFound(new { message = "Không tìm thấy hồ sơ Sitter." });

            var booking = await _context.Bookings.Find(b => b.BookingId == id).FirstOrDefaultAsync();
            if (booking == null || booking.SitterId != sitterProfile.SitterId)
                return NotFound(new { message = "Không tìm thấy booking." });

            if (booking.TrangThai != "PENDING")
                return BadRequest(new { message = "Booking không ở trạng thái chờ xác nhận." });

            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, id) & Builders<Booking>.Filter.Eq(b => b.TrangThai, "PENDING");
            var update = Builders<Booking>.Update.Set(b => b.TrangThai, "CONFIRMED");
            var result = await _context.Bookings.UpdateOneAsync(filter, update);

            if (result.ModifiedCount == 0)
                return BadRequest(new { message = "Không thể chấp nhận booking. Trạng thái đã thay đổi." });

            return Ok(new { message = "Đã chấp nhận booking." });
        }

        [HttpPatch("{id}/reject")]
        public async Task<IActionResult> RejectBooking(string id, [FromBody] RejectBookingDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitterProfile == null) return NotFound(new { message = "Không tìm thấy hồ sơ Sitter." });

            var booking = await _context.Bookings.Find(b => b.BookingId == id).FirstOrDefaultAsync();
            if (booking == null || booking.SitterId != sitterProfile.SitterId)
                return NotFound(new { message = "Không tìm thấy booking." });

            if (booking.TrangThai != "PENDING")
                return BadRequest(new { message = "Booking không ở trạng thái chờ xác nhận." });

            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, id) & Builders<Booking>.Filter.Eq(b => b.TrangThai, "PENDING");
            var update = Builders<Booking>.Update.Set(b => b.TrangThai, "REJECTED").Set(b => b.LyDoTuChoi, dto.LyDo);
            var result = await _context.Bookings.UpdateOneAsync(filter, update);

            if (result.ModifiedCount == 0)
                return BadRequest(new { message = "Không thể từ chối booking. Trạng thái đã thay đổi." });

            // Free the booked dates
            var deleteFilter = Builders<BookedDate>.Filter.Eq(bd => bd.BookingId, booking.BookingId);
            await _context.BookedDates.DeleteManyAsync(deleteFilter);

            return Ok(new { message = "Đã từ chối booking." });
        }

        [HttpPatch("{id}/complete")]
        public async Task<IActionResult> CompleteBooking(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitterProfile == null) return NotFound(new { message = "Không tìm thấy hồ sơ Sitter." });

            var booking = await _context.Bookings.Find(b => b.BookingId == id).FirstOrDefaultAsync();
            if (booking == null || booking.SitterId != sitterProfile.SitterId)
                return NotFound(new { message = "Không tìm thấy booking." });

            if (booking.TrangThai != "CONFIRMED")
                return BadRequest(new { message = "Booking không ở trạng thái đã xác nhận." });

            var currentVnTime = DateTime.UtcNow.AddHours(7).Date;
            if (currentVnTime <= booking.NgayKetThuc.Date)
                return BadRequest(new { message = "Chưa thể hoàn thành booking do chưa qua ngày kết thúc." });

            var filter = Builders<Booking>.Filter.Eq(b => b.BookingId, id) & Builders<Booking>.Filter.Eq(b => b.TrangThai, "CONFIRMED");
            var update = Builders<Booking>.Update.Set(b => b.TrangThai, "COMPLETED");
            var result = await _context.Bookings.UpdateOneAsync(filter, update);

            if (result.ModifiedCount == 0)
                return BadRequest(new { message = "Không thể hoàn thành booking. Trạng thái đã thay đổi." });

            return Ok(new { message = "Đã hoàn thành booking." });
        }
    }
}
