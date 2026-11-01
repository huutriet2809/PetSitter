using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    [BsonIgnoreExtraElements]
    public class User
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string PasswordHash { get; set; } = string.Empty;

        [MaxLength(20)]
        public string? Phone { get; set; }

        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = "USER";
        public string? AvatarUrl { get; set; }
        // USER, SITTER, ADMIN

        [BsonDefaultValue(true)]
        public bool IsActive { get; set; } = true;

        [BsonDefaultValue(0)]
        public int FailedLoginAttempts { get; set; } = 0;

        public DateTime? LockoutEndUtc { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public SitterProfile? SitterProfile { get; set; }
        public ICollection<Pet> Pets { get; set; } = new List<Pet>();
        public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
    }
}
