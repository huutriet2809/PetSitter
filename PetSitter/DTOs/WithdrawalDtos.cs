using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PetSitter.DTOs
{
    public class SitterBankAccountDto
    {
        public string BankBin { get; set; } = string.Empty;
        public string BankName { get; set; } = string.Empty;
        public string MaskedAccountNumber { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
        public bool NameMatchesRegisteredName { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool CanWithdraw { get; set; }
        public DateTime? CooldownUntil { get; set; }
    }

    public class UpdateBankAccountDto
    {
        [Required(ErrorMessage = "Mật khẩu xác nhận là bắt buộc.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Mã ngân hàng (BIN) là bắt buộc.")]
        public string BankBin { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tên ngân hàng là bắt buộc.")]
        public string BankName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Số tài khoản ngân hàng là bắt buộc.")]
        [RegularExpression(@"^[0-9A-Za-z]{6,25}$", ErrorMessage = "Số tài khoản từ 6-25 ký tự.")]
        public string AccountNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tên chủ tài khoản là bắt buộc.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Tên chủ tài khoản từ 3-100 ký tự.")]
        public string AccountHolderName { get; set; } = string.Empty;
    }

    public class CreateWithdrawalDto
    {
        [Range(1000, 1000000000, ErrorMessage = "Số tiền rút không hợp lệ.")]
        public long Amount { get; set; }
    }

    public class WithdrawalItemDto
    {
        public string WithdrawalId { get; set; } = string.Empty;
        public string SitterUserId { get; set; } = string.Empty;
        public string? SitterName { get; set; }
        public long Amount { get; set; }
        public string BankName { get; set; } = string.Empty;
        public string MaskedAccountNumber { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
        public bool NameMatchesRegisteredName { get; set; }
        public string Status { get; set; } = string.Empty;
        public string ReferenceId { get; set; } = string.Empty;
        public string? BankTransactionRef { get; set; }
        public string? ProcessingByAdminUserId { get; set; }
        public string? ProcessingByAdminName { get; set; }
        public DateTime? ProcessingAt { get; set; }
        public string? RejectReason { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? PaidAt { get; set; }
        public bool CanClaimProcessing { get; set; }
        public bool CanConfirmPaid { get; set; }
    }

    public class RejectWithdrawalDto
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Lý do từ chối là bắt buộc.")]
        [StringLength(500, MinimumLength = 5, ErrorMessage = "Lý do từ chối từ 5 đến 500 ký tự.")]
        public string Reason { get; set; } = string.Empty;
    }

    public class ConfirmPaidDto
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Mã tham chiếu ngân hàng là bắt buộc.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Mã tham chiếu từ 3 đến 100 ký tự.")]
        public string BankTransactionRef { get; set; } = string.Empty;
    }

    public class RevenueOverviewDto
    {
        public long TotalCollected { get; set; }       // Tổng tiền đã thu vào GATEWAY_CLEARING
        public long EscrowHeld { get; set; }           // Đang giữ ESCROW_HELD
        public long SitterUnwithdrawn { get; set; }     // Thu nhập Sitter chưa rút
        public long PayoutPending { get; set; }        // Đang chờ rút
        public long RefundPending { get; set; }        // Đang chờ hoàn tiền
        public long SuspenseAmount { get; set; }       // Tiền bất thường trong SUSPENSE
        public long PlatformRevenue { get; set; }      // Hoa hồng nền tảng lũy kế
        public long GatewayFees { get; set; }          // Phí cổng nền tảng chịu
        public long NetPlatformRevenue { get; set; }   // Doanh thu ròng = PlatformRevenue - GatewayFees
        public long PlatformSwept { get; set; }        // Đã sweep sang tài khoản vận hành
        public long SweptTotal { get => PlatformSwept; set => PlatformSwept = value; }
        public long RevenueBuffer { get; set; }        // Khoản đệm an toàn
        public long RevenueReserveBuffer { get => RevenueBuffer; set => RevenueBuffer = value; }
        public long TransferableRevenue { get; set; }  // Doanh thu có thể chuyển (sweep)
        public long TransferableAmount { get => TransferableRevenue; set => TransferableRevenue = value; }
        public long PayosPlanQuota { get; set; }       // Hạn mức đơn PayOS
        public long PayosMaxQuota { get => PayosPlanQuota; set => PayosPlanQuota = value; }
        public long PayosLinksUsed { get; set; }       // Số link PayOS đã dùng
        public long PayosLinksCount { get => PayosLinksUsed; set => PayosLinksUsed = value; }
        public long PayosLinksRemaining { get; set; }  // Số link PayOS còn lại
        public bool IsQuotaWarning { get; set; }       // Cảnh báo khi còn <= 15 đơn
        public bool PayosWarning { get => IsQuotaWarning; set => IsQuotaWarning = value; }
        public List<SweepItemDto> RecentSweeps { get; set; } = new();
    }

    public class WalletSummaryDto
    {
        public long TotalEarnings { get; set; }
        public long AvailableBalance { get; set; }
        public long PendingHoldBalance { get; set; }
        public long ReservedAmount { get; set; }
        public long WithdrawnTotal { get; set; }
        public long DailyLimitRemaining { get; set; }
        public bool CanWithdraw { get; set; }
        public DateTime? FreezeUntil { get; set; }
        public SitterBankAccountDto? BankAccount { get; set; }
    }

    public class CreateSweepDto
    {
        [Range(1000, 1000000000, ErrorMessage = "Số tiền chuyển không hợp lệ.")]
        public long Amount { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "Mã tham chiếu ngân hàng là bắt buộc.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Mã tham chiếu từ 3 đến 100 ký tự.")]
        public string BankTransactionRef { get; set; } = string.Empty;

        [StringLength(255, ErrorMessage = "Ghi chú không quá 255 ký tự.")]
        public string? Note { get; set; }
    }

    public class SweepItemDto
    {
        public string SweepId { get; set; } = string.Empty;
        public long Amount { get; set; }
        public string BankTransactionRef { get; set; } = string.Empty;
        public string? Note { get; set; }
        public string AdminUserId { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class ReconciliationRequestDto
    {
        public long? ActualBankBalance { get; set; } // Số dư ngân hàng Admin nhập
    }

    public class MonthlyReportDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public long GrossCommission { get; set; }
        public long GatewayFees { get; set; }
        public long NetRevenue { get; set; }
        public long TotalSwept { get; set; }
        public long RetainedRevenue { get; set; }
        public List<MonthlyTransactionItemDto> Transactions { get; set; } = new();
    }

    public class MonthlyTransactionItemDto
    {
        public string TransactionId { get; set; } = string.Empty;
        public string OrderCode { get; set; } = string.Empty;
        public string BookingId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public long Amount { get; set; }
        public long Fee { get; set; }
        public long Net { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class AssignSuspenseDto
    {
        [Required(ErrorMessage = "Mã đơn đặt lịch là bắt buộc.")]
        public string BookingId { get; set; } = string.Empty;

        [StringLength(500, ErrorMessage = "Ghi chú không quá 500 ký tự.")]
        public string? Note { get; set; }
    }

    public class RefundSuspenseDto
    {
        [Required(ErrorMessage = "Lý do hoàn tiền là bắt buộc.")]
        [StringLength(500, MinimumLength = 5, ErrorMessage = "Lý do từ 5 đến 500 ký tự.")]
        public string Reason { get; set; } = string.Empty;
    }

    public class DecryptedAccountDto
    {
        public string BankName { get; set; } = string.Empty;
        public string BankBin { get; set; } = string.Empty;
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
    }
}
