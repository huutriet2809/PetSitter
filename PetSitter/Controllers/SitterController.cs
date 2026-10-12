using Microsoft.AspNetCore.Mvc;
using PetSitter.DTOs;
using PetSitter.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/sitters")]
    public class SitterController : ControllerBase
    {
        private readonly ISitterService _sitterService;

        public SitterController(ISitterService sitterService)
        {
            _sitterService = sitterService;
        }

        [HttpGet]
        public async Task<ActionResult<List<SitterListDto>>> GetSitters([FromQuery] string? khuVuc, [FromQuery] string? loaiPet, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var result = await _sitterService.GetSittersAsync(khuVuc, loaiPet, page, pageSize);
            return Ok(result);
        }

        [HttpGet("featured")]
        public async Task<ActionResult<List<SitterListDto>>> GetFeaturedSitters()
        {
            var result = await _sitterService.GetFeaturedSittersAsync();
            return Ok(result);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SitterDetailDto>> GetSitterById(string id)
        {
            var result = await _sitterService.GetSitterByIdAsync(id);
            if (result == null) return NotFound();
            return Ok(result);
        }

        [HttpGet("{id}/reviews")]
        public async Task<ActionResult<List<ReviewDto>>> GetSitterReviews(string id)
        {
            var result = await _sitterService.GetSitterReviewsAsync(id);
            return Ok(result);
        }        [HttpGet("{id}/availability")]
        public async Task<ActionResult<System.Collections.Generic.List<string>>> GetSitterAvailability(string id, [FromQuery] string month)
        {
            var result = await _sitterService.GetSitterAvailabilityAsync(id, month);
            return Ok(result);
        }        [HttpGet("/api/services")]
        public async Task<ActionResult<List<ServiceDto>>> GetServices()
        {
            var result = await _sitterService.GetServicesAsync();
            return Ok(result);
        }

        [HttpGet("/api/stats")]
        public async Task<ActionResult<StatsDto>> GetStats()
        {
            var result = await _sitterService.GetStatsAsync();
            return Ok(result);
        }

        [HttpGet("/api/reviews")]
        public async Task<ActionResult<List<PublicReviewDto>>> GetTopReviews([FromQuery] int limit = 6)
        {
            var result = await _sitterService.GetTopReviewsAsync(limit > 0 && limit <= 20 ? limit : 6);
            return Ok(result);
        }
    }
}