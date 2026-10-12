using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PetSitter.DTOs
{
    public class CreateBookingDto
    {
        [Required]
        public string SitterId { get; set; } = string.Empty;

        [Required]
        public DateTime NgayBatDau { get; set; }
        
        [Required]
        public DateTime NgayKetThuc { get; set; }

        [Required]
        public string TenPet { get; set; } = string.Empty;
        
        [Required]
        public string LoaiPet { get; set; } = string.Empty;

        [Required]
        public string KichCo { get; set; } = string.Empty;
        public string? PetId { get; set; }

        public string? GhiChu { get; set; }

        public List<string> Addons { get; set; } = new List<string>();
    }

    public class BookingDetailDto
    {
        public string BookingId { get; set; } = string.Empty;
        public string MaDatLich { get; set; } = string.Empty;
        public string OwnerId { get; set; } = string.Empty;
        public string SitterId { get; set; } = string.Empty;
        public string SitterName { get; set; } = string.Empty;
        public string SitterAvatarUrl { get; set; } = string.Empty;
        public DateTime NgayBatDau { get; set; }
        public DateTime NgayKetThuc { get; set; }
        public int SoNgay { get; set; }
        public decimal GiaSitter { get; set; }
        public decimal TongAddons { get; set; }
        public decimal TongTien { get; set; } // Chi phí ước tính
        public string TrangThai { get; set; } = string.Empty;
        public string? LyDoTuChoi { get; set; }
        public bool HasReview { get; set; } = false;
        public string? GhiChu { get; set; }
        
        public PetSitter.Models.BookingPet Pet { get; set; } = new();
        public List<PetSitter.Models.BookingAddon> Addons { get; set; } = new();
    }

    public class RejectBookingDto
    {
        [Required]
        public string LyDo { get; set; } = string.Empty;
    }
}
