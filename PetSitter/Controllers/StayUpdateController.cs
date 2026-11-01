using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PetSitter.Controllers
{
    [ApiController]
    public class StayUpdateController : ControllerBase
    {
        private readonly PetSitterContext _context;
        private readonly IConfiguration _config;

        public StayUpdateController(PetSitterContext context, IConfiguration config)
        {
            _context = context;
            _config = config;
        }

        [HttpPost("api/sitter/bookings/{id}/updates")]
        [Authorize(Roles = "SITTER")]
        [RequestSizeLimit(8388608)] // 8MB
        [RequestFormLimits(MultipartBodyLengthLimit = 8388608)]
        public async Task<IActionResult> CreateUpdate(string id, [FromForm] string? note, [FromForm] List<IFormFile> photos)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitterProfile == null) return NotFound(new { message = "Không tìm thấy hồ sơ Sitter." });

            var booking = await _context.Bookings.Find(b => b.BookingId == id).FirstOrDefaultAsync();
            if (booking == null) return NotFound(new { message = "Không tìm thấy booking." });

            if (booking.SitterId != sitterProfile.SitterId)
                return NotFound(new { message = "Không có quyền với booking này." });

            if (booking.TrangThai != BookingStatus.ACCEPTED)
                return BadRequest(new { message = "Booking không ở trạng thái ACCEPTED." });

            bool requireDates = _config.GetValue<bool>("StayUpdates:RequireStayDates", false);
            if (requireDates)
            {
                var now = DateTime.UtcNow.AddHours(7).Date;
                if (now < booking.NgayBatDau.Date || now > booking.NgayKetThuc.Date)
                    return BadRequest(new { message = "Chỉ được đăng trong khoảng thời gian lưu trú." });
            }

            if (string.IsNullOrWhiteSpace(note) && (photos == null || photos.Count == 0))
                return BadRequest(new { message = "Cần có ghi chú hoặc ít nhất một ảnh." });

            if (photos != null && photos.Count > 3)
                return BadRequest(new { message = "Tối đa 3 ảnh." });

            if (!string.IsNullOrWhiteSpace(note) && note.Length > 500)
                return BadRequest(new { message = "Ghi chú tối đa 500 ký tự." });

            int maxUpdates = _config.GetValue<int>("StayUpdates:MaxPerBooking", 30);
            var count = await _context.StayUpdates.CountDocumentsAsync(u => u.BookingId == id);
            if (count >= maxUpdates)
                return Conflict(new { message = "Đã vượt quá số lượng cập nhật tối đa cho chuyến đi này." });

            int maxBytes = _config.GetValue<int>("StayUpdates:MaxPhotoBytes", 2097152);
            var photoPaths = new List<string>();

            if (photos != null && photos.Count > 0)
            {
                var uploadDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "stay", id);
                if (!Directory.Exists(uploadDir))
                    Directory.CreateDirectory(uploadDir);

                foreach (var photo in photos)
                {
                    if (photo.Length > maxBytes)
                        return BadRequest(new { message = "Ảnh vượt quá dung lượng cho phép." });

                    using var stream = photo.OpenReadStream();
                    var buffer = new byte[12];
                    int bytesRead = stream.Read(buffer, 0, 12);
                    if (bytesRead < 12) return BadRequest(new { message = "File quá nhỏ." });
                    
                    stream.Position = 0; // Reset position for saving

                    var hex = BitConverter.ToString(buffer).Replace("-", "");
                    
                    string realExt = "";
                    if (hex.StartsWith("FFD8FF")) realExt = ".jpg";
                    else if (hex.StartsWith("89504E470D0A1A0A")) realExt = ".png";
                    else if (hex.StartsWith("52494646") && hex.Substring(16, 8) == "57454250") realExt = ".webp"; // RIFF...WEBP
                    
                    if (string.IsNullOrEmpty(realExt)) 
                        return BadRequest(new { message = "File không đúng định dạng ảnh (JPG/PNG/WEBP)." });

                    var fileName = Guid.NewGuid().ToString() + realExt;
                    var filePath = Path.Combine(uploadDir, fileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await photo.CopyToAsync(fileStream);
                    }
                    
                    photoPaths.Add($"/uploads/stay/{id}/{fileName}");
                }
            }

            var update = new StayUpdate
            {
                BookingId = id,
                SitterUserId = sitterProfile.UserId,
                Note = note,
                PhotoPaths = photoPaths,
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                await _context.StayUpdates.InsertOneAsync(update);
            }
            catch (Exception)
            {
                foreach (var path in photoPaths)
                {
                    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", path.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }
                throw;
            }

            return Ok(new { message = "Đã đăng cập nhật." });
        }

        [HttpDelete("api/sitter/bookings/{id}/updates/{updateId}")]
        [Authorize(Roles = "SITTER")]
        public async Task<IActionResult> DeleteUpdate(string id, string updateId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (sitterProfile == null) return NotFound(new { message = "Không tìm thấy hồ sơ Sitter." });

            var update = await _context.StayUpdates.Find(u => u.Id == updateId && u.BookingId == id).FirstOrDefaultAsync();
            if (update == null) return NotFound(new { message = "Không tìm thấy cập nhật." });

            if (update.SitterUserId != sitterProfile.UserId)
                return NotFound(new { message = "Bài đăng không phải của bạn." });

            if ((DateTime.UtcNow - update.CreatedAt).TotalHours > 24)
                return Conflict(new { message = "Chỉ có thể xóa bài đăng trong vòng 24 giờ." });

            // Xóa DB trước
            var result = await _context.StayUpdates.DeleteOneAsync(u => u.Id == updateId);
            if (result.DeletedCount > 0)
            {
                foreach (var path in update.PhotoPaths)
                {
                    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", path.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
                }
            }

            return Ok(new { message = "Đã xóa cập nhật." });
        }

        [HttpGet("api/bookings/{id}/updates")]
        [Authorize]
        public async Task<IActionResult> GetUpdates(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = User.FindFirstValue(ClaimTypes.Role);

            var booking = await _context.Bookings.Find(b => b.BookingId == id).FirstOrDefaultAsync();
            if (booking == null) return NotFound(new { message = "Không tìm thấy booking." });

            bool isOwner = booking.OwnerId == userId;
            bool isAdmin = role == "ADMIN";
            bool isSitter = false;
            
            if (role == "SITTER")
            {
                var sitterProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
                if (sitterProfile != null && booking.SitterId == sitterProfile.SitterId) isSitter = true;
            }

            if (!isOwner && !isSitter && !isAdmin)
                return NotFound(new { message = "Không có quyền xem." });

            var updates = await _context.StayUpdates.Find(u => u.BookingId == id)
                                        .SortByDescending(u => u.CreatedAt)
                                        .ToListAsync();

            var dtos = updates.Select(u => new StayUpdateDto
            {
                Id = u.Id,
                BookingId = u.BookingId,
                Note = u.Note,
                PhotoPaths = u.PhotoPaths,
                CreatedAt = u.CreatedAt,
                CanDelete = (u.SitterUserId == userId && (DateTime.UtcNow - u.CreatedAt).TotalHours <= 24)
            });

            return Ok(dtos);
        }
    }
}