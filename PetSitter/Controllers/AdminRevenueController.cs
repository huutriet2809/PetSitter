using System;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PetSitter.DTOs;
using PetSitter.Services;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(Roles = "ADMIN,Admin")]
    public class AdminRevenueController : ControllerBase
    {
        private readonly IRevenueService _revenueService;

        public AdminRevenueController(IRevenueService revenueService)
        {
            _revenueService = revenueService;
        }

        [HttpGet("revenue/overview")]
        public async Task<IActionResult> GetRevenueOverview()
        {
            var res = await _revenueService.GetRevenueOverviewAsync(DateTime.UtcNow);
            return Ok(res);
        }

        [HttpPost("revenue/sweeps")]
        public async Task<IActionResult> CreateRevenueSweep([FromBody] CreateSweepDto dto)
        {
            var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminUserId)) return Unauthorized();

            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var res = await _revenueService.CreateRevenueSweepAsync(adminUserId, dto, ip, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpGet("revenue/sweeps")]
        public async Task<IActionResult> GetRevenueSweeps([FromQuery] int limit = 50)
        {
            if (limit <= 0 || limit > 100) limit = 50;
            var list = await _revenueService.GetRevenueSweepsAsync(limit);
            return Ok(list);
        }

        [HttpPost("reconciliation/run")]
        public async Task<IActionResult> RunReconciliation([FromBody] ReconciliationRequestDto? requestDto)
        {
            var adminUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(adminUserId)) return Unauthorized();

            var res = await _revenueService.RunReconciliationAsync(adminUserId, requestDto, DateTime.UtcNow);
            return Ok(res);
        }

        [HttpGet("reconciliation/history")]
        public async Task<IActionResult> GetReconciliationHistory([FromQuery] int limit = 30)
        {
            if (limit <= 0 || limit > 100) limit = 30;
            var list = await _revenueService.GetReconciliationHistoryAsync(limit);
            return Ok(list);
        }

        [HttpGet("revenue/monthly-report")]
        public async Task<IActionResult> GetMonthlyReport([FromQuery] int year, [FromQuery] int month)
        {
            if (year < 2020 || year > 2100 || month < 1 || month > 12)
            {
                var now = DateTime.UtcNow;
                year = now.Year;
                month = now.Month;
            }

            var report = await _revenueService.GetMonthlyReportAsync(year, month, DateTime.UtcNow);
            return Ok(report);
        }

        [HttpGet("revenue/monthly-report/export-csv")]
        public async Task<IActionResult> ExportMonthlyReportCsv([FromQuery] int year, [FromQuery] int month)
        {
            if (year < 2020 || year > 2100 || month < 1 || month > 12)
            {
                var now = DateTime.UtcNow;
                year = now.Year;
                month = now.Month;
            }

            var csvContent = await _revenueService.ExportMonthlyReportCsvAsync(year, month, DateTime.UtcNow);
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csvContent)).ToArray();
            var fileName = $"MonthlyReport_{year}_{month:D2}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        [HttpPost("reconciliation/upload-statement")]
        public async Task<IActionResult> UploadBankStatement(Microsoft.AspNetCore.Http.IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "Vui lòng tải lên tệp sao kê ngân hàng hợp lệ." });
            }

            // Giới hạn kích thước tối đa 5MB
            const long maxSizeBytes = 5 * 1024 * 1024;
            if (file.Length > maxSizeBytes)
            {
                return BadRequest(new { message = "Kích thước tệp sao kê vượt quá giới hạn cho phép (tối đa 5MB)." });
            }

            // Kiểm tra định dạng .csv
            var ext = System.IO.Path.GetExtension(file.FileName);
            if (!string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { message = "Chỉ chấp nhận tệp định dạng bảng tính CSV (.csv)." });
            }

            using var reader = new System.IO.StreamReader(file.OpenReadStream(), Encoding.UTF8);
            var content = await reader.ReadToEndAsync();
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            return Ok(new
            {
                success = true,
                fileName = file.FileName,
                fileSizeBytes = file.Length,
                totalLines = lines.Length,
                message = $"Tải lên tệp sao kê thành công ({lines.Length} dòng). Sẵn sàng đối chiếu."
            });
        }
    }
}
