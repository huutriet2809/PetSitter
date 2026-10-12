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
        public string NgayBatDau { get; set; } = string.Empty;
        
        [Required]
        public string NgayKetThuc { get; set; } = string.Empty;

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

    public class InquiryHistoryDto
    {
        public string HanhDong { get; set; } = string.Empty;
        public string? FromStatus { get; set; }
        public string NguoiThucHien { get; set; } = string.Empty;
        public string? ActorUserId { get; set; }
        public string? LyDo { get; set; }
        public DateTime ThoiGian { get; set; }
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
        public bool IsActive { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public string? LyDoTuChoi { get; set; }
        public bool HasReview { get; set; } = false;
        public string? GhiChu { get; set; }
        public string? Warning { get; set; }
        
        public PetSitter.Models.BookingPet Pet { get; set; } = new();
        public List<PetSitter.Models.BookingAddon> Addons { get; set; } = new();
        public List<InquiryHistoryDto> History { get; set; } = new();
    }

    public class RejectBookingDto
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Lý do từ chối là bắt buộc.")]
        [StringLength(500, ErrorMessage = "Lý do tối đa 500 ký tự.")]
        public string LyDo { get; set; } = string.Empty;
    }

    public class CancelBookingRequestDto
    {
        [StringLength(500, ErrorMessage = "Lý do tối đa 500 ký tự.")]
        public string? LyDo { get; set; }
        public string? ExpectedStatus { get; set; }
    }
}
