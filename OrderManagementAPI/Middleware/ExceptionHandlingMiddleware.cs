using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderManagementAPI.Exceptions;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace OrderManagementAPI.Middleware
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                await HandleExceptionAsync(context, ex);
            }
        }

        private static Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            context.Response.ContentType = "application/json";

            var statusCode = StatusCodes.Status500InternalServerError;
            var message = "Terjadi kesalahan internal pada server.";

            if (exception is DomainException domainEx)
            {
                statusCode = domainEx.StatusCode;
                message = domainEx.Message;
            }
            else if (exception is DbUpdateConcurrencyException)
            {
                statusCode = StatusCodes.Status409Conflict;
                message = "Data telah diubah oleh permintaan lain. Silakan muat ulang.";
            }
            else if (exception is DbUpdateException dbEx && dbEx.InnerException != null && dbEx.InnerException.Message.Contains("unique", StringComparison.OrdinalIgnoreCase))
            {
                statusCode = StatusCodes.Status409Conflict;
                message = "Terjadi konflik karena data duplikat.";
            }

            context.Response.StatusCode = statusCode;

            var correlationId = context.Response.Headers["X-Correlation-ID"].ToString();

            var response = new
            {
                success = false,
                statusCode = statusCode,
                message = message,
                correlationId = correlationId
            };

            return context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
