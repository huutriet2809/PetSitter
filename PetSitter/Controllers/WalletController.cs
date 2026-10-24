using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSitter.DTOs;
using PetSitter.Exceptions;
using PetSitter.Services;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/wallet")]
    [Authorize]
    public class WalletController : ControllerBase
    {
        private readonly IWithdrawalService _withdrawalService;
        private readonly ILedgerService _ledgerService;

        public WalletController(IWithdrawalService withdrawalService, ILedgerService ledgerService)
        {
            _withdrawalService = withdrawalService;
            _ledgerService = ledgerService;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var res = await _withdrawalService.GetWalletSummaryAsync(userId, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpGet("history")]
        public async Task<IActionResult> GetHistory([FromQuery] int limit = 50)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var earnings = await _ledgerService.GetSitterEarningsAsync(userId, DateTime.UtcNow, limit);
            return Ok(earnings.History);
        }

        [HttpGet("bank-account")]
        public async Task<IActionResult> GetBankAccount()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var res = await _withdrawalService.GetSitterBankAccountAsync(userId, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpPut("bank-account")]
        public async Task<IActionResult> UpdateBankAccount([FromBody] UpdateBankAccountDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var res = await _withdrawalService.UpdateSitterBankAccountAsync(userId, dto, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpPost("withdrawals")]
        [HttpPost("withdraw")]
        public async Task<IActionResult> CreateWithdrawal([FromBody] CreateWithdrawalDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new BadRequestException("Header Idempotency-Key là bắt buộc khi tạo yêu cầu rút tiền.");
            }

            var res = await _withdrawalService.CreateWithdrawalRequestAsync(userId, dto, idempotencyKey, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpGet("withdrawals")]
        public async Task<IActionResult> GetMyWithdrawals([FromQuery] int limit = 50)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            if (limit <= 0 || limit > 100) limit = 50;
            var res = await _withdrawalService.GetMyWithdrawalsAsync(userId, limit);
            return Ok(res);
        }

        [HttpPost("withdrawals/{id}/cancel")]
        public async Task<IActionResult> CancelWithdrawal(string id)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId)) return Unauthorized();

            var res = await _withdrawalService.CancelWithdrawalRequestAsync(userId, id, DateTime.UtcNow);
            return Ok(res);
        }
    }
}
