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

        public string SimulatePayoutStatus { get; set; } = "SUCCESS"; // SUCCESS, FAILED, TIMEOUT
        public ConcurrentDictionary<string, GatewayPayoutStatusResponse> PayoutStore { get; } = new();

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

        public Task<CreatePayoutResponse> CreatePayoutAsync(CreatePayoutRequest request)
        {
            System.Threading.Interlocked.Increment(ref PayoutCallCount);

            if (SimulatePayoutStatus == "TIMEOUT")
            {
                var timeoutRes = new GatewayPayoutStatusResponse
                {
                    PayoutId = $"payout_{request.ReferenceId}",
                    ReferenceId = request.ReferenceId,
                    Status = "PROCESSING",
                    ErrorMessage = "Gateway timeout/processing"
                };
                PayoutStore[request.ReferenceId] = timeoutRes;

                return Task.FromResult(new CreatePayoutResponse
                {
                    PayoutId = timeoutRes.PayoutId,
                    ReferenceId = request.ReferenceId,
                    Status = "PROCESSING",
                    ErrorMessage = "Gateway timeout"
                });
            }

            if (SimulatePayoutStatus == "FAILED")
            {
                var failRes = new GatewayPayoutStatusResponse
                {
                    PayoutId = $"payout_{request.ReferenceId}",
                    ReferenceId = request.ReferenceId,
                    Status = "FAILED",
                    ErrorMessage = "Tài khoản thụ hưởng không hợp lệ hoặc số dư nguồn chi không đủ."
                };
                PayoutStore[request.ReferenceId] = failRes;

                return Task.FromResult(new CreatePayoutResponse
                {
                    PayoutId = failRes.PayoutId,
                    ReferenceId = request.ReferenceId,
                    Status = "FAILED",
                    ErrorMessage = failRes.ErrorMessage
                });
            }

            // Mặc định SUCCESS
            var txRef = "BNK" + DateTime.UtcNow.ToString("yyMMddHHmmss") + Random.Shared.Next(100, 999);
            var successRes = new GatewayPayoutStatusResponse
            {
                PayoutId = $"payout_{request.ReferenceId}",
                ReferenceId = request.ReferenceId,
                Status = "SUCCESS",
                TransactionRef = txRef
            };
            PayoutStore[request.ReferenceId] = successRes;

            return Task.FromResult(new CreatePayoutResponse
            {
                PayoutId = successRes.PayoutId,
                ReferenceId = request.ReferenceId,
                Status = "SUCCESS",
                TransactionRef = txRef
            });
        }

        public Task<GatewayPayoutStatusResponse?> GetPayoutStatusAsync(string referenceId)
        {
            System.Threading.Interlocked.Increment(ref PayoutCallCount);
            PayoutStore.TryGetValue(referenceId, out var status);
            return Task.FromResult(status);
        }
    }
}
