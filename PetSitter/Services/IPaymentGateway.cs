using System;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public class CreatePaymentLinkRequest
    {
        public long OrderCode { get; set; }
        public long Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;
        public string CancelUrl { get; set; } = string.Empty;
        public long ExpiredAt { get; set; } // Unix timestamp in seconds
    }

    public class CreatePaymentLinkResponse
    {
        public string PaymentLinkId { get; set; } = string.Empty;
        public string CheckoutUrl { get; set; } = string.Empty;
        public string QrCode { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    public class GatewayPaymentStatusResponse
    {
        public long OrderCode { get; set; }
        public long Amount { get; set; }
        public string Status { get; set; } = string.Empty; // PENDING, PAID, CANCELLED, EXPIRED
    }

    public interface IPaymentGateway
    {
        Task<CreatePaymentLinkResponse> CreatePaymentLinkAsync(CreatePaymentLinkRequest request);
        Task CancelPaymentLinkAsync(long orderCode, string? cancellationReason = null);
        Task<GatewayPaymentStatusResponse?> GetPaymentStatusAsync(long orderCode);
        bool VerifyWebhookSignature(string rawDataJson, string signature);
    }
}
