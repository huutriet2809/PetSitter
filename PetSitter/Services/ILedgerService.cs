using System;
using System.Threading.Tasks;
using PetSitter.DTOs;
using PetSitter.Models;

namespace PetSitter.Services
{
    public interface ILedgerService
    {
        Task<JournalEntry?> RecordEntryAsync(JournalEntry entry);
        Task<bool> HasEntryAsync(string idempotencyKey);
        Task<SitterEarningsDto> GetSitterEarningsAsync(string sitterUserId, DateTime nowUtc, int limit = 50);
        Task<AdminPaymentsOverviewDto> GetAdminPaymentsOverviewAsync(DateTime nowUtc);
        Task<(long TotalDebit, long TotalCredit, bool IsBalanced)> AuditLedgerBalanceAsync();
        Task<(bool IsValid, string Reason)> VerifyBookingSettlementInvariantAsync(string bookingId);
        Task<(bool IsValid, long SystemEscrowHeld, long ExpectedEscrowHeld, string Reason)> VerifyGlobalEscrowInvariantAsync();
    }
}
