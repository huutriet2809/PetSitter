using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class Conversation
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ConversationId { get; set; }

        public int UserId { get; set; }
        public int SitterId { get; set; }

        public string? Message { get; set; }

        [Required]
        [MaxLength(10)]
        public string SenderType { get; set; } = string.Empty; // USER, SITTER

        public DateTime SentAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("UserId")]
        public User? User { get; set; }

        [ForeignKey("SitterId")]
        public SitterProfile? Sitter { get; set; }
    }
}
