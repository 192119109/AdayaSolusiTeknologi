using Microsoft.EntityFrameworkCore;
using OrderManagementAPI.Data;
using OrderManagementAPI.Models;
using System;
using System.Threading.Tasks;

namespace OrderManagementAPI.Services
{
    public class IdempotencyResult
    {
        public bool IsNew { get; set; }
        public string Status { get; set; } = string.Empty;
        public int ResponseStatusCode { get; set; }
        public string ResponseBody { get; set; } = string.Empty;
    }

    public class IdempotencyService
    {
        private readonly OrderDbContext _dbContext;

        public IdempotencyService(OrderDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IdempotencyResult> CheckOrReserveAsync(string key)
        {
            var existing = await _dbContext.IdempotentRequests.FindAsync(key);
            if (existing != null)
            {
                return new IdempotencyResult
                {
                    IsNew = false,
                    Status = existing.Status,
                    ResponseStatusCode = existing.ResponseStatusCode,
                    ResponseBody = existing.ResponseBody
                };
            }

            var request = new IdempotentRequest
            {
                IdempotencyKey = key,
                Status = "Processing",
                CreatedAt = DateTime.UtcNow
            };

            try
            {
                _dbContext.IdempotentRequests.Add(request);
                await _dbContext.SaveChangesAsync();
                return new IdempotencyResult { IsNew = true, Status = "Processing" };
            }
            catch (DbUpdateException ex)
            {
                var isUniqueViolation = ex.InnerException != null && 
                    (ex.InnerException.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) || 
                     ex.InnerException.Message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
                     ex.InnerException.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));

                if (isUniqueViolation)
                {
                    var concurrent = await _dbContext.IdempotentRequests
                        .AsNoTracking()
                        .FirstOrDefaultAsync(r => r.IdempotencyKey == key);

                    if (concurrent != null)
                    {
                        return new IdempotencyResult
                        {
                            IsNew = false,
                            Status = concurrent.Status,
                            ResponseStatusCode = concurrent.ResponseStatusCode,
                            ResponseBody = concurrent.ResponseBody
                        };
                    }
                }
                throw;
            }
        }
        /*
        public async Task<IdempotencyResult?> CheckOrReserveKeyAsync(string key)
        {
            var allRequests = await _dbContext.IdempotentRequests.ToListAsync();
            var existing = allRequests.FirstOrDefault(r => r.IdempotencyKey == key);
            if (existing != null)
            {
                return new IdempotencyResult
                {
                    IsNew = false,
                    Status = existing.Status,
                    ResponseStatusCode = existing.ResponseStatusCode,
                    ResponseBody = existing.ResponseBody
                };
            }
            return null;
        }
        */
        public async Task CompleteRequestAsync(string key, int statusCode, string responseBody)
        {
            var request = await _dbContext.IdempotentRequests.FindAsync(key);
            if (request != null)
            {
                request.Status = "Completed";
                request.ResponseStatusCode = statusCode;
                request.ResponseBody = responseBody;
                await _dbContext.SaveChangesAsync();
            }
        }

        public async Task RemoveRequestAsync(string key)
        {
            var request = await _dbContext.IdempotentRequests.FindAsync(key);
            if (request != null)
            {
                _dbContext.IdempotentRequests.Remove(request);
                await _dbContext.SaveChangesAsync();
            }
        }

        
    }
}
