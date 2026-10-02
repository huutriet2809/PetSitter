using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class Review
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ReviewId { get; set; }

        public int BookingId { get; set; }
        public int OwnerId { get; set; }
        public int SitterId { get; set; }

        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("BookingId")]
        public Booking? Booking { get; set; }

        [ForeignKey("OwnerId")]
        public User? Owner { get; set; }

        [ForeignKey("SitterId")]
        public SitterProfile? Sitter { get; set; }
    }
}
