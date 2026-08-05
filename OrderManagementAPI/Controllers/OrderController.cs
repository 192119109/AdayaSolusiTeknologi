using Microsoft.AspNetCore.Mvc;
using OrderManagementAPI.Filters;
using OrderManagementAPI.Services;
using System;
using System.Threading.Tasks;

namespace OrderManagementAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrderController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public OrderController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        [HttpPost]
        [ServiceFilter(typeof(IdempotencyFilter))]
        public async Task<IActionResult> Create([FromBody] CreateOrderDto dto)
        {
            var order = await _orderService.CreateOrderAsync(dto);
            return StatusCode(201, order);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var order = await _orderService.GetOrderByIdAsync(id);
            if (order == null)
            {
                return NotFound(new { success = false, message = "Pesanan tidak ditemukan." });
            }
            return Ok(order);
        }

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? status,
            [FromQuery] Guid? customerId,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            var (orders, totalCount) = await _orderService.ListOrdersAsync(status, customerId, startDate, endDate, page, pageSize);

            return Ok(new
            {
                success = true,
                data = orders,
                totalCount = totalCount,
                page = page,
                pageSize = pageSize
            });
        }

        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateStatusDto dto)
        {
            if (string.IsNullOrEmpty(dto.Status))
            {
                return BadRequest(new { success = false, message = "Status wajib diisi." });
            }

            var order = await _orderService.UpdateOrderStatusAsync(id, dto.Status);
            return Ok(order);
        }
    }

    public class UpdateStatusDto
    {
        public string Status { get; set; } = string.Empty;
    }
}
