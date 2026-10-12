using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class SitterAvailability
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string AvailabilityId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; }

        [Column(TypeName = "date")]
        public DateTime AvailableDate { get; set; }

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public bool IsAvailable { get; set; } = true;

        // Navigation properties
        [ForeignKey("SitterId")]
        public SitterProfile? Sitter { get; set; }
    }
}

