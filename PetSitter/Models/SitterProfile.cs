using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetSitter.Models
{
    public class SitterProfile
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int SitterId { get; set; }

        public int UserId { get; set; }

        [MaxLength(1000)]
        public string? Bio { get; set; }

        public int ExperienceYears { get; set; } = 0;

        [MaxLength(255)]
        public string? Address { get; set; }

        [Column(TypeName = "decimal(10, 7)")]
        public decimal? Latitude { get; set; }

        [Column(TypeName = "decimal(10, 7)")]
        public decimal? Longitude { get; set; }

        [MaxLength(500)]
        public string? AvatarUrl { get; set; }

        public bool IsVerified { get; set; } = false;

        [Column(TypeName = "decimal(3, 2)")]
        public decimal Rating { get; set; } = 0;

        public int TotalReviews { get; set; } = 0;

        // Navigation properties
        [ForeignKey("UserId")]
        public User? User { get; set; }
        public ICollection<SitterService> SitterServices { get; set; } = new List<SitterService>();
        public ICollection<SitterAvailability> SitterAvailabilities { get; set; } = new List<SitterAvailability>();
    }
}
