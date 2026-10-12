using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSitter.Models;
using MongoDB.Driver;
using System;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.Collections.Concurrent;

namespace PetSitter.Controllers
{
    public class ContactDto
    {
        [Required, MinLength(2), MaxLength(60)]
        public string Name { get; set; } = string.Empty;
        
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
        
        [Required, MaxLength(100)]
        public string Subject { get; set; } = string.Empty;
        
        [Required, MinLength(10), MaxLength(1000)]
        public string Message { get; set; } = string.Empty;
        
        public string? Website { get; set; } // honeypot
    }

    public class NewsletterDto
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    [ApiController]
    public class MiscController : ControllerBase
    {
        private readonly PetSitterContext _context;
        private static readonly ConcurrentDictionary<string, (int count, DateTime expiresAt)> _ipRequests = new();

        public MiscController(PetSitterContext context)
        {
            _context = context;
        }

        [HttpPost("api/contact")]
        public async Task<IActionResult> SubmitContact([FromBody] ContactDto dto)
        {
            if (!string.IsNullOrEmpty(dto.Website))
                return Ok(new { message = "Gửi tin nhắn thành công." }); // Honeypot trap

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            
            // Cleanup expired
            foreach (var key in _ipRequests.Keys)
            {
                if (_ipRequests.TryGetValue(key, out var val) && val.expiresAt < DateTime.UtcNow)
                    _ipRequests.TryRemove(key, out _);
            }

            var currentRequests = _ipRequests.GetOrAdd(ip, _ => (0, DateTime.UtcNow.AddHours(1)));
            if (currentRequests.count >= 5)
            {
                return StatusCode(429, new { message = "Bạn đã gửi quá nhiều tin nhắn. Vui lòng thử lại sau." });
            }
            
            _ipRequests[ip] = (currentRequests.count + 1, currentRequests.expiresAt);

            var message = new ContactMessage
            {
                Name = dto.Name,
                Email = dto.Email,
                Subject = dto.Subject,
                Message = dto.Message,
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };

            await _context.ContactMessages.InsertOneAsync(message);

            return Ok(new { message = "Gửi tin nhắn thành công." });
        }

        [HttpPost("api/newsletter")]
        public async Task<IActionResult> SubscribeNewsletter([FromBody] NewsletterDto dto)
        {
            var emailLower = dto.Email.ToLower();
            try
            {
                var newsletter = new Newsletter { Email = emailLower };
                await _context.Newsletters.InsertOneAsync(newsletter);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // Ignore dup
            }
            
            return Ok(new { message = "Đăng ký nhận bản tin thành công." });
        }

        [Authorize(Roles = "ADMIN")]
        [HttpGet("api/admin/messages")]
        public async Task<IActionResult> GetMessages([FromQuery] bool? isRead)
        {
            var filter = isRead.HasValue ? Builders<ContactMessage>.Filter.Eq(m => m.IsRead, isRead.Value) : Builders<ContactMessage>.Filter.Empty;
            
            var messages = await _context.ContactMessages.Find(filter)
                .SortByDescending(m => m.CreatedAt)
                .ToListAsync();

            return Ok(messages);
        }

        [Authorize(Roles = "ADMIN")]
        [HttpPatch("api/admin/messages/{id}/read")]
        public async Task<IActionResult> MarkMessageRead(string id)
        {
            var update = Builders<ContactMessage>.Update.Set(m => m.IsRead, true);
            var result = await _context.ContactMessages.UpdateOneAsync(m => m.Id == id, update);

            if (result.MatchedCount == 0) return NotFound(new { message = "Không tìm thấy tin nhắn." });

            return Ok(new { message = "Đã đánh dấu đã đọc." });
        }
    }
}