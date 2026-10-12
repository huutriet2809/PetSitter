using System;
using System.Collections.Generic;

namespace PetSitter.DTOs
{
    public class SitterListDto
    {
        public string SitterId { get; set; } = string.Empty;
        public string Ten { get; set; } = string.Empty;
        public string KhuVuc { get; set; } = string.Empty;
        public decimal DanhGia { get; set; }
        public int SoDanhGia { get; set; }
        public string MoTaNgan { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        public decimal GiaTu { get; set; }
        public bool NoiBat { get; set; }
    }

    public class SitterDetailDto
    {
        public string SitterId { get; set; } = string.Empty;
        public string Ten { get; set; } = string.Empty;
        public string KhuVuc { get; set; } = string.Empty;
        public decimal DanhGia { get; set; }
        public int SoDanhGia { get; set; }
        public string AvatarUrl { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public int KinhNghiem { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public List<ServiceDto> DichVu { get; set; } = new List<ServiceDto>();
        public List<string> LichRanh { get; set; } = new List<string>(); // ISO date strings
    }

    public class ReviewDto
    {
        public string ReviewId { get; set; } = string.Empty;
        public string NguoiDung { get; set; } = string.Empty; // Reviewer name
        public string Avatar { get; set; } = string.Empty; // Reviewer avatar
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime Ngay { get; set; }
    }

    public class PublicReviewDto
    {
        public string NguoiDung { get; set; } = string.Empty;
        public string? Avatar { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public string SitterName { get; set; } = string.Empty;
        public DateTime Ngay { get; set; }
    }

    public class ServiceDto
    {
        public string Id { get; set; } = string.Empty;
        public string Ten { get; set; } = string.Empty;
        public string MoTa { get; set; } = string.Empty;
        public decimal Gia { get; set; }
    }

    public class StatsDto
    {
        public int Sitters { get; set; }
        public int Users { get; set; }
        public int Bookings { get; set; }
    }
    public class UpdateSitterProfileDto
    {
        public string Bio { get; set; } = string.Empty;
        public decimal GiaMoiNgay { get; set; }
        public string KhuVuc { get; set; } = string.Empty;
        public List<string> LoaiPetNhan { get; set; } = new List<string>();
        public List<string> Tags { get; set; } = new List<string>();
        public List<string> DichVuBoSung { get; set; } = new List<string>(); // ServiceIds
    }

    public class SitterDayAvailabilityDto
    {
        public string Date { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Available", "Unavailable", "Booked"
        public string? BookingId { get; set; }
    }

    public class UpdateSitterAvailabilityDto
    {
        public List<string> Dates { get; set; } = new List<string>();
        public bool IsAvailable { get; set; }
    }
}