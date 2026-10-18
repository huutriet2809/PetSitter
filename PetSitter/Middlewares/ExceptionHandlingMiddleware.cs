using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PetSitter.Exceptions;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace PetSitter.Middlewares
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (PriceMismatchException ex)
            {
                _logger.LogWarning(ex, "Handled price mismatch exception: {Message}", ex.Message);
                if (!context.Response.HasStarted)
                {
                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.StatusCode = ex.StatusCode;
                    var json = JsonSerializer.Serialize(new { message = ex.Message, newTotal = ex.NewTotal, currentPrice = ex.NewTotal });
                    await context.Response.WriteAsync(json);
                }
            }
            catch (AppException ex)
            {
                _logger.LogWarning(ex, "Handled application exception: {Message}", ex.Message);
                await HandleExceptionAsync(context, ex.StatusCode, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled server exception: {Message}", ex.Message);
                await HandleExceptionAsync(context, StatusCodes.Status500InternalServerError, "Đã có lỗi xảy ra trên hệ thống. Vui lòng thử lại sau.");
            }
        }

        private static async Task HandleExceptionAsync(HttpContext context, int statusCode, string message)
        {
            if (context.Response.HasStarted) return;

            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = statusCode;

            var response = new { message };
            var json = JsonSerializer.Serialize(response);
            await context.Response.WriteAsync(json);
        }
    }
}
