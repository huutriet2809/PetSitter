using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace PetSitter.Models
{
    public class BookingPet
    {
        public string TenPet { get; set; } = string.Empty;
        public string LoaiPet { get; set; } = string.Empty;
        public string KichCo { get; set; } = string.Empty;
    }

    public class PricingItemSnapshot
    {
        public string ItemType { get; set; } = "BASE"; // "BASE_SERVICE" or "ADDON"
        public string? Type { get => ItemType; set => ItemType = value ?? "BASE"; }
        public string ServiceId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long UnitPrice { get; set; }
        public int Quantity { get; set; }
        public bool IsDaily { get; set; }
        public long TotalAmount { get; set; }
        public long Total { get => TotalAmount; set => TotalAmount = value; }
    }

    public class BookingAddon
    {
        public string ServiceId { get; set; } = string.Empty;
        public string Ten { get; set; } = string.Empty;
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal DonGia { get; set; }
        public bool IsDaily { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal ThanhTien { get; set; }
    }

    public class InquiryHistory
    {
        public string HanhDong { get; set; } = string.Empty;
        public string? FromStatus { get; set; }
        public string NguoiThucHien { get; set; } = string.Empty; // OWNER, SITTER, SYSTEM
        public string? ActorUserId { get; set; }
        public string? LyDo { get; set; }
        public DateTime ThoiGian { get; set; } = DateTime.UtcNow;
    }

    [BsonIgnoreExtraElements]
    public class Booking
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string BookingId { get; set; } = string.Empty;

        public string MaDatLich { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string OwnerId { get; set; } = string.Empty;
        
        [BsonRepresentation(BsonType.ObjectId)]
        public string SitterId { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.ObjectId)]
        public string? PetId { get; set; }

        public DateTime NgayBatDau { get; set; }
        public DateTime NgayKetThuc { get; set; }
        public int SoNgay { get; set; }
        public string? GhiChu { get; set; }

        public BookingPet Pet { get; set; } = new BookingPet();
        public List<BookingAddon> Addons { get; set; } = new List<BookingAddon>();
        public List<PricingItemSnapshot> PriceBreakdown { get; set; } = new List<PricingItemSnapshot>();

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal GiaSitter { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal TongAddons { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal TongTien { get; set; } // Chi phí ước tính

        public string TrangThai { get; set; } = BookingStatus.PENDING;
        public bool IsActive { get; set; } = true;
        public DateTime? ExpiresAt { get; set; }
        public string? LyDoTuChoi { get; set; }

        // Financial Snapshot & Payment (Prompt 5A)
        public string PaymentStatus { get; set; } = Models.PaymentStatus.NONE;
        public DateTime? PaymentDeadline { get; set; }
        public long TotalAmount { get; set; }
        public int CommissionBps { get; set; } = 1000; // 1000 bps = 10%
        public long CommissionAmount { get; set; }
        public long SitterAmount { get; set; }

        public DateTime? SitterCompletedAt { get; set; }
        public DateTime? CompletionDeadline { get; set; }
        public DateTime? OwnerConfirmedAt { get; set; }
        public bool? AutoCompleted { get; set; }
        public string? DisputeReason { get; set; }
        public string? AdminResolutionNote { get; set; }
        public bool? ResolvedByAdmin { get; set; }

        public DateTime? UpdatedAt { get; set; }
        public DateTime? EscrowReleasedAt { get; set; }

        public List<InquiryHistory> History { get; set; } = new List<InquiryHistory>();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
