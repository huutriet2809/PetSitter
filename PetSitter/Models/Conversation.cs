using System.ComponentModel.DataAnnotations;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class Conversation
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string ConversationId { get; set; } = ObjectId.GenerateNewId().ToString();

        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;
        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; } = string.Empty;

        public string? Message { get; set; }

        [Required]
        [MaxLength(10)]
        public string SenderType { get; set; } = string.Empty; // USER, SITTER

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [BsonIgnore]
        public User? User { get; set; }

        [BsonIgnore]
        public SitterProfile? Sitter { get; set; }
    }
}
