using System;
using System.ComponentModel.DataAnnotations;

namespace OrderManagementAPI.Models
{
    public class IdempotentRequest
    {
        [Key]
        public string IdempotencyKey { get; set; } = string.Empty;
        public string Status { get; set; } = "Processing";
        public int ResponseStatusCode { get; set; }
        public string ResponseBody { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
