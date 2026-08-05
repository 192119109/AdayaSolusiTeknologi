using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OrderManagementAPI.Services;
using System.Threading.Tasks;

namespace OrderManagementAPI.Filters
{
    public class IdempotencyFilter : IAsyncActionFilter
    {
        private readonly IdempotencyService _idempotencyService;

        public IdempotencyFilter(IdempotencyService idempotencyService)
        {
            _idempotencyService = idempotencyService;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (context.HttpContext.Request.Method != HttpMethods.Post)
            {
                await next();
                return;
            }

            if (!context.HttpContext.Request.Headers.TryGetValue("Idempotency-Key", out var keyVal) || string.IsNullOrEmpty(keyVal))
            {
                context.Result = new BadRequestObjectResult(new { success = false, message = "Header Idempotency-key wajib ada" });
                return;
            }

            string key = keyVal.ToString();
            var res = await _idempotencyService.CheckOrReserveAsync(key);

            if (!res.IsNew)
            {
                if (res.Status == "Processing")
                {
                    context.Result = new ConflictObjectResult(new { success = false, message = "Permintaan dengan Idempotency key yang sama sedang diproses." });
                    return;
                }

                context.Result = new ContentResult
                {
                    StatusCode = res.ResponseStatusCode,
                    Content = res.ResponseBody,
                    ContentType = "application/json"
                };
                return;
            }

            ActionExecutedContext executedContext;
            try
            {
                executedContext = await next();
            }
            catch
            {
                await _idempotencyService.RemoveRequestAsync(key);
                throw;
            }

            if (executedContext.Exception != null)
            {
                await _idempotencyService.RemoveRequestAsync(key);
                return;
            }

            int status_code = StatusCodes.Status200OK;
            string resBody = string.Empty;

            if (executedContext.Result is ObjectResult objectResult)
            {
                status_code = objectResult.StatusCode ?? StatusCodes.Status200OK;
                resBody = System.Text.Json.JsonSerializer.Serialize(objectResult.Value);
            }
            else if (executedContext.Result is StatusCodeResult statusCodeResult)
            {
                status_code = statusCodeResult.StatusCode;
            }
            else if (executedContext.Result is ContentResult contentResult)
            {
                status_code = contentResult.StatusCode ?? StatusCodes.Status200OK;
                resBody = contentResult.Content ?? string.Empty;
            }

            if (status_code >= 200 && status_code < 300)
            {
                await _idempotencyService.CompleteRequestAsync(key, status_code, resBody);
            }
            else
            {
                await _idempotencyService.RemoveRequestAsync(key);
            }
        }
    }
}
