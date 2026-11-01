using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    [BsonIgnoreExtraElements]
    public class Pet
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string PetId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string OwnerId { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Species { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Breed { get; set; }

        [MaxLength(10)]
        public string? Gender { get; set; } // MALE, FEMALE

        public int? Age { get; set; }

        [Column(TypeName = "decimal(6, 2)")]
        public decimal? Weight { get; set; }

        [MaxLength(1000)]
        public string? HealthStatus { get; set; }

        [MaxLength(1000)]
        public string? DietaryNotes { get; set; }

        [MaxLength(500)]
        public string? AvatarUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("OwnerId")]
        public User? Owner { get; set; }
    }
}

