using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderManagementAPI.Data;
using OrderManagementAPI.Models;
using OrderManagementAPI.Services;
using OrderManagementAPI.Controllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace OrderManagementAPI.Tests
{
    public class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<OrderDbContext>));

                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }

                services.AddDbContext<OrderDbContext>(options =>
                {
                    options.UseSqlServer("Server=localhost;Database=OrderManagementTestDB;Trusted_Connection=True;TrustServerCertificate=True;");
                });
            });
        }
    }

    public class ConcurrencyTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;
        private readonly Guid _productAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private readonly Guid _productBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        public ConcurrencyTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
            SetupDatabase();
        }

        private void SetupDatabase()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            db.Products.AddRange(
                new Product { Id = _productAId, Name = "Product A", StockQuantity = 100, Price = 10000 },
                new Product { Id = _productBId, Name = "Product B", StockQuantity = 15, Price = 20000 }
            );
            db.SaveChanges();
        }


        [Fact]
        public async Task ScenarioA_ConcurrentStockDeduction_ShouldEnsureCorrectStock()
        {
            var client = _factory.CreateClient();
            var tasks = new List<Task<HttpResponseMessage>>();

            for (int i = 0; i < 10; i++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/order")
                {
                    Content = JsonContent.Create(new CreateOrderDto
                    {
                        CustomerId = Guid.NewGuid(),
                        ShippingAddress = "Test Address",
                        Items = new List<OrderItemDto>
                        {
                            new() { ProductId = _productBId, Quantity = 10 }
                        }
                    })
                };
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
                tasks.Add(client.SendAsync(request));
            }

            var responses = await Task.WhenAll(tasks);

            int successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
            int failureCount = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity || r.StatusCode == HttpStatusCode.Conflict);

            Assert.Equal(1, successCount);
            Assert.Equal(9, failureCount);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var product = await db.Products.FindAsync(_productBId);
            Assert.NotNull(product);
            Assert.Equal(5, product.StockQuantity);
        }

        [Fact]
        public async Task ScenarioB_ConcurrentStatusUpdate_ShouldAllowOnlyOneWinner()
        {
            var client = _factory.CreateClient();

            var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/order")
            {
                Content = JsonContent.Create(new CreateOrderDto
                {
                    CustomerId = Guid.NewGuid(),
                    ShippingAddress = "Test Address",
                    Items = new List<OrderItemDto>
                    {
                        new() { ProductId = _productAId, Quantity = 2 }
                    }
                })
            };
            createRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            var createResponse = await client.SendAsync(createRequest);
            if (createResponse.StatusCode != HttpStatusCode.Created)
            {
                var body = await createResponse.Content.ReadAsStringAsync();
                throw new Exception($"Failed to create order. Status: {createResponse.StatusCode}, Body: {body}");
            }
            var order = await createResponse.Content.ReadFromJsonAsync<Order>();
            Assert.NotNull(order);

            var confirmRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/order/{order.Id}/status")
            {
                Content = JsonContent.Create(new UpdateStatusDto { Status = "Confirmed" })
            };
            var confirmResponse = await client.SendAsync(confirmRequest);
            Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

            var updateShippedTask = client.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/api/order/{order.Id}/status")
            {
                Content = JsonContent.Create(new UpdateStatusDto { Status = "Shipped" })
            });

            var updateCancelledTask = client.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/api/order/{order.Id}/status")
            {
                Content = JsonContent.Create(new UpdateStatusDto { Status = "Cancelled" })
            });

            var responses = await Task.WhenAll(updateShippedTask, updateCancelledTask);

            int successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
            int failureCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict || r.StatusCode == HttpStatusCode.BadRequest);

            Assert.Equal(1, successCount);
            Assert.Equal(1, failureCount);
        }

        [Fact]
        public async Task ScenarioC_IdempotentCreateUnderRace_ShouldCreateExactlyOneOrder()
        {
            var client = _factory.CreateClient();
            var idempotencyKey = Guid.NewGuid().ToString();
            var customerId = Guid.NewGuid();
            var tasks = new List<Task<HttpResponseMessage>>();

            for (int i = 0; i < 5; i++)
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "/api/order")
                {
                    Content = JsonContent.Create(new CreateOrderDto
                    {
                        CustomerId = customerId,
                        ShippingAddress = "Test Address",
                        Items = new List<OrderItemDto>
                        {
                            new() { ProductId = _productAId, Quantity = 1 }
                        }
                    })
                };
                request.Headers.Add("Idempotency-Key", idempotencyKey);
                tasks.Add(client.SendAsync(request));
            }

            var responses = await Task.WhenAll(tasks);

            int successCount = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
            int conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

            Assert.True(successCount >= 1);
            Assert.Equal(5, successCount + conflictCount);

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            var orders = await db.Orders.Where(o => o.CustomerId == customerId).ToListAsync();
            Assert.Single(orders);
        }
    }
}
