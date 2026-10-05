using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class SitterProfile
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Bio { get; set; }

        public int ExperienceYears { get; set; } = 0;

        [MaxLength(255)]
        public string? Address { get; set; }

        public decimal? Latitude { get; set; }

        public decimal? Longitude { get; set; }

        [MaxLength(500)]
        public string? AvatarUrl { get; set; }

        public bool IsVerified { get; set; } = false;

        public decimal Rating { get; set; } = 0;

        public int TotalReviews { get; set; } = 0;

        // Navigation properties
        [BsonIgnore]
        public User? User { get; set; }
        [BsonIgnore]
        public ICollection<SitterService> SitterServices { get; set; } = new List<SitterService>();
        [BsonIgnore]
        public ICollection<SitterAvailability> SitterAvailabilities { get; set; } = new List<SitterAvailability>();
    }
}
