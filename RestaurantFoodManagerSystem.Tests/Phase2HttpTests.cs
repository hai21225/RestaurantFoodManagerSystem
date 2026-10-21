using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Đợt 2 (2026-10-21) — hợp đồng HTTP mới: cấu hình quán, gỡ khuyến mãi,
/// idempotency cho gọi thêm món.
/// </summary>
public sealed class Phase2HttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public Phase2HttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    // ==================== Cấu hình quán ====================

    [Fact]
    public async Task GetSettings_ReturnsDefaultConfiguration_WhenNeverConfigured()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.RestaurantSettings.RemoveRange(db.RestaurantSettings);
        db.SaveChanges();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/management/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0m, body.GetProperty("taxRate").GetDecimal());
        Assert.Equal(0m, body.GetProperty("serviceChargeRate").GetDecimal());
        Assert.True(body.GetProperty("taxIncluded").GetBoolean());
        Assert.Equal("VND", body.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task PutSettings_SavesConfiguration_AndGetReturnsIt()
    {
        using var client = _factory.CreateClient();

        var put = await client.PutAsJsonAsync("/api/management/settings", new
        {
            restaurantName = "Quán Hải Đồng",
            address = "12 Lê Lợi",
            phone = "0900000000",
            taxCode = "0312345678",
            currency = "VND",
            taxRate = 10m,
            taxIncluded = true,
            serviceChargeRate = 5m
        });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var body = await client.GetFromJsonAsync<JsonElement>("/api/management/settings");
        Assert.Equal("Quán Hải Đồng", body.GetProperty("restaurantName").GetString());
        Assert.Equal("0312345678", body.GetProperty("taxCode").GetString());
        Assert.Equal(10m, body.GetProperty("taxRate").GetDecimal());
        Assert.Equal(5m, body.GetProperty("serviceChargeRate").GetDecimal());
    }

    // ==================== Gọi thêm món + idempotency ====================

    private static Users NewUser(string prefix)
    {
        var userName = $"{prefix}-{Guid.NewGuid():N}";
        return new Users
        {
            UserName = userName,
            UserPassword = "not-used",
            UserFullName = "Phase2 Test",
            UserEmail = $"{userName}@test.local",
            UserPhone = "0000000000",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Tạo đủ bàn/người/món có recipe + tồn kho để <c>POST /orders</c> chạy được.</summary>
    private (int TableId, int SizeId, int UserId, int IngredientId) SeedOrderableScenario()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = NewUser("p2-order");
        var category = new Category
        {
            CategoryName = $"P2 cat {Guid.NewGuid():N}",
            CategoryDescription = "d",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.Categories.Add(category);
        db.SaveChanges();

        var food = new Food
        {
            CategoryId = category.CategoryId,
            FoodName = $"P2 food {Guid.NewGuid():N}",
            FoodDescription = "d",
            FoodImageUrl = "img.png",
            FoodPrice = 10m,
            IsAvailable = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var ingredient = new Ingredient
        {
            Name = $"P2 ing {Guid.NewGuid():N}",
            Unit = "kg",
            MinimumStock = 0m,
            IsActive = true
        };
        db.Foods.Add(food);
        db.Ingredients.Add(ingredient);
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
        db.Inventories.Add(new Inventory
        {
            IngredientId = ingredient.IngredientId,
            Quanlity = 1000m,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        var recipe = new Recipe
        {
            SizeId = size.SizeId,
            Name = $"P2 recipe {Guid.NewGuid():N}",
            Version = "1",
            IsActive = true
        };
        db.Recipes.Add(recipe);
        db.SaveChanges();

        db.RecipeIngredients.Add(new RecipeIngredient
        {
            RecipeId = recipe.RecipeId,
            IngredientId = ingredient.IngredientId,
            Quanlity = 1m
        });

        var table = new RestaurantTable
        {
            TableNumber = $"P{Guid.NewGuid():N}"[..12],
            Capacity = 4,
            Status = RestaurantTableStatuses.Available,
            IsActive = true
        };
        db.RestaurantTables.Add(table);
        db.SaveChanges();

        return (table.TableId, size.SizeId, user.Id, ingredient.IngredientId);
    }

    [Fact]
    public async Task PostOrderItems_WithSameIdempotencyKey_AddsItemsOnlyOnce()
    {
        var (tableId, sizeId, userId, ingredientId) = SeedOrderableScenario();
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/orders", new
        {
            tableId,
            createdBy = userId,
            items = new[] { new { sizeId, quantity = 1, toppings = Array.Empty<object>() } }
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var orderId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();

        client.DefaultRequestHeaders.Add("Idempotency-Key", "p2-add-items");

        var body = new
        {
            changedBy = userId,
            items = new[] { new { sizeId, quantity = 1, toppings = Array.Empty<object>() } }
        };

        var first = await client.PostAsJsonAsync($"/api/orders/{orderId}/items", body);
        var second = await client.PostAsJsonAsync($"/api/orders/{orderId}/items", body);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1 phiếu bếp lúc tạo đơn + 1 phiếu cho lần gọi thêm (bấm 2 lần chỉ thêm 1 lần).
        Assert.Equal(2, db.KitchenOrders.Count(x => x.OrderId == orderId));

        // Tồn kho chỉ trừ cho 2 phần (1 lúc tạo + 1 lần gọi thêm).
        Assert.Equal(998m, db.Inventories.Single(x => x.IngredientId == ingredientId).Quanlity);
    }

    [Fact]
    public async Task DeleteOrderPromotion_ClearsDiscount()
    {
        var (tableId, sizeId, userId, _) = SeedOrderableScenario();
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/orders", new
        {
            tableId,
            createdBy = userId,
            items = new[] { new { sizeId, quantity = 2, toppings = Array.Empty<object>() } }
        });
        var orderId = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("orderId").GetInt32();

        // Khuyến mãi đang hiệu lực (client gửi giờ VN).
        var nowVn = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var promo = await client.PostAsJsonAsync("/api/management/promotions", new
        {
            name = "P2 khuyến mãi",
            description = "",
            discountType = "Percent",
            discountValue = 10m,
            startAt = nowVn.AddDays(-1),
            endAt = nowVn.AddDays(1),
            minimumOrderAmount = 0m,
            maxDiscountAmount = 0m
        });
        Assert.Equal(HttpStatusCode.OK, promo.StatusCode);
        var promoId = (await promo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var apply = await client.PatchAsync($"/api/orders/{orderId}/promotion/{promoId}", null);
        Assert.Equal(HttpStatusCode.NoContent, apply.StatusCode);

        var withPromo = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal(promoId, withPromo.GetProperty("promotionId").GetInt32());
        Assert.True(withPromo.GetProperty("discountAmount").GetDecimal() > 0);

        var remove = await client.DeleteAsync($"/api/orders/{orderId}/promotion/{promoId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var afterRemove = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal(0m, afterRemove.GetProperty("discountAmount").GetDecimal());
        Assert.False(afterRemove.TryGetProperty("promotionId", out var promoField)
                      && promoField.ValueKind != JsonValueKind.Null);
    }
}
