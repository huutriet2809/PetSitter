using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class Service
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ServiceId { get; set; }

        [Required]
        [MaxLength(100)]
        public string ServiceName { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal Gia { get; set; } = 50000;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<SitterService> SitterServices { get; set; } = new List<SitterService>();
    }
}

