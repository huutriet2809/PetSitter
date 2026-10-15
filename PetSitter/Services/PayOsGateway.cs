using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PetSitter.Services
{
    public class PayOsGateway : IPaymentGateway
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly ILogger<PayOsGateway> _logger;
        private readonly string _clientId;
        private readonly string _apiKey;
        private readonly string _checksumKey;
        private readonly string _baseUrl;

        public PayOsGateway(HttpClient httpClient, IConfiguration config, ILogger<PayOsGateway> logger)
        {
            _httpClient = httpClient;
            _config = config;
            _logger = logger;
            _clientId = _config["Payments:PayOs:ClientId"] ?? string.Empty;
            _apiKey = _config["Payments:PayOs:ApiKey"] ?? string.Empty;
            _checksumKey = _config["Payments:PayOs:ChecksumKey"] ?? string.Empty;
            _baseUrl = _config["Payments:PayOs:BaseUrl"] ?? "https://api-merchant.payos.vn";
        }

        public async Task<CreatePaymentLinkResponse> CreatePaymentLinkAsync(CreatePaymentLinkRequest request)
        {
            // Cắt ngắn description nếu vượt quá 25 ký tự theo quy định PayOS
            var safeDesc = request.Description ?? string.Empty;
            if (safeDesc.Length > 25) safeDesc = safeDesc.Substring(0, 25);

            // Sắp xếp các trường alphabet để tạo signature
            // amount, cancelUrl, description, orderCode, returnUrl
            var signData = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                { "amount", request.Amount.ToString() },
                { "cancelUrl", request.CancelUrl },
                { "description", safeDesc },
                { "orderCode", request.OrderCode.ToString() },
                { "returnUrl", request.ReturnUrl }
            };

            var dataString = string.Join("&", signData.Select(kv => $"{kv.Key}={kv.Value}"));
            var signature = ComputeHmacSha256(dataString, _checksumKey);

            var bodyObj = new
            {
                orderCode = request.OrderCode,
                amount = request.Amount,
                description = safeDesc,
                cancelUrl = request.CancelUrl,
                returnUrl = request.ReturnUrl,
                expiredAt = request.ExpiredAt,
                signature = signature
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");
            var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl.TrimEnd('/')}/v2/payment-requests")
            {
                Content = jsonContent
            };
            reqMsg.Headers.Add("x-client-id", _clientId);
            reqMsg.Headers.Add("x-api-key", _apiKey);

            var response = await _httpClient.SendAsync(reqMsg);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayOS CreatePaymentLink error: {StatusCode} - {Body}", response.StatusCode, responseBody);
                throw new InvalidOperationException($"Lỗi gọi PayOS API: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var code = root.GetProperty("code").GetString();
            if (code != "00")
            {
                var desc = root.TryGetProperty("desc", out var descElem) ? descElem.GetString() : "Unknown error";
                throw new InvalidOperationException($"PayOS từ chối tạo link: {code} - {desc}");
            }

            var data = root.GetProperty("data");
            return new CreatePaymentLinkResponse
            {
                PaymentLinkId = data.TryGetProperty("paymentLinkId", out var plId) ? plId.GetString() ?? "" : "",
                CheckoutUrl = data.TryGetProperty("checkoutUrl", out var coUrl) ? coUrl.GetString() ?? "" : "",
                QrCode = data.TryGetProperty("qrCode", out var qr) ? qr.GetString() ?? "" : "",
                Status = data.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "PENDING"
            };
        }

        public async Task CancelPaymentLinkAsync(long orderCode, string? cancellationReason = null)
        {
            var bodyObj = new
            {
                cancellationReason = cancellationReason ?? "Đơn hết hạn hoặc bị hủy"
            };
            var jsonContent = new StringContent(JsonSerializer.Serialize(bodyObj), Encoding.UTF8, "application/json");
            var reqMsg = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl.TrimEnd('/')}/v2/payment-requests/{orderCode}/cancel")
            {
                Content = jsonContent
            };
            reqMsg.Headers.Add("x-client-id", _clientId);
            reqMsg.Headers.Add("x-api-key", _apiKey);

            var response = await _httpClient.SendAsync(reqMsg);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("PayOS CancelPaymentLink failed for order {OrderCode}: {StatusCode} - {Body}", orderCode, response.StatusCode, body);
            }
        }

        public async Task<GatewayPaymentStatusResponse?> GetPaymentStatusAsync(long orderCode)
        {
            var reqMsg = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl.TrimEnd('/')}/v2/payment-requests/{orderCode}");
            reqMsg.Headers.Add("x-client-id", _clientId);
            reqMsg.Headers.Add("x-api-key", _apiKey);

            var response = await _httpClient.SendAsync(reqMsg);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.GetProperty("code").GetString() != "00")
                return null;

            var data = root.GetProperty("data");
            return new GatewayPaymentStatusResponse
            {
                OrderCode = data.GetProperty("orderCode").GetInt64(),
                Amount = data.GetProperty("amount").GetInt64(),
                Status = data.GetProperty("status").GetString() ?? ""
            };
        }

        public bool VerifyWebhookSignature(string rawDataJson, string signature)
        {
            return VerifyWebhookSignatureStatic(rawDataJson, signature, _checksumKey);
        }

        public static bool VerifyWebhookSignatureStatic(string rawDataJson, string signature, string checksumKey)
        {
            if (string.IsNullOrWhiteSpace(rawDataJson) || string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(checksumKey))
                return false;

            try
            {
                using var doc = JsonDocument.Parse(rawDataJson);
                var root = doc.RootElement;

                // PayOS webhook có thể truyền trực tiếp data element hoặc toàn bộ payload có trường "data"
                JsonElement dataElem;
                if (root.TryGetProperty("data", out var d))
                {
                    dataElem = d;
                }
                else
                {
                    dataElem = root;
                }

                var dict = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var prop in dataElem.EnumerateObject())
                {
                    var valStr = prop.Value.ValueKind switch
                    {
                        JsonValueKind.Null or JsonValueKind.Undefined => "",
                        JsonValueKind.String => prop.Value.GetString() ?? "",
                        JsonValueKind.Number => prop.Value.GetRawText(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        _ => prop.Value.GetRawText()
                    };
                    dict[prop.Name] = valStr;
                }

                var signString = string.Join("&", dict.Select(kv => $"{kv.Key}={kv.Value}"));
                var computedHash = ComputeHmacSha256(signString, checksumKey);

                return CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computedHash.ToLowerInvariant()),
                    Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant())
                );
            }
            catch
            {
                return false;
            }
        }

        public static string ComputeHmacSha256(string data, string key)
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
