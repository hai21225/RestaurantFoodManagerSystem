using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// P2: <c>POST /api/orders</c>, <c>POST /api/payments</c> và <c>GET /api/payments/order/{id}</c>
/// trả DTO thay vì entity EF thô, nhưng vẫn giữ đủ field JSON mà frontend đang đọc.
/// </summary>
public sealed class ResponseDtoHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public ResponseDtoHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    private static Users NewUser(string prefix)
    {
        var userName = $"{prefix}-{Guid.NewGuid():N}";
        return new Users
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
    }

    private static RestaurantTable NewTable()
    {
        return new RestaurantTable
        {
            TableNumber = $"D{Guid.NewGuid():N}"[..12],
            Capacity = 4,
            Status = RestaurantTableStatuses.Available,
            IsActive = true
        };
    }

    /// <summary>Tạo đủ bàn/người/món có recipe + tồn kho để <c>POST /orders</c> chạy được.</summary>
    private (int TableId, int SizeId, int UserId, int ToppingId) SeedOrderableScenario()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = NewUser("dto-order");
        var category = new Category
        {
            CategoryName = $"DTO cat {Guid.NewGuid():N}",
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
            FoodName = $"DTO food {Guid.NewGuid():N}",
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
            Name = $"DTO ing {Guid.NewGuid():N}",
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
        db.Inventories.Add(new Inventory { IngredientId = ingredient.IngredientId, Quanlity = 1000m, UpdatedAt = DateTime.UtcNow });
        db.SaveChanges();

        var recipe = new Recipe { SizeId = size.SizeId, Name = $"DTO recipe {Guid.NewGuid():N}", Version = "1", IsActive = true };
        db.Recipes.Add(recipe);
        db.SaveChanges();

        db.RecipeIngredients.Add(new RecipeIngredient { RecipeId = recipe.RecipeId, IngredientId = ingredient.IngredientId, Quanlity = 1m });

        var topping = new Topping
        {
            ToppingName = $"DTO topping {Guid.NewGuid():N}",
            ToppingPrice = 5m,
            IsActive = true,
            IsAvailable = true
        };
        db.Toppings.Add(topping);
        db.SaveChanges();
        db.FoodToppings.Add(new FoodTopping { FoodId = food.FoodId, ToppingId = topping.ToppingId });

        var table = NewTable();
        db.RestaurantTables.Add(table);
        db.SaveChanges();

        return (table.TableId, size.SizeId, user.Id, topping.ToppingId);
    }

    /// <summary>Tạo bàn/người/đơn (không cần món) để thử luồng thanh toán.</summary>
    private (int OrderId, int UserId) SeedPayableOrder(decimal total)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = NewUser("dto-pay");
        var table = NewTable();
        db.Users.Add(user);
        db.RestaurantTables.Add(table);
        db.SaveChanges();

        var order = new Order
        {
            TableId = table.TableId,
            CreatedBy = user.Id,
            Status = OrderStatuses.Pending,
            Subtotal = total,
            TotalAmount = total,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);
        db.SaveChanges();

        return (order.OrderId, user.Id);
    }

    [Fact]
    public async Task PostOrder_ReturnsDto_WithoutRowVersion()
    {
        var (tableId, sizeId, userId, _) = SeedOrderableScenario();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/orders", new
        {
            tableId,
            createdBy = userId,
            items = new[] { new { sizeId, quantity = 2, toppings = Array.Empty<object>() } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("orderId").GetInt32() > 0);
        Assert.Equal(tableId, json.GetProperty("tableId").GetInt32());
        Assert.Equal(OrderStatuses.SentToKitchen, json.GetProperty("status").GetString());
        Assert.Equal(20m, json.GetProperty("totalAmount").GetDecimal());
        Assert.True(json.TryGetProperty("createdAt", out _));
        Assert.False(json.TryGetProperty("rowVersion", out _));
    }

    [Fact]
    public async Task PostOrder_WithIdempotencyKey_ReplaysOriginalOrder_AndDeductsStockOnce()
    {
        var (tableId, sizeId, userId, _) = SeedOrderableScenario();
        using var client = _factory.CreateClient();
        var key = Guid.NewGuid().ToString("N");
        client.DefaultRequestHeaders.Add("Idempotency-Key", key);

        var payload = new
        {
            tableId,
            createdBy = userId,
            items = new[] { new { sizeId, quantity = 1, toppings = Array.Empty<object>() } }
        };

        var first = await client.PostAsJsonAsync("/api/orders", payload);
        var second = await client.PostAsJsonAsync("/api/orders", payload);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var firstJson = await first.Content.ReadFromJsonAsync<JsonElement>();
        var secondJson = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstJson.GetProperty("orderId").GetInt32(), secondJson.GetProperty("orderId").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recipeId = db.Recipes.Single(x => x.SizeId == sizeId).RecipeId;
        var ingredientId = db.RecipeIngredients.Single(x => x.RecipeId == recipeId).IngredientId;
        Assert.Equal(1, db.Orders.Count(x => x.TableId == tableId));
        Assert.Equal(999m, db.Inventories.Single(x => x.IngredientId == ingredientId).Quanlity);
    }

    [Fact]
    public async Task PostOrder_WithIdempotencyKey_AndDifferentPayload_ReturnsConflict()
    {
        var (tableId, sizeId, userId, _) = SeedOrderableScenario();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var first = await client.PostAsJsonAsync("/api/orders", new
        {
            tableId,
            createdBy = userId,
            items = new[] { new { sizeId, quantity = 1, toppings = Array.Empty<object>() } }
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var conflict = await client.PostAsJsonAsync("/api/orders", new
        {
            tableId,
            createdBy = userId,
            items = new[] { new { sizeId, quantity = 2, toppings = Array.Empty<object>() } }
        });

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("different order payload", await conflict.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PutOrderItems_ReplacesItems_ReturnsUpdatedDetail()
    {
        var (tableId, sizeId, userId, toppingId) = SeedOrderableScenario();
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/orders", new
        {
            tableId,
            createdBy = userId,
            items = new[] { new { sizeId, quantity = 2, toppings = Array.Empty<object>() } }
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = created.GetProperty("orderId").GetInt32();

        var update = await client.PutAsJsonAsync($"/api/orders/{orderId}/items", new
        {
            changedBy = userId,
            items = new[]
            {
                new
                {
                    sizeId,
                    quantity = 3,
                    note = "more",
                    toppings = new[] { new { toppingId, quantity = 1 } }
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var detail = await update.Content.ReadFromJsonAsync<JsonElement>();
        var item = Assert.Single(detail.GetProperty("items").EnumerateArray());
        Assert.Equal(3, item.GetProperty("quantity").GetInt32());
        Assert.Equal("more", item.GetProperty("note").GetString());
        Assert.Equal(45m, detail.GetProperty("totalAmount").GetDecimal());
        Assert.Single(item.GetProperty("toppings").EnumerateArray());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recipeId = db.Recipes.Single(x => x.SizeId == sizeId).RecipeId;
        var ingredientId = db.RecipeIngredients.Single(x => x.RecipeId == recipeId).IngredientId;
        Assert.Equal(997m, db.Inventories.Single(x => x.IngredientId == ingredientId).Quanlity);
    }

    [Fact]
    public async Task GetOrder_ReturnsItemToppings_WithQuantityJsonProperty()
    {
        var (tableId, sizeId, userId, toppingId) = SeedOrderableScenario();
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/orders", new
        {
            tableId,
            createdBy = userId,
            items = new[]
            {
                new
                {
                    sizeId,
                    quantity = 2,
                    toppings = new[] { new { toppingId, quantity = 3 } }
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var orderId = created.GetProperty("orderId").GetInt32();

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        var item = Assert.Single(detail.GetProperty("items").EnumerateArray());
        var topping = Assert.Single(item.GetProperty("toppings").EnumerateArray());

        Assert.Equal(toppingId, topping.GetProperty("toppingId").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(topping.GetProperty("toppingName").GetString()));
        Assert.Equal(3, topping.GetProperty("quantity").GetInt32());
        Assert.Equal(5m, topping.GetProperty("unitPrice").GetDecimal());
        Assert.False(topping.TryGetProperty("quanlity", out _));
    }

    [Fact]
    public async Task PostPayment_And_GetByOrder_ReturnDto()
    {
        var (orderId, userId) = SeedPayableOrder(150m);
        using var client = _factory.CreateClient();

        var post = await client.PostAsJsonAsync("/api/payments", new
        {
            orderId,
            paymentMethod = "Cash",
            transactionCode = "TXN-DTO",
            createdBy = userId
        });

        Assert.Equal(HttpStatusCode.OK, post.StatusCode);
        var created = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(created.GetProperty("paymentId").GetInt32() > 0);
        Assert.Equal(orderId, created.GetProperty("orderId").GetInt32());
        Assert.Equal(150m, created.GetProperty("amount").GetDecimal());
        Assert.Equal("Cash", created.GetProperty("paymentMethod").GetString());
        Assert.Equal("Paid", created.GetProperty("status").GetString());
        Assert.Equal("TXN-DTO", created.GetProperty("transactionCode").GetString());

        var get = await client.GetAsync($"/api/payments/order/{orderId}");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var rows = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        var row = rows.EnumerateArray().Single(x => x.GetProperty("orderId").GetInt32() == orderId);
        Assert.True(row.GetProperty("paymentId").GetInt32() > 0);
        Assert.Equal("Cash", row.GetProperty("paymentMethod").GetString());
    }

    [Fact]
    public async Task PostPayment_WithUnknownMethod_ReturnsBadRequest()
    {
        // Defect #26: controller phải map ArgumentException thành 400 thay vì 500.
        var (orderId, userId) = SeedPayableOrder(150m);
        using var client = _factory.CreateClient();

        var post = await client.PostAsJsonAsync("/api/payments", new
        {
            orderId,
            paymentMethod = "Bitcoin",
            createdBy = userId
        });

        Assert.Equal(HttpStatusCode.BadRequest, post.StatusCode);
        var body = await post.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Invalid payment method", body.GetProperty("message").GetString());
    }
}
