using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Batch A2 — kiểm tra mapping lỗi ở mức HTTP: dữ liệu sai phải trả 400/409 với message
/// nghiệp vụ, không lộ message SQL và không biến thành 500.
/// </summary>
public sealed class BatchA2HttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public BatchA2HttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    private void Seed(Action<AppDbContext> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        db.SaveChanges();
    }

    private static Users NewUser(string suffix) => new()
    {
        UserName = $"batch-a2-{suffix}",
        UserPassword = "not-used",
        UserFullName = "Batch A2",
        UserEmail = $"batch-a2-{suffix}@test.local",
        UserPhone = "0000000000",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task UpdateTable_ToDuplicateNumber_ReturnsConflict_WithoutSqlMessage()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var firstId = 0;
        var secondId = 0;

        Seed(db =>
        {
            var first = new RestaurantTable
            {
                TableNumber = $"A-{suffix}",
                Capacity = 4,
                Status = "Available",
                IsActive = true
            };
            var second = new RestaurantTable
            {
                TableNumber = $"B-{suffix}",
                Capacity = 4,
                Status = "Available",
                IsActive = true
            };
            db.RestaurantTables.AddRange(first, second);
            db.SaveChanges();
            firstId = first.TableId;
            secondId = second.TableId;
        });

        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/restaurant-tables/{secondId}", new
        {
            tableNumber = $"  A-{suffix}  ",
            capacity = 4,
            isActive = true
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var message = body.GetProperty("message").GetString();
        Assert.Equal("Table number already exists", message);
        Assert.DoesNotContain("UNIQUE", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SQL", message, StringComparison.OrdinalIgnoreCase);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal($"B-{suffix}", db.RestaurantTables.Single(x => x.TableId == secondId).TableNumber);
        Assert.Equal($"A-{suffix}", db.RestaurantTables.Single(x => x.TableId == firstId).TableNumber);
    }

    [Fact]
    public async Task DeleteOccupiedTable_ReturnsConflict()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var tableId = 0;

        Seed(db =>
        {
            var user = NewUser(suffix);
            db.Users.Add(user);
            var table = new RestaurantTable
            {
                TableNumber = $"BUSY-{suffix}",
                Capacity = 4,
                Status = "Occupied",
                IsActive = true
            };
            db.RestaurantTables.Add(table);
            db.SaveChanges();
            tableId = table.TableId;

            db.Orders.Add(new Order
            {
                TableId = table.TableId,
                CreatedBy = user.Id,
                Status = "SentToKitchen",
                Subtotal = 50m,
                TotalAmount = 50m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        });

        using var client = _factory.CreateClient();
        var response = await client.DeleteAsync($"/api/restaurant-tables/{tableId}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(db.RestaurantTables.Single(x => x.TableId == tableId).IsActive);
    }

    [Fact]
    public async Task DeleteTable_Unknown_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();
        var response = await client.DeleteAsync($"/api/restaurant-tables/{int.MaxValue}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTable_WithoutNumber_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/restaurant-tables", new
        {
            tableNumber = "   ",
            capacity = 4
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdatePromotion_WithInvalidRange_ReturnsBadRequest()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var promotionId = 0;

        Seed(db =>
        {
            var promotion = new Promotion
            {
                Name = $"promo-{suffix}",
                Description = "d",
                DiscountType = "Percent",
                DiscountValue = 10m,
                StartAt = DateTime.UtcNow.AddDays(-1),
                EndAt = DateTime.UtcNow.AddDays(1),
                IsActive = true
            };
            db.Promotions.Add(promotion);
            db.SaveChanges();
            promotionId = promotion.Id;
        });

        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/management/promotions/{promotionId}", new
        {
            name = $"promo-{suffix}",
            description = "d",
            discountType = "Percent",
            discountValue = 10m,
            startAt = DateTime.UtcNow,
            endAt = DateTime.UtcNow.AddDays(-1),
            minimumOrderAmount = 0m,
            maxDiscountAmount = 0m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateRecipe_WithNullIngredients_ReturnsBadRequest()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var sizeId = 0;

        Seed(db =>
        {
            var category = new Category
            {
                CategoryName = $"cat-{suffix}",
                CategoryDescription = "d",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Categories.Add(category);
            db.SaveChanges();

            var food = new Food
            {
                CategoryId = category.CategoryId,
                FoodName = $"food-{suffix}",
                FoodDescription = "d",
                FoodImageUrl = "img.png",
                FoodPrice = 10m,
                IsAvailable = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Foods.Add(food);
            db.SaveChanges();

            var size = new FoodSize
            {
                FoodId = food.FoodId,
                SizeName = "M",
                SizePrice = 10m,
                IsDefault = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.FoodSizes.Add(size);
            db.SaveChanges();
            sizeId = size.SizeId;
        });

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/management/recipes", new
        {
            sizeId,
            name = $"recipe-{suffix}",
            version = "1",
            ingredients = (object?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreatePurchase_WithNullOrEmptyItems_ReturnsBadRequest()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var supplierId = 0;
        var userId = 0;

        Seed(db =>
        {
            var user = NewUser(suffix);
            db.Users.Add(user);

            var supplier = new Supplier
            {
                Name = $"supplier-{suffix}",
                Phone = "1",
                Email = $"supplier-{suffix}@test.local",
                Address = "A",
                IsActive = true
            };
            db.Suppliers.Add(supplier);
            db.SaveChanges();
            supplierId = supplier.SupplierId;
            userId = user.Id;
        });

        using var client = _factory.CreateClient();

        var nullItems = await client.PostAsJsonAsync("/api/management/purchase-orders", new
        {
            supplierId,
            createdBy = userId,
            items = (object?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, nullItems.StatusCode);

        var emptyItems = await client.PostAsJsonAsync("/api/management/purchase-orders", new
        {
            supplierId,
            createdBy = userId,
            items = Array.Empty<object>()
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyItems.StatusCode);
    }
}
