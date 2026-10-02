using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class SitterVerification
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int VerificationId { get; set; }

        public int SitterId { get; set; }

        [Required]
        [MaxLength(50)]
        public string DocumentType { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? DocumentNumber { get; set; }

        [MaxLength(500)]
        public string? DocumentUrl { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED

        public DateTime? VerifiedAt { get; set; }

        [MaxLength(500)]
        public string? RejectionReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("SitterId")]
        public SitterProfile? Sitter { get; set; }
    }
}
