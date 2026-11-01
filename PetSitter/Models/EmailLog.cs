using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PetSitter.Models
{
    [BsonIgnoreExtraElements]
    public class EmailLog
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string DedupeKey { get; set; } = string.Empty;

        public string ToEmail { get; set; } = string.Empty;

        public string ToName { get; set; } = string.Empty;

        public string Subject { get; set; } = string.Empty;

        public string BodyHtml { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public string Status { get; set; } = "PENDING"; // PENDING, SENT, FAILED

        public int Attempts { get; set; } = 0;

        public string? ErrorMessage { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? SentAt { get; set; }
    }
}

