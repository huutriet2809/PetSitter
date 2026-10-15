using System;
using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Services;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api")]
    public class PaymentsController : ControllerBase
    {
        private readonly IPaymentService _paymentService;
        private readonly ILedgerService _ledgerService;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(
            IPaymentService paymentService,
            ILedgerService ledgerService,
            ILogger<PaymentsController> logger)
        {
            _paymentService = paymentService;
            _ledgerService = ledgerService;
            _logger = logger;
        }

        /// <summary>
        /// Tạo hoặc lấy lại link thanh toán PayOS đang hiệu lực (Owner của đơn)
        /// </summary>
        [HttpPost("bookings/{id}/pay")]
        [Authorize]
        public async Task<IActionResult> CreateOrGetPaymentLink(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var result = await _paymentService.CreateOrGetPaymentLinkAsync(userId, id, DateTime.UtcNow);
            return Ok(result);
        }

        /// <summary>
        /// Lấy thông tin trạng thái thanh toán của đơn (Owner, Sitter của đơn, Admin)
        /// </summary>
        [HttpGet("bookings/{id}/payment")]
        [Authorize]
        public async Task<IActionResult> GetBookingPaymentStatus(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
            var result = await _paymentService.GetBookingPaymentStatusAsync(userId, role, id);
            return Ok(result);
        }

        /// <summary>
        /// Webhook nhận thông báo kết quả thanh toán từ PayOS (VietQR)
        /// Bắt buộc xác thực chữ ký HMAC-SHA256 trước mọi truy vấn DB.
        /// </summary>
        [HttpPost("payments/payos/webhook")]
        [AllowAnonymous]
        [DisableRateLimiting]
        public async Task<IActionResult> PayOsWebhook()
        {
            string rawBody;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return BadRequest(new { message = "Dữ liệu webhook rỗng." });
            }

            string? signature = Request.Headers["x-signature"].FirstOrDefault();
            if (string.IsNullOrEmpty(signature))
            {
                try
                {
                    using var doc = JsonDocument.Parse(rawBody);
                    if (doc.RootElement.TryGetProperty("signature", out var sigProp))
                    {
                        signature = sigProp.GetString();
                    }
                }
                catch
                {
                    return BadRequest(new { message = "Định dạng JSON webhook không hợp lệ." });
                }
            }

            if (string.IsNullOrEmpty(signature))
            {
                return BadRequest(new { message = "Thiếu chữ ký xác thực webhook." });
            }

            try
            {
                await _paymentService.ProcessWebhookAsync(rawBody, signature, DateTime.UtcNow);
                return Ok(new { code = "00", message = "Xử lý webhook thành công." });
            }
            catch (BadRequestException ex)
            {
                _logger.LogWarning("Webhook failed validation: {Message}", ex.Message);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi nội bộ khi xử lý PayOS webhook.");
                return StatusCode(500, new { message = "Lỗi xử lý webhook." });
            }
        }

        /// <summary>
        /// Lấy thông tin thu nhập của Sitter (tổng, khả dụng, đang treo, lịch sử)
        /// </summary>
        [HttpGet("sitter/earnings")]
        [Authorize(Roles = "Sitter")]
        public async Task<IActionResult> GetSitterEarnings([FromQuery] int limit = 50)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (limit <= 0 || limit > 100) limit = 50;

            var earnings = await _ledgerService.GetSitterEarningsAsync(userId, DateTime.UtcNow, limit);
            return Ok(earnings);
        }

        /// <summary>
        /// Tổng quan tài chính & thanh toán hệ thống (Admin)
        /// </summary>
        [HttpGet("admin/payments/overview")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAdminPaymentsOverview()
        {
            var overview = await _ledgerService.GetAdminPaymentsOverviewAsync(DateTime.UtcNow);
            return Ok(overview);
        }

        /// <summary>
        /// Danh sách hàng chờ hoàn tiền REFUND_PENDING (Admin)
        /// </summary>
        [HttpGet("admin/refunds")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAdminRefundQueue([FromQuery] int limit = 50)
        {
            if (limit <= 0 || limit > 100) limit = 50;
            var list = await _paymentService.GetRefundQueueAsync(limit);
            return Ok(list);
        }

        /// <summary>
        /// Xác nhận đã hoàn tiền thủ công qua ngân hàng (Admin)
        /// </summary>
        [HttpPost("admin/refunds/{id}/complete")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CompleteRefund(string id, [FromBody] CompleteRefundDto dto)
        {
            var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminUserId)) return Unauthorized();

            await _paymentService.CompleteRefundAsync(adminUserId, id, dto, DateTime.UtcNow);
            return Ok(new { message = "Xác nhận hoàn tiền thành công." });
        }

        /// <summary>
        /// Owner cập nhật tài khoản nhận hoàn tiền nếu webhook không có thông tin counterAccount
        /// </summary>
        [HttpPost("bookings/{id}/refund-account")]
        [Authorize]
        public async Task<IActionResult> UpdateRefundAccount(string id, [FromBody] UpdateRefundAccountDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            await _paymentService.UpdateRefundAccountAsync(userId, id, dto);
            return Ok(new { message = "Cập nhật tài khoản nhận hoàn tiền thành công." });
        }
    }
}
