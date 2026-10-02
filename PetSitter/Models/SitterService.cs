using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class SitterService
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SitterServiceId { get; set; }

        public int SitterId { get; set; }
        public int ServiceId { get; set; }

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
