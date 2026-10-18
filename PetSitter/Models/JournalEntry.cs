using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class JournalEntry
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string EntryId { get; set; } = null!;

        public string TransactionId { get; set; } = null!;

        public string BookingId { get; set; } = null!;

        public string Type { get; set; } = null!; // PAYMENT, ESCROW_RELEASE, REFUND, REFUND_COMPLETED, DISPUTE_RESOLUTION, etc.

        public string? ActorUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public string IdempotencyKey { get; set; } = null!;

        public List<JournalLine> Lines { get; set; } = new List<JournalLine>();
    }

    public class JournalLine
    {
        public string Account { get; set; } = null!; // GATEWAY_CLEARING, ESCROW_HELD, PLATFORM_REVENUE, SITTER_EARNINGS:{SitterUserId}, REFUND_PENDING

        public string Direction { get; set; } = null!; // DEBIT, CREDIT

        public long Amount { get; set; } // VND nguyên dương

        public DateTime? AvailableAt { get; set; } // Cho bút toán thu nhập Sitter
    }

    public static class LedgerAccount
    {
        public const string GATEWAY_CLEARING = "GATEWAY_CLEARING";
        public const string ESCROW_HELD = "ESCROW_HELD";
        public const string PLATFORM_REVENUE = "PLATFORM_REVENUE";
        public const string GATEWAY_FEES = "GATEWAY_FEES";
        public const string REFUND_PENDING = "REFUND_PENDING";

        public static string SitterEarnings(string sitterUserId) => $"SITTER_EARNINGS:{sitterUserId}";
    }

    public static class LedgerDirection
    {
        public const string DEBIT = "DEBIT";
        public const string CREDIT = "CREDIT";
    }

    public static class JournalType
    {
        public const string PAYMENT_HELD = "PAYMENT_HELD";
        public const string ESCROW_RELEASE = "ESCROW_RELEASE";
        public const string REFUND_PENDING = "REFUND_PENDING";
        public const string REFUND_COMPLETED = "REFUND_COMPLETED";
        public const string DISPUTE_SPLIT = "DISPUTE_SPLIT";
        public const string LATE_PAYMENT_REFUND = "LATE_PAYMENT_REFUND";
    }

    public static class LedgerIdempotencyKeys
    {
        public static string PaymentHeld(string bookingId) => $"{bookingId}:PAYMENT_HELD";
        public static string EscrowRelease(string bookingId) => $"{bookingId}:ESCROW_RELEASE";
        public static string CancelSitter(string bookingId) => $"{bookingId}:CANCEL_SITTER";
        public static string CancelOwner100(string bookingId) => $"{bookingId}:CANCEL_OWNER_100";
        public static string CancelOwner50(string bookingId) => $"{bookingId}:CANCEL_OWNER_50";
        public static string CancelOwner0(string bookingId) => $"{bookingId}:CANCEL_OWNER_0";
        public static string DisputeSplit(string bookingId) => $"{bookingId}:DISPUTE_SPLIT";
        public static string DisputeForfeit(string bookingId) => $"{bookingId}:DISPUTE_FORFEIT";
        public static string RefundCompleted(string refundId) => $"{refundId}:REFUND_COMPLETED";
        public static string LatePaymentRefund(string bookingId) => $"{bookingId}:LATE_PAYMENT_REFUND";
    }
}
