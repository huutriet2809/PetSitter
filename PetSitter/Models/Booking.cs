using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class Booking
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string BookingId { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonRepresentation(BsonType.ObjectId)]
        public string OwnerId { get; set; } = string.Empty;
        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; } = string.Empty;
        [BsonRepresentation(BsonType.ObjectId)]
        public string PetId { get; set; } = string.Empty;
        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterServiceId { get; set; } = string.Empty;

        public DateTime StartDatetime { get; set; }
        public DateTime EndDatetime { get; set; }

        public decimal TotalPrice { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "PENDING"; // PENDING, CONFIRMED, CANCELLED, COMPLETED, REJECTED

        [MaxLength(1000)]
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [BsonIgnore]
        public User? Owner { get; set; }

        [BsonIgnore]
        public SitterProfile? Sitter { get; set; }

        [BsonIgnore]
        public Pet? Pet { get; set; }

        [BsonIgnore]
        public SitterService? SitterService { get; set; }

        [BsonIgnore]
        public Payment? Payment { get; set; }
        [BsonIgnore]
        public Review? Review { get; set; }
    }
}
