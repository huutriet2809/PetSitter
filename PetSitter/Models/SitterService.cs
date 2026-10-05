using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class SitterService
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterServiceId { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; } = string.Empty;
        [BsonRepresentation(BsonType.ObjectId)]
        public string ServiceId { get; set; } = string.Empty;

        public decimal Price { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        // Navigation properties
        [BsonIgnore]
        public SitterProfile? Sitter { get; set; }

        [BsonIgnore]
        public Service? Service { get; set; }
    }
}
