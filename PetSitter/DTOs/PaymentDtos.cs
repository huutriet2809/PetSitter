using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PetSitter.DTOs
{
    public class PaymentLinkDto
    {
        public string PaymentId { get; set; } = string.Empty;
        public string BookingId { get; set; } = string.Empty;
        public long OrderCode { get; set; }
        public long Amount { get; set; }
        public string? CheckoutUrl { get; set; }
        public string? QrCode { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
    }

    public class PaymentStatusDto
    {
        public string PaymentStatus { get; set; } = string.Empty;
        public DateTime? PaymentDeadline { get; set; }
        public long TotalAmount { get; set; }
        public long SitterAmount { get; set; }
        public long CommissionAmount { get; set; }
        public string? CheckoutUrl { get; set; }
        public string? QrCode { get; set; }
        public DateTime? PaidAt { get; set; }
        public bool CanPay { get; set; }
    }

    public class SitterEarningsDto
    {
        public long TotalEarnings { get; set; }
        public long AvailableBalance { get; set; }
        public long PendingHoldBalance { get; set; }
        public List<SitterLedgerItemDto> History { get; set; } = new();
    }

    public class SitterLedgerItemDto
    {
        public string TransactionId { get; set; } = string.Empty;
        public string BookingId { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Direction { get; set; } = string.Empty;
        public long Amount { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? AvailableAt { get; set; }
        public bool IsAvailable { get; set; }
    }

    public class AdminPaymentsOverviewDto
    {
        public long TotalCollected { get; set; }       // Tổng tiền đã thu vào ESCROW_HELD
        public long EscrowCurrentlyHeld { get; set; }  // Tiền đang giữ chờ hoàn tất
        public long PlatformRevenue { get; set; }      // Hoa hồng nền tảng đã ghi nhận
        public long GatewayFees { get; set; }          // Phí cổng
        public long SitterEarningsUnwithdrawn { get; set; } // Thu nhập Sitter chưa rút
        public long RefundPendingAmount { get; set; }  // Tiền đang chờ hoàn cho khách
        public int RefundPendingCount { get; set; }    // Số lượng yêu cầu hoàn đang chờ
    }

    public class RefundQueueItemDto
    {
        public string RefundId { get; set; } = string.Empty;
        public string BookingId { get; set; } = string.Empty;
        public string OwnerUserId { get; set; } = string.Empty;
        public string? OwnerName { get; set; }
        public string? OwnerEmail { get; set; }
        public long Amount { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? PayerInfoMasked { get; set; }
        public string? BankTransactionRef { get; set; }
    }

    public class CompleteRefundDto
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Mã tham chiếu giao dịch ngân hàng là bắt buộc.")]
        [StringLength(100, MinimumLength = 3, ErrorMessage = "Mã tham chiếu từ 3 đến 100 ký tự.")]
        public string BankTransactionRef { get; set; } = string.Empty;
    }

    public class UpdateRefundAccountDto
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "Tên ngân hàng là bắt buộc.")]
        public string BankName { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "Số tài khoản là bắt buộc.")]
        public string AccountNumber { get; set; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "Tên chủ tài khoản là bắt buộc.")]
        public string AccountName { get; set; } = string.Empty;
    }

    // PayOS Webhook Payload structure
    public class PayosWebhookPayload
    {
        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("success")]
        public bool? Success { get; set; }

        [JsonPropertyName("data")]
        public PayosWebhookData? Data { get; set; }

        [JsonPropertyName("signature")]
        public string? Signature { get; set; }
    }

    public class PayosWebhookData
    {
        [JsonPropertyName("orderCode")]
        public long OrderCode { get; set; }

        [JsonPropertyName("amount")]
        public long Amount { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("accountNumber")]
        public string? AccountNumber { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("transactionDateTime")]
        public string? TransactionDateTime { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("paymentLinkId")]
        public string? PaymentLinkId { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("counterAccountBankId")]
        public string? CounterAccountBankId { get; set; }

        [JsonPropertyName("counterAccountBankName")]
        public string? CounterAccountBankName { get; set; }

        [JsonPropertyName("counterAccountName")]
        public string? CounterAccountName { get; set; }

        [JsonPropertyName("counterAccountNumber")]
        public string? CounterAccountNumber { get; set; }

        [JsonPropertyName("virtualAccountName")]
        public string? VirtualAccountName { get; set; }

        [JsonPropertyName("virtualAccountNumber")]
        public string? VirtualAccountNumber { get; set; }
    }
}
