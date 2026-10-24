using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSitter.DTOs;
using PetSitter.Services;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/admin/withdrawals")]
    [Authorize(Roles = "ADMIN,Admin")]
    public class AdminWithdrawalsController : ControllerBase
    {
        private readonly IWithdrawalService _withdrawalService;

        public AdminWithdrawalsController(IWithdrawalService withdrawalService)
        {
            _withdrawalService = withdrawalService;
        }

        [HttpGet]
        public async Task<IActionResult> GetWithdrawals([FromQuery] string? status = null, [FromQuery] string? sitterUserId = null, [FromQuery] int limit = 50)
        {
            if (limit <= 0 || limit > 100) limit = 50;
            var list = await _withdrawalService.GetAdminWithdrawalsAsync(status, sitterUserId, limit);
            return Ok(list);
        }

        [HttpPost("{id}/approve")]
        public async Task<IActionResult> ApproveWithdrawal(string id)
        {
            var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminUserId)) return Unauthorized();

            var res = await _withdrawalService.ApproveWithdrawalAsync(adminUserId, id, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpPost("{id}/claim-processing")]
        public async Task<IActionResult> ClaimProcessing(string id)
        {
            var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var adminName = User.FindFirstValue(ClaimTypes.Name) ?? adminUserId ?? "Admin";
            if (string.IsNullOrEmpty(adminUserId)) return Unauthorized();

            var res = await _withdrawalService.ClaimProcessingAsync(adminUserId, adminName, id, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpPost("{id}/confirm-paid")]
        public async Task<IActionResult> ConfirmPaid(string id, [FromBody] ConfirmPaidDto dto)
        {
            var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminUserId)) return Unauthorized();

            var res = await _withdrawalService.ConfirmPaidAsync(adminUserId, id, dto, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpPost("{id}/reject")]
        public async Task<IActionResult> RejectWithdrawal(string id, [FromBody] RejectWithdrawalDto dto)
        {
            var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminUserId)) return Unauthorized();

            var res = await _withdrawalService.RejectWithdrawalAsync(adminUserId, id, dto, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpPost("{id}/query-payout")]
        public async Task<IActionResult> QueryPayout(string id)
        {
            var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminUserId)) return Unauthorized();

            var res = await _withdrawalService.QueryAndProcessPayoutAsync(adminUserId, id, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpGet("{id}/decrypted-bank")]
        public async Task<IActionResult> GetDecryptedBank(string id)
        {
            var res = await _withdrawalService.GetDecryptedWithdrawalBankAsync(id);
            if (res == null) return NotFound();
            return Ok(res);
        }
    }
}
