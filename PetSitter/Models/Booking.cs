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

        [BsonRepresentation(BsonType.Decimal128)]
        public decimal GiaSitter { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal TongAddons { get; set; }
        [BsonRepresentation(BsonType.Decimal128)]
        public decimal TongTien { get; set; } // Chi phí ước tính

        public string TrangThai { get; set; } = "PENDING";
        public string? LyDoTuChoi { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
