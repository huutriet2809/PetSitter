using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSitter.DTOs;
using PetSitter.Services;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/pets")]
    [Authorize]
    public class PetController : ControllerBase
    {
        private readonly IPetService _petService;

        public PetController(IPetService petService)
        {
            _petService = petService;
        }

        [HttpGet]
        public async Task<ActionResult<List<PetDto>>> GetMyPets()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var pets = await _petService.GetMyPetsAsync(userId);
            return Ok(pets);
        }

        [HttpPost]
        public async Task<ActionResult<PetDto>> CreatePet([FromBody] CreatePetDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var pet = await _petService.CreatePetAsync(userId, dto);
            return Ok(pet);
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<PetDto>> UpdatePet(string id, [FromBody] CreatePetDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var pet = await _petService.UpdatePetAsync(userId, id, dto);
            if (pet == null) return NotFound();

            return Ok(pet);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeletePet(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var success = await _petService.DeletePetAsync(userId, id);
            if (!success) return NotFound();

            return NoContent();
        }
    }
}