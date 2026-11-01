using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;

namespace PetSitter.Controllers
{
    [ApiController]
    [Route("api/csp-report")]
    [AllowAnonymous]
    [EnableRateLimiting("csp-report")]
    public class CspReportController : ControllerBase
    {
        private readonly ILogger<CspReportController> _logger;
        private const int MaxBodyLength = 10 * 1024; // 10 KB

        public CspReportController(ILogger<CspReportController> logger)
        {
            _logger = logger;
        }

        [HttpPost]
        [RequestSizeLimit(MaxBodyLength)]
        [Consumes("application/csp-report", "application/reports+json", "application/json", "text/plain")]
        public async Task<IActionResult> ReceiveReport()
        {
            if (Request.ContentLength.HasValue && Request.ContentLength.Value > MaxBodyLength)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            if (body.Length > MaxBodyLength)
            {
                return StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                return NoContent();
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Chuẩn CSP 1.0/2.0 bọc trong "csp-report", Reporting API (CSP 3.0) là array hoặc object
                JsonElement reportElement = root;
                if (root.TryGetProperty("csp-report", out var cspReport))
                {
                    reportElement = cspReport;
                }
                else if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    reportElement = root[0];
                    if (reportElement.TryGetProperty("body", out var repBody))
                    {
                        reportElement = repBody;
                    }
                }

                string GetCleanProperty(string propName)
                {
                    if (reportElement.TryGetProperty(propName, out var el) && el.ValueKind == JsonValueKind.String)
                    {
                        return SanitizeForLog(el.GetString());
                    }
                    return string.Empty;
                }

                var documentUri = GetCleanProperty("document-uri");
                if (string.IsNullOrEmpty(documentUri)) documentUri = GetCleanProperty("documentURL");

                var violatedDirective = GetCleanProperty("violated-directive");
                var effectiveDirective = GetCleanProperty("effective-directive");

                var blockedUri = GetCleanProperty("blocked-uri");
                if (string.IsNullOrEmpty(blockedUri)) blockedUri = GetCleanProperty("blockedURL");

                var rawIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                var clientIp = SanitizeForLog(rawIp, 50);

                var directive = !string.IsNullOrEmpty(effectiveDirective) ? effectiveDirective : violatedDirective;

                _logger.LogWarning(
                    "[CSP-REPORT] Vi phạm CSP phát hiện từ IP {ClientIp} | Trang: {DocumentUri} | Chỉ thị: {Directive} | Bị chặn: {BlockedUri}",
                    clientIp,
                    documentUri,
                    directive,
                    blockedUri
                );
            }
            catch (JsonException)
            {
                _logger.LogDebug("Nhận payload CSP Report không đúng định dạng JSON.");
            }

            return NoContent();
        }

        public static string SanitizeForLog(string? input, int maxLength = 200)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            if (input.Length > maxLength)
            {
                input = input.Substring(0, maxLength);
            }

            // Loại bỏ ký tự điều khiển (CR, LF, Tab, NULL, v.v.) để chống Log Injection / CRLF Injection
            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                if (!char.IsControl(c))
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append(' ');
                }
            }
            return sb.ToString().Trim();
        }
    }
}

