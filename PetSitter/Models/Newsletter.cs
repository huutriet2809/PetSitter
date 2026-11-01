using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class Newsletter
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

        public string Email { get; set; } = string.Empty;
    }
}