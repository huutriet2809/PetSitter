using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Models;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PetSitter.Controllers
{
    [Route("api/admin")]
    [ApiController]
    [Authorize(Roles = "ADMIN")]
    public class AdminController : ControllerBase
    {
        private readonly PetSitterContext _context;

        public AdminController(PetSitterContext context)
        {
            _context = context;
        }

        [HttpGet("sitter-applications")]
        public async Task<IActionResult> GetApplications([FromQuery] string status = "Pending")
        {
            var profiles = await _context.SitterProfiles.Find(s => s.Status == status).ToListAsync();
            var dtos = new List<SitterApplicationDto>();

            foreach (var profile in profiles)
            {
                var user = await _context.Users.Find(u => u.UserId == profile.UserId).FirstOrDefaultAsync();
                var sitterService = await _context.SitterServices.Find(ss => ss.SitterId == profile.SitterId).FirstOrDefaultAsync();

                dtos.Add(new SitterApplicationDto
                {
                    Id = profile.SitterId,
                    UserId = profile.UserId,
                    FullName = user?.FullName ?? "",
                    Bio = profile.Bio ?? "",
                    KhuVuc = profile.Address ?? "",
                    KinhNghiem = profile.ExperienceYears,
                    Tags = profile.Tags,
                    LoaiPetNhan = profile.LoaiPetNhan,
                    GiaMoiNgay = sitterService?.Price ?? 0,
                    Status = profile.Status,
                    LyDoTuChoi = profile.LyDoTuChoi,
                    CreatedAt = profile.CreatedAt
                });
            }

            return Ok(dtos);
        }

        [HttpPatch("sitter-applications/{id}/approve")]
        public async Task<IActionResult> ApproveApplication(string id)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var profile = await _context.SitterProfiles.Find(s => s.SitterId == id).FirstOrDefaultAsync();
            
            if (profile == null) return NotFound(new { message = "Không tìm thấy hồ sơ." });
            if (profile.UserId == currentUserId) return BadRequest(new { message = "Không thể tự duyệt hồ sơ của chính mình." });

            var filter = Builders<SitterProfile>.Filter.Eq(s => s.SitterId, id);
            var update = Builders<SitterProfile>.Update
                .Set(s => s.Status, "Approved")
                .Set(s => s.IsVerified, true)
                .Set(s => s.ReviewedAt, DateTime.UtcNow);
            
            await _context.SitterProfiles.UpdateOneAsync(filter, update);

            var userFilter = Builders<User>.Filter.Eq(u => u.UserId, profile.UserId);
            var userUpdate = Builders<User>.Update.Set(u => u.Role, "SITTER");
            await _context.Users.UpdateOneAsync(userFilter, userUpdate);

            // Create default availabilities (60 days) if not exist
            var hasAvailability = await _context.SitterAvailabilities.Find(a => a.SitterId == id).AnyAsync();
            if (!hasAvailability)
            {
                var availabilities = new List<SitterAvailability>();
                var today = DateTime.UtcNow.AddHours(7).Date;
                for (int i = 0; i < 60; i++)
                {
                    availabilities.Add(new SitterAvailability
                    {
                        AvailabilityId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                        SitterId = id,
                        AvailableDate = today.AddDays(i),
                        StartTime = new TimeSpan(7, 0, 0),
                        EndTime = new TimeSpan(20, 0, 0),
                        IsAvailable = true
                    });
                }
                await _context.SitterAvailabilities.InsertManyAsync(availabilities);
            }

            return Ok(new { message = "Đã duyệt hồ sơ." });
        }

        [HttpPatch("sitter-applications/{id}/reject")]
        public async Task<IActionResult> RejectApplication(string id, [FromBody] RejectApplicationDto dto)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var profile = await _context.SitterProfiles.Find(s => s.SitterId == id).FirstOrDefaultAsync();
            
            if (profile == null) return NotFound(new { message = "Không tìm thấy hồ sơ." });
            if (profile.UserId == currentUserId) return BadRequest(new { message = "Không thể tự từ chối hồ sơ của chính mình." });

            var filter = Builders<SitterProfile>.Filter.Eq(s => s.SitterId, id);
            var update = Builders<SitterProfile>.Update
                .Set(s => s.Status, "Rejected")
                .Set(s => s.IsVerified, false)
                .Set(s => s.LyDoTuChoi, dto.LyDo)
                .Set(s => s.ReviewedAt, DateTime.UtcNow);
            
            await _context.SitterProfiles.UpdateOneAsync(filter, update);

            return Ok(new { message = "Đã từ chối hồ sơ." });
        }
    }
}
