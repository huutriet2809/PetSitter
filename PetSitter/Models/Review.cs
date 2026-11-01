using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class Review
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ReviewId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string BookingId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string OwnerId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; }

        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("BookingId")]
        public Booking? Booking { get; set; }

        [ForeignKey("OwnerId")]
        public User? Owner { get; set; }

        [ForeignKey("SitterId")]
        public SitterProfile? Sitter { get; set; }
    }
}

