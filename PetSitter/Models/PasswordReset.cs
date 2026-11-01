using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PetSitter.Models
{
    public class PasswordReset
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        public string Email { get; set; } = string.Empty;
        public string TokenHash { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public bool Used { get; set; } = false;
    }
}