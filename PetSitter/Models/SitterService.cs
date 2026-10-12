using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class SitterService
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterServiceId { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; }
        [BsonRepresentation(BsonType.ObjectId)]
        public string ServiceId { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Price { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        // Navigation properties
        [ForeignKey("SitterId")]
        public SitterProfile? Sitter { get; set; }

        [ForeignKey("ServiceId")]
        public Service? Service { get; set; }
    }
}

