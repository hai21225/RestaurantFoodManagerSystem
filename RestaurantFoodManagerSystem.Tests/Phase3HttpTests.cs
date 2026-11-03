using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Đợt 3 — hợp đồng HTTP: chuyển bàn, luồng MoMo (kể cả chế độ giả lập) và hoàn tiền.
/// </summary>
public sealed class Phase3HttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public Phase3HttpTests(RestaurantApiFactory factory)
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
            UserFullName = "Phase3 Test",
            UserEmail = $"{userName}@test.local",
            UserPhone = "0000000000",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>Tạo bàn + người + đơn đang chờ thu (Ready) để thu tiền được.</summary>
    private (int TableId, int OtherTableId, int UserId, int OrderId) SeedPayableScenario()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = NewUser("p3-pay");
        var table = new RestaurantTable
        {
            TableNumber = $"P{Guid.NewGuid():N}"[..12],
            Capacity = 4,
            Status = RestaurantTableStatuses.Available,
            IsActive = true
        };
        var otherTable = new RestaurantTable
        {
            TableNumber = $"Q{Guid.NewGuid():N}"[..12],
            Capacity = 4,
            Status = RestaurantTableStatuses.Available,
            IsActive = true
        };

        db.Users.Add(user);
        db.RestaurantTables.Add(table);
        db.RestaurantTables.Add(otherTable);
        db.SaveChanges();

        var order = new Order
        {
            TableId = table.TableId,
            CreatedBy = user.Id,
            Status = OrderStatuses.Ready,
            Subtotal = 100m,
            TotalAmount = 100m,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Orders.Add(order);
        db.SaveChanges();

        return (table.TableId, otherTable.TableId, user.Id, order.OrderId);
    }

    // ==========================================================
    // Chuyển bàn
    // ==========================================================

    [Fact]
    public async Task PatchOrderTable_MovesOrderToNewTable()
    {
        var (tableId, otherTableId, userId, orderId) = SeedPayableScenario();
        using var client = _factory.CreateClient();

        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/table",
            new { tableId = otherTableId, changedBy = userId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal(otherTableId, detail.GetProperty("tableId").GetInt32());
    }

    [Fact]
    public async Task PatchOrderTable_TargetTableHasOpenOrder_ReturnsConflict()
    {
        var (tableId, otherTableId, userId, orderId) = SeedPayableScenario();

        // Tạo thêm 1 đơn mở trên bàn đích ⇒ bàn đó đang có người.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.Add(new Order
            {
                TableId = otherTableId,
                CreatedBy = userId,
                Status = OrderStatuses.Pending,
                Subtotal = 50m,
                TotalAmount = 50m,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }

        using var client = _factory.CreateClient();
        var response = await client.PatchAsJsonAsync(
            $"/api/orders/{orderId}/table",
            new { tableId = otherTableId, changedBy = userId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ==========================================================
    // MoMo (chế độ giả lập)
    // ==========================================================

    [Fact]
    public async Task PostMomoPayment_ReturnsMockUrl_ThenMockPay_ClosesTheOrder()
    {
        var (_, _, userId, orderId) = SeedPayableScenario();
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/payments/momo", new { orderId, createdBy = userId });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var intent = await create.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Pending", intent.GetProperty("status").GetString());
        Assert.Equal("MoMo", intent.GetProperty("provider").GetString());
        Assert.True(intent.GetProperty("isMock").GetBoolean());

        var paymentId = intent.GetProperty("paymentId").GetInt32();

        // Đơn CHƯA đóng khi mới tạo intent.
        var before = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("Ready", before.GetProperty("status").GetString());

        // Bấm "thanh toán" ở chế độ giả lập ⇒ đóng đơn.
        var mock = await client.PostAsync($"/api/payments/momo/mock/{paymentId}?success=true", null);
        Assert.Equal(HttpStatusCode.OK, mock.StatusCode);

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("Paid", after.GetProperty("status").GetString());

        var payments = await client.GetFromJsonAsync<JsonElement>($"/api/payments/order/{orderId}");
        var paid = payments.EnumerateArray().Single(x => x.GetProperty("paymentId").GetInt32() == paymentId);
        Assert.Equal("Paid", paid.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PostMomoNotify_WithBadSignature_DoesNotCloseTheOrder()
    {
        var (_, _, userId, orderId) = SeedPayableScenario();
        using var client = _factory.CreateClient();

        var create = await client.PostAsJsonAsync("/api/payments/momo", new { orderId, createdBy = userId });
        var intent = await create.Content.ReadFromJsonAsync<JsonElement>();
        var requestId = intent.GetProperty("requestId").GetString();

        var notify = await client.PostAsJsonAsync("/api/payments/momo/notify", new
        {
            partnerCode = "MOCK_PARTNER",
            partnerRefCode = orderId.ToString(),
            amount = "100",
            orderId = orderId.ToString(),
            orderInfo = $"Thanh toan don {orderId}",
            requestId,
            requestType = "capture",
            resultCode = "9000",
            responseCode = "9000",
            extraData = "1",
            message = "ok",
            transId = "MOCK-1",
            signature = "0123456789abcdef0123456789abcdef"
        });

        Assert.Equal(HttpStatusCode.OK, notify.StatusCode);
        var body = await notify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("accepted").GetBoolean());

        var order = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("Ready", order.GetProperty("status").GetString());
    }

    // ==========================================================
    // Hoàn tiền
    // ==========================================================

    [Fact]
    public async Task PostPaymentRefund_MarksRefunded_AndReopensOrder()
    {
        var (_, _, userId, orderId) = SeedPayableScenario();
        using var client = _factory.CreateClient();

        var pay = await client.PostAsJsonAsync("/api/payments", new
        {
            orderId,
            paymentMethod = "Cash",
            createdBy = userId
        });
        Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
        var payment = await pay.Content.ReadFromJsonAsync<JsonElement>();
        var paymentId = payment.GetProperty("paymentId").GetInt32();

        var refund = await client.PostAsJsonAsync($"/api/payments/{paymentId}/refund", new
        {
            reason = "Khách huỷ món",
            createdBy = userId
        });

        Assert.Equal(HttpStatusCode.OK, refund.StatusCode);
        var refunded = await refund.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Refunded", refunded.GetProperty("status").GetString());
        Assert.True(refunded.GetProperty("refundedAt").GetDateTime() > DateTime.MinValue);

        var order = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("Ready", order.GetProperty("status").GetString());
    }
}
