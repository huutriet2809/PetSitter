using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class SitterAvailability
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int AvailabilityId { get; set; }

        public int SitterId { get; set; }

        [Column(TypeName = "date")]
        public DateTime AvailableDate { get; set; }

        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }

        public bool IsAvailable { get; set; } = true;

        // Navigation properties
        [ForeignKey("SitterId")]
        public SitterProfile? Sitter { get; set; }
    }
}
