using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OrderManagementAPI.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OrderManagementAPI.Middleware
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private const string CORR_ID_HEADER = "X-Correlation-ID";

        public CorrelationIdMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
        {
            if (!context.Request.Headers.TryGetValue(CORR_ID_HEADER, out var corrId))
            {
                corrId = Guid.NewGuid().ToString();
            }

            context.Response.Headers[CORR_ID_HEADER] = corrId;
            CorrelationContext.CorrelationId = corrId.ToString();

            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = corrId.ToString() }))
            {
                await _next(context);
            }
        }
    }
}
