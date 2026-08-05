using Microsoft.EntityFrameworkCore;
using OrderManagementAPI.Data;
using OrderManagementAPI.Exceptions;
using OrderManagementAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OrderManagementAPI.Services
{
    public class OrderService : IOrderService
    {
        private readonly OrderDbContext _dbContext;

        public OrderService(OrderDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<Order> CreateOrderAsync(CreateOrderDto dto)
        {
            if (dto.Items == null || dto.Items.Count == 0)
            {
                throw new DomainException("Pesanan harus berisi minimal satu item.", 400);
            }

            const int LIMIT_RETRY = 3; 
            int attempt = 0;

            while (true)
            {
                using var transaction = await _dbContext.Database.BeginTransactionAsync();
                try
                {
                    var order = new Order
                    {
                        Id = Guid.NewGuid(),
                        CustomerId = dto.CustomerId,
                        ShippingAddress = dto.ShippingAddress,
                        Status = "Pending",
                        CreatedAt = DateTime.UtcNow,
                        Items = new List<OrderItem>()
                    };

                    foreach (var itemDto in dto.Items)
                    {
                        if (itemDto.Quantity <= 0)
                        {
                            throw new DomainException("Jumlah barang harus lebih besar dari nol.", 400);
                        }

                        var product = await _dbContext.Products.FindAsync(itemDto.ProductId);
                        if (product == null)
                        {
                            throw new DomainException($"Produk {itemDto.ProductId} tidak ditemukan.", 404);
                        }

                        if (product.StockQuantity < itemDto.Quantity)
                        {
                            throw new DomainException($"Stok tidak mencukupi untuk produk '{product.Name}'. Tersedia: {product.StockQuantity}.", 422);
                        }

                        product.StockQuantity -= itemDto.Quantity;

                        order.Items.Add(new OrderItem
                        {
                            Id = Guid.NewGuid(),
                            OrderId = order.Id,
                            ProductId = product.Id,
                            Quantity = itemDto.Quantity,
                            Price = product.Price
                        });
                    }

                    _dbContext.Orders.Add(order);
                    await _dbContext.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return order;
                }
                catch (DbUpdateConcurrencyException)
                {
                    await transaction.RollbackAsync();
                    attempt++;
                    if (attempt >= LIMIT_RETRY)
                    {
                        throw new DomainException("Server sedang sibuk menangani permintaan bersamaan. Silakan coba lagi.", 409);
                    }
                    _dbContext.ChangeTracker.Clear();
                }
                catch (Exception)
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }

        public async Task<Order?> GetOrderByIdAsync(Guid id)
        {
            return await _dbContext.Orders
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);
        }

        public async Task<(IEnumerable<Order> Orders, int TotalCount)> ListOrdersAsync(
            string? status,
            Guid? customerId,
            DateTime? startDate,
            DateTime? endDate,
            int page,
            int pageSize)
        {
            var query = _dbContext.Orders.Include(o => o.Items).AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(o => o.Status == status);
            }

            if (customerId.HasValue)
            {
                query = query.Where(o => o.CustomerId == customerId.Value);
            }

            if (startDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(o => o.CreatedAt <= endDate.Value);
            }

            int totalCount = await query.CountAsync();

            var orders = await query
                .OrderByDescending(o => o.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (orders, totalCount);
        }

        public async Task<Order> UpdateOrderStatusAsync(Guid id, string newStatus)
        {
            const int maxRetries = 3;
            int retryCount = 0;

            while (true)
            {
                using var trx = await _dbContext.Database.BeginTransactionAsync();
                try
                {
                    var order = await _dbContext.Orders
                        .Include(o => o.Items)
                        .FirstOrDefaultAsync(o => o.Id == id);

                    if (order == null)
                    {
                        throw new DomainException("Pesanan tidak ditemukan.", 404);
                    }

                    var currentStatus = order.Status;
                    if (currentStatus == newStatus)
                    {
                        await trx.CommitAsync();
                        return order;
                    }

                    if (currentStatus == "Delivered" || currentStatus == "Cancelled")
                    {
                        throw new DomainException($"Tidak dapat mengubah status dari pesanan yang sudah selesai ({currentStatus}).", 400);
                    }

                    bool isValid = false;
                    if (currentStatus == "Pending")
                    {
                        isValid = newStatus == "Confirmed" || newStatus == "Cancelled";
                    }
                    else if (currentStatus == "Confirmed")
                    {
                        isValid = newStatus == "Shipped" || newStatus == "Cancelled";
                    }
                    else if (currentStatus == "Shipped")
                    {
                        isValid = newStatus == "Delivered";
                    }

                    if (!isValid)
                    {
                        throw new DomainException($"Perpindahan status tidak valid dari {currentStatus} ke {newStatus}.", 400);
                    }

                    if (newStatus == "Cancelled")
                    {
                        foreach (var item in order.Items)
                        {
                            var product = await _dbContext.Products.FindAsync(item.ProductId);
                            if (product != null)
                            {
                                product.StockQuantity += item.Quantity;
                            }
                        }
                    }

                    order.Status = newStatus;
                    await _dbContext.SaveChangesAsync();
                    await trx.CommitAsync();

                    return order;
                }
                catch (DbUpdateConcurrencyException)
                {
                    await trx.RollbackAsync();

                    _dbContext.ChangeTracker.Clear();
                    var dbOrder = await _dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
                    if (dbOrder == null || dbOrder.Status != newStatus)
                    {
                        throw new DomainException("Status pesanan telah diubah oleh permintaan lain.", 409);
                    }

                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        throw new DomainException("Server sedang sibuk menangani pembaruan bersamaan. Silakan coba lagi.", 409);
                    }
                }
                catch (Exception)
                {
                    await trx.RollbackAsync();
                    throw;
                }
            }
        }

        /*
        public async Task<Order?> GetOrderByIdLegacyAsync(Guid id)
        {
            var orders = await _dbContext.Orders.Include(o => o.Items).ToListAsync();
            foreach (var o in orders)
            {
                if (o.Id == id)
                {
                    return o;
                }
            }
            return null;
        }
        */
    }
}
