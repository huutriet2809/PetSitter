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

        public DateTime? SitterCompletedAt { get; set; }
        public DateTime? CompletionDeadline { get; set; }
        public DateTime? OwnerConfirmedAt { get; set; }
        public bool? AutoCompleted { get; set; }
        public string? DisputeReason { get; set; }
        public string? AdminResolutionNote { get; set; }
        public bool? ResolvedByAdmin { get; set; }

        public string PaymentStatus { get; set; } = Models.PaymentStatus.NONE;
        public DateTime? PaymentDeadline { get; set; }
        public long TotalAmount { get; set; }
        public int CommissionBps { get; set; }
        public long CommissionAmount { get; set; }
        public long SitterAmount { get; set; }
        
        public PetSitter.Models.BookingPet Pet { get; set; } = new();
        public List<PetSitter.Models.BookingAddon> Addons { get; set; } = new();
        public List<PetSitter.Models.PricingItemSnapshot> PriceBreakdown { get; set; } = new();
        public List<InquiryHistoryDto> History { get; set; } = new();
    }

    public class DisputeBookingDto
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Lý do khiếu nại là bắt buộc.")]
        [StringLength(500, MinimumLength = 10, ErrorMessage = "Lý do khiếu nại phải từ 10 đến 500 ký tự.")]
        public string LyDo { get; set; } = string.Empty;
    }

    public class ResolveDisputeDto
    {
        [Required(ErrorMessage = "Hướng xử lý là bắt buộc.")]
        [RegularExpression("^(COMPLETED|CANCELLED|SPLIT)$", ErrorMessage = "Resolution chỉ nhận COMPLETED, CANCELLED hoặc SPLIT.")]
        public string Resolution { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "Ghi chú xử lý là bắt buộc.")]
        [StringLength(500, MinimumLength = 10, ErrorMessage = "Ghi chú xử lý phải từ 10 đến 500 ký tự.")]
        public string Note { get; set; } = string.Empty;

        public long? RefundToOwnerAmount { get; set; }
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
