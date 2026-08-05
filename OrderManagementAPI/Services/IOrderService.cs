using OrderManagementAPI.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OrderManagementAPI.Services
{
    public class CreateOrderDto
    {
        public Guid CustomerId { get; set; }
        public string ShippingAddress { get; set; } = string.Empty;
        public List<OrderItemDto> Items { get; set; } = new();
    }

    public class OrderItemDto
    {
        public Guid ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public interface IOrderService
    {
        Task<Order> CreateOrderAsync(CreateOrderDto dto);
        Task<Order?> GetOrderByIdAsync(Guid id);
        Task<(IEnumerable<Order> Orders, int TotalCount)> ListOrdersAsync(string? status, Guid? customerId, DateTime? startDate, DateTime? endDate, int page, int pageSize);
        Task<Order> UpdateOrderStatusAsync(Guid id, string newStatus);
    }
}
