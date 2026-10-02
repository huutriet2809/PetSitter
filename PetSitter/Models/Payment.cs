using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class Payment
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int PaymentId { get; set; }

        public int BookingId { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Amount { get; set; }

        [Required]
        [MaxLength(30)]
        public string PaymentMethod { get; set; } = string.Empty; // CASH, BANK_TRANSFER, MOMO, VNPAY

        [MaxLength(100)]
        public string? TransactionCode { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "PENDING"; // PENDING, PAID, FAILED, REFUNDED

        public DateTime? PaidAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("BookingId")]
        public Booking? Booking { get; set; }
    }
}
