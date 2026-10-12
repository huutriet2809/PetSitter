using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace PetSitter.Models
{
    public class StayUpdate
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string BookingId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterUserId { get; set; }

        public string? Note { get; set; }
        public List<string> PhotoPaths { get; set; } = new List<string>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public StayUpdate()
        {
            Id = ObjectId.GenerateNewId().ToString();
        }
    }
}