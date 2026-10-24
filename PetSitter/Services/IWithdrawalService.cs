using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PetSitter.DTOs;
using PetSitter.Models;

namespace PetSitter.Services
{
    public interface IWithdrawalService
    {
        Task<SitterBankAccountDto> GetSitterBankAccountAsync(string sitterUserId, DateTime nowUtc);
        Task<SitterBankAccountDto> UpdateSitterBankAccountAsync(string sitterUserId, UpdateBankAccountDto dto, DateTime nowUtc);
        Task<WithdrawalItemDto> CreateWithdrawalRequestAsync(string sitterUserId, CreateWithdrawalDto dto, string idempotencyKey, DateTime nowUtc);
        Task<List<WithdrawalItemDto>> GetMyWithdrawalsAsync(string sitterUserId, int limit = 50);
        Task<WithdrawalItemDto> CancelWithdrawalRequestAsync(string sitterUserId, string withdrawalId, DateTime nowUtc);

        Task<List<WithdrawalItemDto>> GetAdminWithdrawalsAsync(string? status = null, string? sitterUserId = null, int limit = 50);
        Task<WithdrawalItemDto> ApproveWithdrawalAsync(string adminUserId, string withdrawalId, DateTime nowUtc);
        Task<WithdrawalItemDto> ClaimProcessingAsync(string adminUserId, string adminName, string withdrawalId, DateTime nowUtc);
        Task<WithdrawalItemDto> ConfirmPaidAsync(string adminUserId, string withdrawalId, ConfirmPaidDto dto, DateTime nowUtc);
        Task<WithdrawalItemDto> RejectWithdrawalAsync(string adminUserId, string withdrawalId, RejectWithdrawalDto dto, DateTime nowUtc);
        Task<WithdrawalItemDto> QueryAndProcessPayoutAsync(string adminUserId, string withdrawalId, DateTime nowUtc);
        Task<(int FixedAccountsCount, List<string> Logs)> ReconcileReservedAmountAsync(string? specificUserId = null);
        Task<WalletSummaryDto> GetWalletSummaryAsync(string sitterUserId, DateTime nowUtc);
        Task<DecryptedAccountDto?> GetDecryptedBankAccountAsync(string sitterUserId);
        Task<DecryptedAccountDto?> GetDecryptedWithdrawalBankAsync(string withdrawalId);
    }
}
