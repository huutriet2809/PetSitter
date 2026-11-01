using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PetSitter.DTOs
{
    public class CreateSitterApplicationDto
    {
        [Required]
        [MaxLength(1000)]
        public string Bio { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string KhuVuc { get; set; } = string.Empty;

        [Required]
        public decimal GiaMoiNgay { get; set; }

        public List<string> LoaiPetNhan { get; set; } = new List<string>();

        public int KinhNghiem { get; set; }

        public List<string> Tags { get; set; } = new List<string>();
    }

    public class SitterApplicationDto
    {
        public string Id { get; set; } = string.Empty; // SitterId
        public string UserId { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string KhuVuc { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public int KinhNghiem { get; set; }
        public List<string> LoaiPetNhan { get; set; } = new List<string>();
        public List<string> Tags { get; set; } = new List<string>();
        public decimal GiaMoiNgay { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? LyDoTuChoi { get; set; }
        public System.DateTime CreatedAt { get; set; }
    }

    public class RejectApplicationDto
    {
        [Required]
        public string LyDo { get; set; } = string.Empty;
    }
}
