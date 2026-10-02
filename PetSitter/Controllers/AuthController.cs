using Microsoft.AspNetCore.Mvc;
using PetSitter.DTOs;
using PetSitter.Services;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;

        public AuthController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userDto = await _userService.AuthenticateAsync(loginDto);

            if (userDto == null)
                return Unauthorized(new { Message = "Invalid email or password." });

            return Ok(userDto);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userDto = await _userService.RegisterAsync(registerDto);

            if (userDto == null)
                return BadRequest(new { Message = "User with this email already exists." });

            return Ok(userDto);
        }
    }
}
