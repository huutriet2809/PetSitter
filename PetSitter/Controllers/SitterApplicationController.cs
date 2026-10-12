using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using PetSitter.DTOs;
using PetSitter.Models;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PetSitter.Controllers
{
    [Route("api/sitter-applications")]
    [ApiController]
    [Authorize]
    public class SitterApplicationController : ControllerBase
    {
        private readonly PetSitterContext _context;

        public SitterApplicationController(PetSitterContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Apply([FromBody] CreateSitterApplicationDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var existingProfile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (existingProfile != null && (existingProfile.Status == "Pending" || existingProfile.Status == "Approved"))
            {
                return BadRequest(new { message = "Bạn đã có hồ sơ đang chờ duyệt hoặc đã được duyệt." });
            }

            var sitterId = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

            var profile = new SitterProfile
            {
                SitterId = sitterId,
                UserId = userId,
                Bio = dto.Bio,
                Address = dto.KhuVuc, // KhuVuc maps to Address
                ExperienceYears = dto.KinhNghiem,
                Tags = dto.Tags,
                LoaiPetNhan = dto.LoaiPetNhan,
                Status = "Pending",
                IsVerified = false,
                CreatedAt = DateTime.UtcNow
            };

            if (existingProfile != null && existingProfile.Status == "Rejected")
            {
                // Re-apply logic: update the existing profile instead of creating new one
                profile.SitterId = existingProfile.SitterId;
                var filter = Builders<SitterProfile>.Filter.Eq(s => s.SitterId, existingProfile.SitterId);
                var update = Builders<SitterProfile>.Update
                    .Set(s => s.Bio, profile.Bio)
                    .Set(s => s.Address, profile.Address)
                    .Set(s => s.ExperienceYears, profile.ExperienceYears)
                    .Set(s => s.Tags, profile.Tags)
                    .Set(s => s.LoaiPetNhan, profile.LoaiPetNhan)
                    .Set(s => s.Status, "Pending")
                    .Set(s => s.CreatedAt, DateTime.UtcNow)
                    .Set(s => s.LyDoTuChoi, null);
                await _context.SitterProfiles.UpdateOneAsync(filter, update);
                sitterId = existingProfile.SitterId;
            }
            else
            {
                await _context.SitterProfiles.InsertOneAsync(profile);
            }

            // Create a default base service for GiaMoiNgay
            var servicesCollection = _context.Services;
            var baseService = await servicesCollection.Find(s => s.ServiceName == "Trông giữ tại nhà").FirstOrDefaultAsync();
            var serviceId = baseService?.ServiceId ?? MongoDB.Bson.ObjectId.GenerateNewId().ToString();

            if (baseService == null)
            {
                await servicesCollection.InsertOneAsync(new Service { ServiceId = serviceId, ServiceName = "Trông giữ tại nhà", CreatedAt = DateTime.UtcNow });
            }

            // Remove old services if re-applying? For simplicity, just insert/update
            var sitterServiceFilter = Builders<SitterService>.Filter.Eq(ss => ss.SitterId, sitterId) & Builders<SitterService>.Filter.Eq(ss => ss.ServiceId, serviceId);
            var existingService = await _context.SitterServices.Find(sitterServiceFilter).FirstOrDefaultAsync();
            if (existingService == null)
            {
                await _context.SitterServices.InsertOneAsync(new SitterService
                {
                    SitterServiceId = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
                    SitterId = sitterId,
                    ServiceId = serviceId,
                    Price = dto.GiaMoiNgay
                });
            }
            else
            {
                await _context.SitterServices.UpdateOneAsync(sitterServiceFilter, Builders<SitterService>.Update.Set(ss => ss.Price, dto.GiaMoiNgay));
            }

            return Ok(new { message = "Đã nộp hồ sơ, vui lòng chờ duyệt." });
        }

        [HttpGet("me")]
        public async Task<IActionResult> GetMyApplication()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var profile = await _context.SitterProfiles.Find(s => s.UserId == userId).FirstOrDefaultAsync();
            if (profile == null) return NotFound(new { message = "Bạn chưa có hồ sơ đăng ký." });

            var user = await _context.Users.Find(u => u.UserId == userId).FirstOrDefaultAsync();
            var sitterService = await _context.SitterServices.Find(ss => ss.SitterId == profile.SitterId).FirstOrDefaultAsync();

            var dto = new SitterApplicationDto
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
            };

            return Ok(dto);
        }
    }
}
