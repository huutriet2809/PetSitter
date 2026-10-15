using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class Payment
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string PaymentId { get; set; } = null!;

        public string BookingId { get; set; } = null!;

        public long OrderCode { get; set; } // int64 duy nhất

        public string? PayosPaymentLinkId { get; set; }

        public string? CheckoutUrl { get; set; }

        public string? QrCode { get; set; }

        public long Amount { get; set; } // VND nguyên

        public string Status { get; set; } = PaymentLinkStatus.PENDING; // PENDING, PAID, CANCELLED, EXPIRED

        public DateTime ExpiresAt { get; set; }

        public DateTime? PaidAt { get; set; }

        public string? RawWebhook { get; set; } // JSON sanitized (bỏ các secret/khóa nhạy cảm)

        // Mã hóa thông tin người trả (PayerInfo) bằng ASP.NET Core Data Protection
        public string? EncryptedPayerInfo { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PayerInfo
    {
        public string? AccountNumber { get; set; }
        public string? AccountName { get; set; }
        public string? BankId { get; set; }
        public string? BankName { get; set; }
        public string? Reference { get; set; }
    }

    public static class PaymentLinkStatus
    {
        public const string PENDING = "PENDING";
        public const string PAID = "PAID";
        public const string CANCELLED = "CANCELLED";
        public const string EXPIRED = "EXPIRED";
    }
}
