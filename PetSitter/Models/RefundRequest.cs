using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class RefundRequest
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string RefundId { get; set; } = null!;

        public string BookingId { get; set; } = null!;

        public string? PaymentId { get; set; }

        public string OwnerUserId { get; set; } = null!;

        public long Amount { get; set; } // Số tiền hoàn (long VND)

        public string Reason { get; set; } = null!; // SITTER_CANCELLED, OWNER_CANCELLED_EARLY, ADMIN_RESOLVED, LATE_PAYMENT, etc.

        public string Status { get; set; } = RefundStatus.REFUND_PENDING; // REFUND_PENDING, COMPLETED, REJECTED

        // Thông tin tài khoản nhận hoàn của Owner (nếu webhook không có counterAccount, Owner nhập thủ công)
        public string? EncryptedRefundAccount { get; set; }

        public string? BankTransactionRef { get; set; } // Mã giao dịch ngân hàng khi Admin hoàn tiền thủ công

        public string? AdminUserId { get; set; }
        public string? AdminNote { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAt { get; set; }
    }

    public class RefundAccountInfo
    {
        public string BankName { get; set; } = null!;
        public string AccountNumber { get; set; } = null!;
        public string AccountName { get; set; } = null!;
    }

    public static class RefundStatus
    {
        public const string REFUND_PENDING = "REFUND_PENDING";
        public const string COMPLETED = "COMPLETED";
        public const string REJECTED = "REJECTED";
    }
}
