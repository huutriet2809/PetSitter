using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PetSitter.Models
{
    [BsonIgnoreExtraElements]
    public class Notification
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string NotificationId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string UserId { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? BookingId { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReadAt { get; set; }

        public string DedupeKey { get; set; } = string.Empty;

        // OWNER, SITTER, ADMIN (hỗ trợ điều hướng chính xác cho người dùng có nhiều vai trò)
        public string? TargetRole { get; set; }
    }
}
