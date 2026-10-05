using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class SitterAvailability
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string AvailabilityId { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; } = string.Empty;

        public DateTime AvailableDate { get; set; }

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public bool IsAvailable { get; set; } = true;

        // Navigation properties
        [BsonIgnore]
        public SitterProfile? Sitter { get; set; }
    }
}
