using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// P1: <c>GET /api/orders</c> và <c>GET /api/payments</c> — lọc, phân trang, dữ liệu join.
/// Tầng service (SQLite in-memory) + tầng HTTP (WebApplicationFactory).
/// </summary>
public sealed class ListEndpointsServiceTests : TestBase
{
    [Fact]
    public async Task Orders_List_FiltersByStatusAndTable_WithTableNumberAndItemCount()
    {
        var s = SeedOrderScenario();
        var other = SeedOrderScenario();

        var matching = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Pending", subtotal: 100m);
        SeedOrder(s.Table.TableId, s.Staff.Id, status: "Paid", subtotal: 200m);
        SeedOrder(other.Table.TableId, other.Staff.Id, status: "Pending", subtotal: 300m);

        Db.OrderItems.Add(new OrderItem { OrderId = matching.OrderId, SizeId = s.Size.SizeId, Quanlity = 2, UnitPrice = 50m, Status = "Pending", CreatedAt = DateTime.UtcNow });
        Db.OrderItems.Add(new OrderItem { OrderId = matching.OrderId, SizeId = s.Size.SizeId, Quanlity = 1, UnitPrice = 50m, Status = "Pending", CreatedAt = DateTime.UtcNow });
        Db.SaveChanges();

        // "pending" chữ thường cũng phải khớp (lọc không phân biệt hoa/thường).
        var result = await Orders.GetListAsync(new OrderListQuery { Status = "pending", TableId = s.Table.TableId });

        Assert.Equal(1, result.Total);
        var row = Assert.Single(result.Items);
        Assert.Equal(matching.OrderId, row.OrderId);
        Assert.Equal(s.Table.TableNumber, row.TableNumber);
        Assert.Equal(2, row.ItemCount);
        Assert.Equal(100m, row.TotalAmount);
        Assert.Equal(s.Staff.Id, row.CreatedBy);
    }

    [Fact]
    public async Task Orders_List_Paginates_NewestFirst_NoOverlap()
    {
        var s = SeedOrderScenario();
        for (var i = 0; i < 5; i++)
        {
            SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: i + 1);
        }

        var page1 = await Orders.GetListAsync(new OrderListQuery { Page = 1, PageSize = 2 });
        var page2 = await Orders.GetListAsync(new OrderListQuery { Page = 2, PageSize = 2 });

        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.True(page1.Items[0].OrderId > page1.Items[1].OrderId, "Đơn mới nhất phải đứng đầu.");
        Assert.Empty(page1.Items.Select(x => x.OrderId).Intersect(page2.Items.Select(x => x.OrderId)));
    }

    [Fact]
    public async Task Orders_List_FiltersByCreatedAtRange()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        var included = await Orders.GetListAsync(new OrderListQuery
        {
            From = DateTime.UtcNow.AddMinutes(-5),
            To = DateTime.UtcNow.AddMinutes(5)
        });
        Assert.Contains(included.Items, x => x.OrderId == order.OrderId);

        var excluded = await Orders.GetListAsync(new OrderListQuery { From = DateTime.UtcNow.AddMinutes(5) });
        Assert.DoesNotContain(excluded.Items, x => x.OrderId == order.OrderId);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Orders_List_RejectsInvalidPaging(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Orders.GetListAsync(new OrderListQuery { Page = page, PageSize = pageSize }));
    }

    [Fact]
    public async Task Payments_List_FiltersByMethodAndStatus()
    {
        var s = SeedOrderScenario();
        var order1 = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 100m);
        var order2 = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 200m);

        await Payments.CreateAsync(new CreatePaymentRequest { OrderId = order1.OrderId, PaymentMethod = "Cash", CreatedBy = s.Staff.Id }, CancellationToken.None);
        await Payments.CreateAsync(new CreatePaymentRequest { OrderId = order2.OrderId, PaymentMethod = "Card", CreatedBy = s.Staff.Id }, CancellationToken.None);

        var cash = await Payments.GetListAsync(new PaymentListQuery { Method = "cash", Status = "paid" });

        Assert.Equal(1, cash.Total);
        var row = Assert.Single(cash.Items);
        Assert.Equal(order1.OrderId, row.OrderId);
        Assert.Equal("Cash", row.PaymentMethod);
        Assert.Equal("Paid", row.Status);
        Assert.Equal(100m, row.Amount);
    }

    [Fact]
    public async Task Payments_List_RejectsInvalidPage()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Payments.GetListAsync(new PaymentListQuery { Page = 0 }));
    }
}

public sealed class ListEndpointsHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public ListEndpointsHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    private (int TableId, int OrderId) SeedTableAndOrder()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userName = $"list-{Guid.NewGuid():N}";
        var user = new Users
        {
            UserName = userName,
            UserPassword = "not-used",
            UserFullName = "List Test",
            UserEmail = $"{userName}@test.local",
            UserPhone = "0000000000",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);

        var table = new RestaurantTable
        {
            TableNumber = $"L{Guid.NewGuid():N}"[..12],
            Capacity = 4,
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
            Subtotal = 42m,
            TotalAmount = 42m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);
        db.SaveChanges();

        return (table.TableId, order.OrderId);
    }

    [Fact]
    public async Task GetOrders_ReturnsPagedEnvelope_WithJoinedFields()
    {
        var (tableId, orderId) = SeedTableAndOrder();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/orders?tableId={tableId}&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(payload.TryGetProperty("page", out _));
        Assert.True(payload.TryGetProperty("pageSize", out _));
        Assert.True(payload.TryGetProperty("total", out var total));
        Assert.True(total.GetInt32() >= 1);

        var order = payload.GetProperty("items").EnumerateArray()
            .Single(x => x.GetProperty("orderId").GetInt32() == orderId);

        Assert.Equal(tableId, order.GetProperty("tableId").GetInt32());
        Assert.Equal("Pending", order.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(order.GetProperty("tableNumber").GetString()));
        Assert.Equal(0, order.GetProperty("itemCount").GetInt32());
        Assert.False(order.TryGetProperty("rowVersion", out _));
    }

    [Fact]
    public async Task GetOrders_InvalidPageSize_ReturnsBadRequestWithMessage()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/orders?pageSize=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("pageSize", payload.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetPayments_ReturnsPagedEnvelope()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/payments?pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, payload.GetProperty("page").GetInt32());
        Assert.Equal(5, payload.GetProperty("pageSize").GetInt32());
        Assert.True(payload.TryGetProperty("total", out _));
        Assert.Equal(JsonValueKind.Array, payload.GetProperty("items").ValueKind);
    }
}
