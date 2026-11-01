using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace PetSitter.Models
{
    public class ReconciliationRun
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string RunId { get; set; } = null!;

        public DateTime RunAt { get; set; } = DateTime.UtcNow;

        public string AdminUserId { get; set; } = null!;

        public bool IsBalancedTotalDebitCredit { get; set; }
        public long TotalDebit { get; set; }
        public long TotalCredit { get; set; }

        public bool IsReservedAmountMatchesOpenWithdrawals { get; set; }
        public long TotalReservedAmount { get; set; }
        public long TotalOpenWithdrawals { get; set; }
        public int AutoFixedAccountsCount { get; set; }

        public bool IsEscrowHeldMatchesHeldBookings { get; set; }
        public long EscrowHeldBalance { get; set; }
        public long HeldBookingsTotal { get; set; }

        public bool HasCompletedBookingsWithHeldStatus { get; set; }
        public int CompletedBookingsWithHeldStatusCount { get; set; }

        public bool HasPaidPaymentsWithoutProperStatus { get; set; }
        public int PaidPaymentsWithoutProperStatusCount { get; set; }

        public bool HasStaleProcessingWithdrawals { get; set; }
        public int StaleProcessingWithdrawalsCount { get; set; }

        public long? ActualBankBalance { get; set; }
        public long? ExpectedBankBalance { get; set; }
        public long? BankBalanceDiscrepancy { get; set; }
        public bool? IsBankBalanceMatched { get; set; }

        public string Status { get; set; } = "SUCCESS"; // SUCCESS, WARNING, FAILED

        public List<string> Details { get; set; } = new();
    }
}
