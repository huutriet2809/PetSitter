using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using PetSitter.Controllers;
using Xunit;

namespace PetSitter.Tests
{
    public class CspReportTests
    {
        private readonly CspReportController _controller;

        public CspReportTests()
        {
            _controller = new CspReportController(NullLogger<CspReportController>.Instance);
        }

        [Fact]
        public async Task ReceiveReport_ValidCspReport_ReturnsNoContent()
        {
            var json = @"{
                ""csp-report"": {
                    ""document-uri"": ""https://pawstay.vn/login.html"",
                    ""violated-directive"": ""script-src 'self'"",
                    ""blocked-uri"": ""inline"",
                    ""line-number"": 42
                }
            }";

            var httpContext = new DefaultHttpContext();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            httpContext.Request.Body = stream;
            httpContext.Request.ContentLength = stream.Length;
            _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var result = await _controller.ReceiveReport();

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task ReceiveReport_EmptyBody_ReturnsNoContent()
        {
            var httpContext = new DefaultHttpContext();
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(""));
            httpContext.Request.Body = stream;
            httpContext.Request.ContentLength = 0;
            _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var result = await _controller.ReceiveReport();

            Assert.IsType<NoContentResult>(result);
        }

        [Fact]
        public async Task ReceiveReport_BodyLargerThan10KB_Returns413PayloadTooLarge()
        {
            var httpContext = new DefaultHttpContext();
            var oversizedData = new byte[10241]; // 10KB + 1 byte
            var stream = new MemoryStream(oversizedData);
            httpContext.Request.Body = stream;
            httpContext.Request.ContentLength = oversizedData.Length;
            _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var result = await _controller.ReceiveReport();

            var statusResult = Assert.IsType<StatusCodeResult>(result);
            Assert.Equal(StatusCodes.Status413PayloadTooLarge, statusResult.StatusCode);
        }
        [Fact]
        public void SanitizeForLog_RemovesControlCharactersAndNewlines_PreventsLogInjection()
        {
            var maliciousInput = "login.html\r\n[CRITICAL] Admin account breached!\nFakeLogLine: 123\tInjected";
            var sanitized = CspReportController.SanitizeForLog(maliciousInput);

            Assert.DoesNotContain("\r", sanitized);
            Assert.DoesNotContain("\n", sanitized);
            Assert.DoesNotContain("\t", sanitized);
            Assert.Equal("login.html  [CRITICAL] Admin account breached! FakeLogLine: 123 Injected", sanitized);
        }

        [Fact]
        public void SanitizeForLog_TruncatesLongString_ToMaxLength()
        {
            var longString = new string('A', 350);
            var sanitized = CspReportController.SanitizeForLog(longString, 200);

            Assert.Equal(200, sanitized.Length);
        }

        [Theory]
        [InlineData("application/csp-report")]
        [InlineData("application/reports+json")]
        [InlineData("application/json")]
        public async Task ReceiveReport_AcceptsVariousCspContentTypes(string contentType)
        {
            var json = @"{
                ""csp-report"": {
                    ""document-uri"": ""https://pawstay.vn/signup.html"",
                    ""violated-directive"": ""style-src 'self'""
                }
            }";

            var httpContext = new DefaultHttpContext();
            httpContext.Request.ContentType = contentType;
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            httpContext.Request.Body = stream;
            httpContext.Request.ContentLength = stream.Length;
            _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

            var result = await _controller.ReceiveReport();
            Assert.IsType<NoContentResult>(result);
        }
    }
}

