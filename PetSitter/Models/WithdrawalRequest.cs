using System;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class WithdrawalRequest
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string WithdrawalId { get; set; } = null!;

        public string SitterUserId { get; set; } = null!;

        public long Amount { get; set; } // VND nguyên dương

        public string EncryptedBankSnapshot { get; set; } = null!; // JSON mã hóa Data Protection

        public string MaskedAccountNumber { get; set; } = null!; // ****1234

        public string BankBin { get; set; } = null!;

        public string BankName { get; set; } = null!;

        public string AccountHolderName { get; set; } = null!;

        public bool NameMatchesRegisteredName { get; set; } = true;

        public string Status { get; set; } = WithdrawalStatus.REQUESTED; // REQUESTED, APPROVED, PROCESSING, PAID, FAILED, REJECTED, CANCELLED

        public string ReferenceId { get; set; } = null!; // Unique

        public string IdempotencyKey { get; set; } = null!; // Unique theo user

        public string? SettlementKey { get; set; } // Khóa bút toán sổ cái khi PAID

        public string? BankTransactionRef { get; set; } // Mã tham chiếu giao dịch ngân hàng khi PAID (unique)

        public string? ApprovedByAdminUserId { get; set; }
        public DateTime? ApprovedAt { get; set; }

        public string? ProcessingByAdminUserId { get; set; } // Admin nhận claim xử lý thủ công
        public string? ProcessingByAdminName { get; set; }
        public DateTime? ProcessingAt { get; set; }

        public string? PaidByAdminUserId { get; set; }
        public DateTime? PaidAt { get; set; }

        public string? RejectedByAdminUserId { get; set; }
        public string? RejectReason { get; set; }
        public DateTime? RejectedAt { get; set; }

        public DateTime? CancelledAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public static class WithdrawalStatus
    {
        public const string REQUESTED = "REQUESTED";
        public const string APPROVED = "APPROVED";
        public const string PROCESSING = "PROCESSING";
        public const string PAID = "PAID";
        public const string FAILED = "FAILED";
        public const string REJECTED = "REJECTED";
        public const string CANCELLED = "CANCELLED";
    }
}
