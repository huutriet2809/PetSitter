using System;
using System.ComponentModel.DataAnnotations;

namespace PetSitter.DTOs
{
    public class PetDto
    {
        public string PetId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Species { get; set; } = string.Empty;
        public string? Breed { get; set; }
        public string? Gender { get; set; }
        public int? Age { get; set; }
        public decimal? Weight { get; set; }
        public string? HealthStatus { get; set; }
        public string? DietaryNotes { get; set; }
        public string? AvatarUrl { get; set; }
    }

    public class CreatePetDto
    {
        [Required(ErrorMessage = "Tên thú cưng là bắt buộc")]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Loại thú cưng là bắt buộc")]
        [MaxLength(50)]
        public string Species { get; set; } = string.Empty; // chó/mèo/khác

        [MaxLength(100)]
        public string? Breed { get; set; }

        [MaxLength(10)]
        public string? Gender { get; set; }

        [Range(0, 50, ErrorMessage = "Tuổi phải lớn hơn hoặc bằng 0")]
        public int? Age { get; set; }

        [Range(0.1, 200, ErrorMessage = "Cân nặng phải lớn hơn 0")]
        public decimal? Weight { get; set; }

        [MaxLength(500, ErrorMessage = "Ghi chú sức khỏe tối đa 500 ký tự")]
        public string? HealthStatus { get; set; }

        [MaxLength(1000)]
        public string? DietaryNotes { get; set; }

        [MaxLength(500)]
        public string? AvatarUrl { get; set; }
    }
}