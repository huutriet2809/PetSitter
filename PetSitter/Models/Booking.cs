using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class Booking
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int BookingId { get; set; }

        public int OwnerId { get; set; }
        public int SitterId { get; set; }
        public int PetId { get; set; }
        public int SitterServiceId { get; set; }

        public DateTime StartDatetime { get; set; }
        public DateTime EndDatetime { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal TotalPrice { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "PENDING"; // PENDING, CONFIRMED, CANCELLED, COMPLETED, REJECTED

        [MaxLength(1000)]
        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("OwnerId")]
        public User? Owner { get; set; }

        [ForeignKey("SitterId")]
        public SitterProfile? Sitter { get; set; }

        [ForeignKey("PetId")]
        public Pet? Pet { get; set; }

        [ForeignKey("SitterServiceId")]
        public SitterService? SitterService { get; set; }

        public Payment? Payment { get; set; }
        public Review? Review { get; set; }
    }
}
