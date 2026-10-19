using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// P1 (phần 2): lịch sử trạng thái đơn (<c>GET /api/orders/{id}/history</c>) và
/// biến động kho (<c>GET /api/management/inventory-transactions</c>).
/// </summary>
public sealed class HistoryAndTransactionsServiceTests : TestBase
{
    [Fact]
    public async Task Orders_History_ReturnsChronologicalRows_WithActorName()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: OrderStatuses.Pending);

        await Orders.UpdateStatusAsync(order.OrderId, new UpdateOrderStatusRequest { Status = "Preparing", ChangedBy = s.Staff.Id }, CancellationToken.None);
        await Orders.UpdateStatusAsync(order.OrderId, new UpdateOrderStatusRequest { Status = "Ready", ChangedBy = s.Staff.Id }, CancellationToken.None);

        var history = await Orders.GetHistoryAsync(order.OrderId);

        Assert.NotNull(history);
        Assert.Equal(2, history!.Count);
        Assert.Equal("Pending", history[0].OldStatus);
        Assert.Equal("Preparing", history[0].NewStatus);
        Assert.Equal("Preparing", history[1].OldStatus);
        Assert.Equal("Ready", history[1].NewStatus);
        Assert.Equal("Test User", history[0].ChangedByName);
        Assert.True(history[0].Id < history[1].Id);
    }

    [Fact]
    public async Task Orders_History_UnknownOrder_ReturnsNull()
    {
        Assert.Null(await Orders.GetHistoryAsync(999_999));
    }

    [Fact]
    public async Task Inventory_Transactions_FilterByIngredientAndType_NewestFirst()
    {
        var s = SeedOrderScenario(stock: 100m);
        var ingredientId = s.Ingredient.IngredientId;

        await InventorySvc.AdjustAsync(ingredientId, new InventoryAdjustmentRequest { Quanlity = 10m, CreatedBy = s.Staff.Id, Note = "+10" }, CancellationToken.None);
        await InventorySvc.AdjustAsync(ingredientId, new InventoryAdjustmentRequest { Quanlity = -4m, CreatedBy = s.Staff.Id }, CancellationToken.None);

        var all = await InventorySvc.GetTransactionsAsync(new InventoryTransactionQuery { IngredientId = ingredientId });

        Assert.Equal(2, all.Total);
        Assert.Equal("AdjustmentOut", all.Items[0].Type);
        Assert.Equal(s.Ingredient.Name, all.Items[0].IngredientName);

        var inputs = await InventorySvc.GetTransactionsAsync(new InventoryTransactionQuery { IngredientId = ingredientId, Type = "adjustmentin" });

        var row = Assert.Single(inputs.Items);
        Assert.Equal(10m, row.Quanlity);
        Assert.Equal(100m, row.QuanlityBefore);
        Assert.Equal(110m, row.QuanlityAfter);
        Assert.Equal("+10", row.Note);
        Assert.Equal(s.Staff.Id, row.CreatedBy);
    }

    [Fact]
    public async Task Inventory_Transactions_RejectsInvalidPaging()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            InventorySvc.GetTransactionsAsync(new InventoryTransactionQuery { PageSize = 1000 }));
    }
}

public sealed class HistoryAndTransactionsHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public HistoryAndTransactionsHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    private int SeedOrder()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userName = $"hist-{Guid.NewGuid():N}";
        var user = new Users
        {
            UserName = userName,
            UserPassword = "not-used",
            UserFullName = "History Test",
            UserEmail = $"{userName}@test.local",
            UserPhone = "0000000000",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);

        var table = new RestaurantTable
        {
            TableNumber = $"H{Guid.NewGuid():N}"[..12],
            Capacity = 2,
            Status = RestaurantTableStatuses.Available,
            IsActive = true
        };
        db.RestaurantTables.Add(table);
        db.SaveChanges();

        var order = new Order
        {
            TableId = table.TableId,
            CreatedBy = user.Id,
            Status = OrderStatuses.Pending,
            Subtotal = 10m,
            TotalAmount = 10m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);
        db.SaveChanges();

        return order.OrderId;
    }

    [Fact]
    public async Task GetOrderHistory_UnknownOrder_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/orders/999999/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetOrderHistory_ExistingOrder_ReturnsArray()
    {
        var orderId = SeedOrder();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/orders/{orderId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
    }

    [Fact]
    public async Task GetInventoryTransactions_ReturnsPagedEnvelope()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/management/inventory-transactions?pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("page").GetInt32());
        Assert.Equal(5, payload.GetProperty("pageSize").GetInt32());
        Assert.True(payload.TryGetProperty("total", out _));
        Assert.Equal(JsonValueKind.Array, payload.GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task GetInventoryTransactions_InvalidPageSize_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/management/inventory-transactions?pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("pageSize", payload.GetProperty("message").GetString());
    }
}
