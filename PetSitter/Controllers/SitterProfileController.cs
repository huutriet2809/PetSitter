using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Services;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/sitter")]
    public class SitterProfileController : ControllerBase
    {
        private readonly ISitterService _sitterService;

        public SitterProfileController(ISitterService sitterService)
        {
            _sitterService = sitterService;
        }

        [Authorize(Roles = "SITTER")]
        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var profile = await _sitterService.GetSitterProfileByUserIdAsync(userId);
            if (profile == null) return NotFound(new { message = "Không tìm thấy hồ sơ sitter." });

            return Ok(profile);
        }

        [Authorize(Roles = "SITTER")]
        [HttpPut("profile")]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateSitterProfileDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                await _sitterService.UpdateSitterProfileAsync(userId, dto);
                return Ok(new { message = "Cập nhật hồ sơ thành công" });
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [Authorize(Roles = "SITTER")]
        [HttpGet("availability")]
        public async Task<IActionResult> GetAvailability([FromQuery] string month)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await _sitterService.GetSitterAvailabilityCalendarAsync(userId, month);
            return Ok(result);
        }

        [Authorize(Roles = "SITTER")]
        [HttpPut("availability")]
        public async Task<IActionResult> UpdateAvailability([FromBody] UpdateSitterAvailabilityDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                await _sitterService.UpdateSitterAvailabilityAsync(userId, dto);
                return Ok(new { message = "Cập nhật lịch thành công" });
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }
    }
}