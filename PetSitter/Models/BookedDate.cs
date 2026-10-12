using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace PetSitter.Models
{
    public class BookedDate
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string BookingId { get; set; } = string.Empty;

        public DateTime Date { get; set; }
    }
}
