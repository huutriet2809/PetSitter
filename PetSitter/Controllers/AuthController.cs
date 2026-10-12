using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Services;
using PetSitter.Models;
using PetSitter.Data;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Driver;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly PetSitterContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IUserService userService, PetSitterContext context, IWebHostEnvironment env, ILogger<AuthController> logger)
        {
            _userService = userService;
            _context = context;
            _env = env;
            _logger = logger;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userDto = await _userService.AuthenticateAsync(loginDto);

            if (userDto == null)
                return Unauthorized(new { Message = "Email hoặc mật khẩu không đúng." });

            return Ok(userDto);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto registerDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var userDto = await _userService.RegisterAsync(registerDto);

            if (userDto == null)
                return BadRequest(new { Message = "Email đã tồn tại." });

            return Ok(userDto);
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<IActionResult> GetMe()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var user = await _userService.GetUserByIdAsync(userId);
            if (user == null) return NotFound();

            return Ok(new UserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                Role = user.Role,
                Phone = user.Phone,
                AvatarUrl = user.AvatarUrl
            });
        }

        [Authorize]
        [HttpPut("me")]
        public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                var userDto = await _userService.UpdateProfileAsync(userId, dto);
                return Ok(userDto);
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [Authorize]
        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            try
            {
                var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId)) return Unauthorized();

                await _userService.ChangePasswordAsync(userId, dto);
                return Ok(new { message = "Đổi mật khẩu thành công." });
            }
            catch (AppException ex)
            {
                return StatusCode(ex.StatusCode, new { message = ex.Message });
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
        {
            var message = "Nếu email tồn tại, hướng dẫn đặt lại đã được tạo";
            var user = await _context.Users.Find(u => u.Email.ToLower() == dto.Email.ToLower()).FirstOrDefaultAsync();

            if (user != null)
            {
                var createdAfter = DateTime.UtcNow.AddMinutes(-45);
                var recentRequests = await _context.PasswordResets.CountDocumentsAsync(r => r.Email == user.Email && r.ExpiresAt > createdAfter);
                
                if (recentRequests < 3)
                {
                    var tokenBytes = new byte[32];
                    RandomNumberGenerator.Fill(tokenBytes);
                    var token = Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
                    
                    var tokenHash = ComputeSha256(token);

                    var reset = new PasswordReset
                    {
                        Email = user.Email,
                        TokenHash = tokenHash,
                        ExpiresAt = DateTime.UtcNow.AddMinutes(15),
                        Used = false
                    };

                    await _context.PasswordResets.InsertOneAsync(reset);
                    
                    var devResetLink = $"/reset-password.html?token={token}";
                    _logger.LogInformation($"Reset password link created: {devResetLink}");

                    if (_env.IsDevelopment())
                    {
                        return Ok(new { message, devResetLink });
                    }
                }
            }

            return Ok(new { message });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
        {
            var tokenHash = ComputeSha256(dto.Token);
            
            var reset = await _context.PasswordResets.Find(r => r.TokenHash == tokenHash && !r.Used && r.ExpiresAt > DateTime.UtcNow).FirstOrDefaultAsync();
            if (reset == null)
            {
                return BadRequest(new { message = "Liên kết không hợp lệ hoặc đã hết hạn." });
            }

            var user = await _context.Users.Find(u => u.Email == reset.Email).FirstOrDefaultAsync();
            if (user == null)
            {
                return BadRequest(new { message = "Liên kết không hợp lệ hoặc đã hết hạn." });
            }

            var update = Builders<User>.Update.Set(u => u.PasswordHash, BCrypt.Net.BCrypt.HashPassword(dto.NewPassword));
            await _context.Users.UpdateOneAsync(u => u.UserId == user.UserId, update);

            var resetUpdate = Builders<PasswordReset>.Update.Set(r => r.Used, true);
            await _context.PasswordResets.UpdateOneAsync(r => r.Id == reset.Id, resetUpdate);

            return Ok(new { message = "Đặt lại mật khẩu thành công." });
        }

        private string ComputeSha256(string input)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLower();
        }
    }
}