using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderManagementAPI.Data;
using OrderManagementAPI.Filters;
using OrderManagementAPI.Middleware;
using OrderManagementAPI.Models;
using OrderManagementAPI.Services;
using OrderManagementAPI.Logging;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

string logMode = builder.Configuration["LogMode"] ?? "dev";
switch (logMode)
{
    case "dev":
        builder.Logging.AddProvider(new FileLoggerProvider(Path.Combine(builder.Environment.ContentRootPath, "logs", "app.log")));
        break;
    case "prod":
        builder.Logging.AddConsole();
        break;
}

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<OrderDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddScoped<IdempotencyFilter>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    dbContext.Database.EnsureCreated();

    if (!dbContext.Products.Any())
    {
        dbContext.Products.AddRange(
            new Product { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "Product A", StockQuantity = 100, Price = 10000 },
            new Product { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), Name = "Product B", StockQuantity = 15, Price = 25000 },
            new Product { Id = Guid.Parse("33333333-3333-3333-3333-333333333333"), Name = "Product C", StockQuantity = 5, Price = 50000 }
        );
        dbContext.SaveChanges();
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
