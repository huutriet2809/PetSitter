using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public static class BankMatchStatus
    {
        public const string MATCHED = "MATCHED";
        public const string AMOUNT_MISMATCH = "AMOUNT_MISMATCH";
        public const string MISSING_IN_LEDGER = "MISSING_IN_LEDGER";
        public const string MISSING_IN_BANK = "MISSING_IN_BANK";
        public const string SUSPICIOUS = "SUSPICIOUS";
    }

    public class BankReconciliationItem
    {
        public int LineNumber { get; set; }
        public DateTime? TransactionDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public long StatementAmount { get; set; }
        public string ReferenceCode { get; set; } = string.Empty;
        public long? OrderCode { get; set; }
        public string? BookingId { get; set; }
        public long? LedgerAmount { get; set; }
        public string MatchStatus { get; set; } = string.Empty; // MATCHED, AMOUNT_MISMATCH, MISSING_IN_LEDGER, MISSING_IN_BANK
        public string DiscrepancyNote { get; set; } = string.Empty;
    }

    public class BankReconciliationRun
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string RunId { get; set; } = null!;

        public DateTime RunAt { get; set; } = DateTime.UtcNow;
        public string AdminUserId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }

        public int TotalBankRows { get; set; }
        public int MatchedCount { get; set; }
        public int AmountMismatchCount { get; set; }
        public int MissingInLedgerCount { get; set; }
        public int MissingInBankCount { get; set; }
        public int SuspiciousCount { get; set; }

        public long TotalBankAmount { get; set; }
        public long TotalLedgerAmount { get; set; }

        public List<BankReconciliationItem> Items { get; set; } = new();
    }
}

