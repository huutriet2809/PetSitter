using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace PetSitter.Services
{
    public class FakePaymentGateway : IPaymentGateway
    {
        public const string DefaultFakeChecksumKey = "fake_test_checksum_key_1234567890";
        public string ChecksumKey { get; set; } = DefaultFakeChecksumKey;

        public ConcurrentDictionary<long, GatewayPaymentStatusResponse> StatusStore { get; } = new();
        public ConcurrentBag<long> CreatedOrders { get; } = new();
        public ConcurrentBag<long> CancelledOrders { get; } = new();

        public int OutgoingCallCount = 0;
        public int PayoutCallCount = 0;
        public bool SimulateFailureOnCreate { get; set; } = false;

        public Task<CreatePaymentLinkResponse> CreatePaymentLinkAsync(CreatePaymentLinkRequest request)
        {
            System.Threading.Interlocked.Increment(ref OutgoingCallCount);

            if (SimulateFailureOnCreate)
            {
                throw new InvalidOperationException("Mô phỏng lỗi mạng khi kết nối PayOS Gateway.");
            }

            CreatedOrders.Add(request.OrderCode);
            StatusStore[request.OrderCode] = new GatewayPaymentStatusResponse
            {
                OrderCode = request.OrderCode,
                Amount = request.Amount,
                Status = "PENDING"
            };

            return Task.FromResult(new CreatePaymentLinkResponse
            {
                PaymentLinkId = $"fake_pl_{request.OrderCode}",
                CheckoutUrl = $"https://pay.pawstay.vn/checkout/{request.OrderCode}",
                QrCode = $"vietqr://fake-qr/{request.OrderCode}",
                Status = "PENDING"
            });
        }

        public Task CancelPaymentLinkAsync(long orderCode, string? cancellationReason = null)
        {
            System.Threading.Interlocked.Increment(ref OutgoingCallCount);
            CancelledOrders.Add(orderCode);

            if (StatusStore.TryGetValue(orderCode, out var st))
            {
                st.Status = "CANCELLED";
            }

            return Task.CompletedTask;
        }

        public Task<GatewayPaymentStatusResponse?> GetPaymentStatusAsync(long orderCode)
        {
            System.Threading.Interlocked.Increment(ref OutgoingCallCount);
            StatusStore.TryGetValue(orderCode, out var status);
            return Task.FromResult(status);
        }

        public bool VerifyWebhookSignature(string rawDataJson, string signature)
        {
            if (signature == "MOCK_VALID_SIGNATURE") return true;
            return PayOsGateway.VerifyWebhookSignatureStatic(rawDataJson, signature, ChecksumKey);
        }
    }
}
