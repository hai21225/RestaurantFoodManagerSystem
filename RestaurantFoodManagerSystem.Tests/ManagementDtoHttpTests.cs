using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// C4: các endpoint <c>/api/management/*</c> phải trả DTO thay vì entity EF thô,
/// nhưng vẫn giữ đúng tên field JSON mà frontend hiện tại đang đọc.
/// </summary>
public sealed class ManagementDtoHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public ManagementDtoHttpTests(RestaurantApiFactory factory)
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

    private static Users NewUser(string userName) => new()
    {
        UserName = userName,
        UserPassword = "not-used",
        UserFullName = "DTO Test",
        UserEmail = $"{userName}@test.local",
        UserPhone = "0000000000",
        IsActive = true,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task GetIngredients_ReturnsDto_WithFrontendFields()
    {
        var name = $"DTO ingredient {Guid.NewGuid():N}";
        Seed(db => db.Ingredients.Add(new Ingredient
        {
            Name = name,
            Unit = "kg",
            MinimumStock = 2.5m,
            IsActive = true
        }));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/management/ingredients");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = rows.EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);

        Assert.True(row.TryGetProperty("ingredientId", out _));
        Assert.Equal("kg", row.GetProperty("unit").GetString());
        Assert.Equal(2.5m, row.GetProperty("minimumStock").GetDecimal());
        Assert.True(row.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task GetInventory_ReturnsQuanlity_AndOmitsInternalRowVersion()
    {
        var name = $"DTO inventory {Guid.NewGuid():N}";
        var ingredientId = 0;

        Seed(db =>
        {
            var ingredient = new Ingredient
            {
                Name = name,
                Unit = "kg",
                MinimumStock = 1m,
                IsActive = true
            };
            db.Ingredients.Add(ingredient);
            db.SaveChanges();
            ingredientId = ingredient.IngredientId;

            db.Inventories.Add(new Inventory
            {
                IngredientId = ingredient.IngredientId,
                Quanlity = 12.5m,
                UpdatedAt = DateTime.UtcNow
            });
        });

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/management/inventory");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = rows.EnumerateArray().Single(x => x.GetProperty("ingredientId").GetInt32() == ingredientId);

        Assert.Equal(12.5m, row.GetProperty("quanlity").GetDecimal());
        Assert.False(row.TryGetProperty("rowVersion", out _));
        Assert.False(row.TryGetProperty("RowVersion", out _));
    }

    [Fact]
    public async Task GetPurchaseOrders_ReturnsDto()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var supplierId = 0;
        var orderId = 0;
        var orderedAt = new DateTime(2026, 10, 19, 8, 30, 0, DateTimeKind.Utc);
        var receivedAt = orderedAt.AddHours(1);

        Seed(db =>
        {
            var user = NewUser($"dto-po-{suffix}");
            db.Users.Add(user);

            var supplier = new Supplier
            {
                Name = $"DTO supplier {suffix}",
                Phone = "0900000000",
                Email = "supplier@test.local",
                Address = "Test",
                IsActive = true
            };
            db.Suppliers.Add(supplier);
            db.SaveChanges();
            supplierId = supplier.SupplierId;

            var order = new PurchaseOrder
            {
                SupplierId = supplier.SupplierId,
                Status = "Received",
                TotalAmount = 125.5m,
                OrderedAt = orderedAt,
                ReceivedAt = receivedAt,
                CreatedBy = user.Id
            };
            db.PurchaseOrders.Add(order);
            db.SaveChanges();
            orderId = order.Id;
        });

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/management/purchase-orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = rows.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == orderId);

        Assert.Equal(supplierId, row.GetProperty("supplierId").GetInt32());
        Assert.Equal("Received", row.GetProperty("status").GetString());
        Assert.Equal(125.5m, row.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(orderedAt, row.GetProperty("orderedAt").GetDateTime());
        Assert.Equal(receivedAt, row.GetProperty("receivedAt").GetDateTime());
    }

    [Fact]
    public async Task PurchaseOrderDetail_ReturnsItems_AndCancelBlocksReceive()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var orderId = 0;
        var ingredientId = 0;
        var userId = 0;

        Seed(db =>
        {
            var user = NewUser($"dto-po-detail-{suffix}");
            db.Users.Add(user);
            db.SaveChanges();
            userId = user.Id;

            var supplier = new Supplier
            {
                Name = $"Detail supplier {suffix}",
                Phone = "0900000000",
                Email = "supplier@test.local",
                Address = "Test",
                IsActive = true
            };
            db.Suppliers.Add(supplier);

            var ingredient = new Ingredient
            {
                Name = $"Detail ingredient {suffix}",
                Unit = "kg",
                MinimumStock = 0m,
                IsActive = true
            };
            db.Ingredients.Add(ingredient);
            db.SaveChanges();
            ingredientId = ingredient.IngredientId;

            db.Inventories.Add(new Inventory
            {
                IngredientId = ingredient.IngredientId,
                Quanlity = 0m,
                UpdatedAt = DateTime.UtcNow
            });

            var order = new PurchaseOrder
            {
                SupplierId = supplier.SupplierId,
                Status = "Draft",
                TotalAmount = 10m,
                OrderedAt = DateTime.UtcNow,
                CreatedBy = user.Id
            };
            db.PurchaseOrders.Add(order);
            db.SaveChanges();
            orderId = order.Id;

            db.PurchaseOrderItems.Add(new PurchaseOrderItem
            {
                PurchaseOrderId = order.Id,
                IngredientId = ingredient.IngredientId,
                Quanlity = 4m,
                UnitPrice = 2.5m,
                TotalPrice = 10m
            });
        });

        using var client = _factory.CreateClient();

        var detail = await client.GetAsync($"/api/management/purchase-orders/{orderId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal($"Detail supplier {suffix}", body.GetProperty("supplierName").GetString());

        var item = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(ingredientId, item.GetProperty("ingredientId").GetInt32());
        Assert.Equal($"Detail ingredient {suffix}", item.GetProperty("ingredientName").GetString());
        Assert.Equal(4m, item.GetProperty("quantity").GetDecimal());
        Assert.Equal(2.5m, item.GetProperty("unitPrice").GetDecimal());

        var cancel = await client.PostAsync($"/api/management/purchase-orders/{orderId}/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        var cancelled = await client.GetFromJsonAsync<JsonElement>($"/api/management/purchase-orders/{orderId}");
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());

        var receive = await client.PostAsJsonAsync(
            $"/api/management/purchase-orders/{orderId}/receive",
            new { receivedBy = userId });
        Assert.Equal(HttpStatusCode.Conflict, receive.StatusCode);
    }

    [Fact]
    public async Task GetPromotions_ReturnsDto()
    {
        var name = $"DTO promotion {Guid.NewGuid():N}";

        Seed(db => db.Promotions.Add(new Promotion
        {
            Name = name,
            Description = "Test",
            DiscountType = "Percent",
            DiscountValue = 10m,
            StartAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            EndAt = new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc),
            MinimumOrderAmount = 50m,
            MaxDiscountAmount = 20m,
            IsActive = true
        }));

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/management/promotions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = rows.EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);

        Assert.Equal("Percent", row.GetProperty("discountType").GetString());
        Assert.Equal(10m, row.GetProperty("discountValue").GetDecimal());
        Assert.True(row.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task GetKitchenOrders_ReturnsDto()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var kitchenOrderId = 0;
        var sentAt = new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);

        Seed(db =>
        {
            var user = NewUser($"dto-kitchen-{suffix}");
            db.Users.Add(user);

            var table = new RestaurantTable
            {
                TableNumber = $"DTO-{suffix[..6]}",
                Capacity = 4,
                Status = "Available",
                IsActive = true
            };
            db.RestaurantTables.Add(table);
            db.SaveChanges();

            var order = new Order
            {
                TableId = table.TableId,
                Status = "SentToKitchen",
                CreatedBy = user.Id,
                CreatedAt = sentAt,
                UpdatedAt = sentAt
            };
            db.Orders.Add(order);
            db.SaveChanges();

            var kitchenOrder = new KitchenOrder
            {
                OrderId = order.OrderId,
                Status = "Pending",
                SentAt = sentAt,
                Priority = 2
            };
            db.KitchenOrders.Add(kitchenOrder);
            db.SaveChanges();
            kitchenOrderId = kitchenOrder.KitchenOrderId;
        });

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/management/kitchen/orders");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = rows.EnumerateArray().Single(x => x.GetProperty("kitchenOrderId").GetInt32() == kitchenOrderId);

        Assert.Equal("Pending", row.GetProperty("status").GetString());
        Assert.Equal(2, row.GetProperty("priority").GetInt32());
        Assert.Equal(sentAt, row.GetProperty("sentAt").GetDateTime());
    }

    [Fact]
    public async Task PostRecipe_ReturnsSummaryDto_AndAcceptsQuantityJsonProperty()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var sizeId = 0;
        var ingredientId = 0;

        Seed(db =>
        {
            var category = new Category
            {
                CategoryName = $"DTO category {suffix}",
                CategoryDescription = "Test",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Categories.Add(category);
            db.SaveChanges();

            var food = new Food
            {
                CategoryId = category.CategoryId,
                FoodName = $"DTO food {suffix}",
                FoodDescription = "Test",
                FoodImageUrl = "",
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
                SizeName = "L",
                SizePrice = 10m,
                IsActive = true,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.FoodSizes.Add(size);

            var ingredient = new Ingredient
            {
                Name = $"DTO recipe ingredient {suffix}",
                Unit = "kg",
                MinimumStock = 0m,
                IsActive = true
            };
            db.Ingredients.Add(ingredient);
            db.SaveChanges();

            sizeId = size.SizeId;
            ingredientId = ingredient.IngredientId;
        });

        using var client = _factory.CreateClient();
        // Cố ý gửi "quantity" (hợp đồng frontend hiện tại) dù C# property là Quanlity.
        var response = await client.PostAsJsonAsync("/api/management/recipes", new
        {
            sizeId,
            name = $"DTO recipe {suffix}",
            version = "1",
            ingredients = new[] { new { ingredientId, quantity = 2.5m } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("recipeId", out var recipeId));
        Assert.True(recipeId.GetInt32() > 0);
        Assert.Equal(sizeId, body.GetProperty("sizeId").GetInt32());
        Assert.Equal(1, body.GetProperty("ingredientCount").GetInt32());
        Assert.False(body.TryGetProperty("ingredients", out _));
    }
}
