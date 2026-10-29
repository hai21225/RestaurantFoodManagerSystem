using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Đợt 3 — "Đủ vận hành".
///
/// <list type="bullet">
/// <item><description><b>B2</b> — hoá đơn gộp theo bàn: <c>GET /api/orders/table/{id}/bill</c>.</description></item>
/// <item><description><b>B3</b> — ghi chú cho cả hoá đơn: <c>Order.Note</c> + <c>PATCH /api/orders/{id}/note</c>.</description></item>
/// <item><description><b>B4</b> — danh sách tồn kho thấp: <c>GET /api/management/inventory/low-stock</c>.</description></item>
/// <item><description><b>B6</b> — bảng bếp: <c>?since=</c> (chỉ phiếu thay đổi) + phân trang tuỳ chọn.</description></item>
/// </list>
///
/// <para>
/// Lưu ý về B2: luật nghiệp vụ hiện tại là **1 bàn chỉ có 1 đơn đang mở**
/// (<c>OrderService.CreateAsync</c> chặn "Table already has an open order"). Endpoint hoá đơn
/// gộp vẫn gộp **mọi** đơn đang mở ⇒ đúng cả khi dữ liệu cũ có nhiều đơn trên 1 bàn.
/// </para>
/// </summary>
public sealed class Doi3OperationsTests : TestBase
{
    /// <summary>Tạo 1 đơn có dòng món, ghi thẳng DB (bỏ qua luật "1 bàn = 1 đơn mở").</summary>
    private async Task<Order> SeedOrderWithItemsAsync(
        int tableId,
        int createdBy,
        int sizeId,
        int quantity,
        string status = "Ready",
        string? note = null)
    {
        var order = new Order
        {
            TableId = tableId,
            CreatedBy = createdBy,
            Status = status,
            Subtotal = 100m * quantity,
            TotalAmount = 100m * quantity,
            Note = note,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Db.Orders.Add(order);
        await Db.SaveChangesAsync();

        Db.OrderItems.Add(new OrderItem
        {
            OrderId = order.OrderId,
            SizeId = sizeId,
            Quanlity = quantity,
            UnitPrice = 100m,
            Status = "Ready",
            CreatedAt = DateTime.UtcNow,
            OrderItemGroupId = Guid.NewGuid()
        });
        await Db.SaveChangesAsync();

        return order;
    }

    // ==========================================================
    // B2 — hoá đơn gộp theo bàn
    // ==========================================================

    [Fact]
    public async Task GetTableBill_SumsOpenOrders_AndReturnsItems()
    {
        var scenario = SeedOrderScenario(sizePrice: 100m, stock: 100m);
        var staff = SeedUser();

        var first = await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1);
        var second = await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 2);

        var bill = await Orders.GetTableBillAsync(scenario.Table.TableId, CancellationToken.None);

        Assert.NotNull(bill);
        Assert.Equal(2, bill!.OrderCount);
        Assert.Equal(new[] { first.OrderId, second.OrderId }, bill.OrderIds);
        Assert.Equal(300m, bill.Subtotal);
        Assert.Equal(300m, bill.TotalAmount);

        // 1 + 2 phần cùng món, cùng đơn giá, cùng ghi chú ⇒ gộp thành 1 dòng.
        var line = Assert.Single(bill.Items);
        Assert.Equal(3, line.Quanlity);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(300m, line.LineTotal);
        Assert.Equal(scenario.Size.SizeId, line.SizeId);
    }

    [Fact]
    public async Task GetTableBill_ExcludesPaidAndCancelledOrders()
    {
        var scenario = SeedOrderScenario(sizePrice: 100m, stock: 100m);
        var staff = SeedUser();

        await SeedOrderWithItemsAsync(scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1, status: "Paid");
        await SeedOrderWithItemsAsync(scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1, status: "Cancelled");
        var open = await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 2);

        var bill = await Orders.GetTableBillAsync(scenario.Table.TableId, CancellationToken.None);

        Assert.NotNull(bill);
        Assert.Equal(1, bill!.OrderCount);
        Assert.Equal(new[] { open.OrderId }, bill.OrderIds);
        Assert.Equal(200m, bill.Subtotal);
    }

    [Fact]
    public async Task GetTableBill_ExcludesCancelledItems()
    {
        var scenario = SeedOrderScenario(sizePrice: 100m, stock: 100m);
        var staff = SeedUser();

        var order = await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1);

        // Thêm 1 dòng nữa rồi huỷ dòng đầu ⇒ hoá đơn chỉ còn dòng còn lại.
        Db.OrderItems.Add(new OrderItem
        {
            OrderId = order.OrderId,
            SizeId = scenario.Size.SizeId,
            Quanlity = 1,
            UnitPrice = 100m,
            Status = "Ready",
            CreatedAt = DateTime.UtcNow,
            OrderItemGroupId = Guid.NewGuid()
        });
        Db.SaveChanges();

        var first = Db.OrderItems.First(x => x.OrderId == order.OrderId);
        first.Status = "Cancelled";
        first.CancelledAt = DateTime.UtcNow;
        Db.SaveChanges();

        var bill = await Orders.GetTableBillAsync(scenario.Table.TableId, CancellationToken.None);

        Assert.NotNull(bill);
        var line = Assert.Single(bill!.Items);
        Assert.Equal(1, line.Quanlity);
        Assert.Equal(100m, line.LineTotal);
    }

    [Fact]
    public async Task GetTableBill_KeepsDifferentNotesAsSeparateLines()
    {
        var scenario = SeedOrderScenario(sizePrice: 100m, stock: 100m);
        var staff = SeedUser();

        var order = await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1);

        Db.OrderItems.Add(new OrderItem
        {
            OrderId = order.OrderId,
            SizeId = scenario.Size.SizeId,
            Quanlity = 1,
            UnitPrice = 100m,
            Note = "bỏ hành",
            Status = "Ready",
            CreatedAt = DateTime.UtcNow,
            OrderItemGroupId = Guid.NewGuid()
        });
        Db.SaveChanges();

        var bill = await Orders.GetTableBillAsync(scenario.Table.TableId, CancellationToken.None);

        Assert.NotNull(bill);
        Assert.Equal(2, bill!.Items.Count);
        Assert.Contains(bill.Items, x => x.Note == "bỏ hành" && x.Quanlity == 1);
        Assert.Contains(bill.Items, x => x.Note == null && x.Quanlity == 1);
    }

    [Fact]
    public async Task GetTableBill_CollectsOrderNotes()
    {
        var scenario = SeedOrderScenario(sizePrice: 100m, stock: 100m);
        var staff = SeedUser();

        await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1, note: "Xuất hoá đơn công ty ABC");
        await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1, note: "  Chia đôi với bàn 6  ");

        var bill = await Orders.GetTableBillAsync(scenario.Table.TableId, CancellationToken.None);

        Assert.NotNull(bill);
        Assert.Equal(2, bill!.Notes.Count);
        Assert.Contains("Xuất hoá đơn công ty ABC", bill.Notes);
        Assert.Contains("Chia đôi với bàn 6", bill.Notes);
    }

    [Fact]
    public async Task GetTableBill_ReturnsNull_WhenNoOpenOrder()
    {
        var scenario = SeedOrderScenario(sizePrice: 100m, stock: 100m);
        var staff = SeedUser();

        await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1, status: "Paid");

        Assert.Null(await Orders.GetTableBillAsync(scenario.Table.TableId, CancellationToken.None));
    }

    [Fact]
    public async Task GetTableBill_ReturnsNull_WhenTableMissing()
    {
        Assert.Null(await Orders.GetTableBillAsync(999999, CancellationToken.None));
    }

    // ==========================================================
    // B3 — ghi chú hoá đơn
    // ==========================================================

    [Fact]
    public async Task UpdateNote_SetsNote_AndRecordsHistory()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var staff = SeedUser();
        var order = await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1);

        Assert.True(await Orders.UpdateNoteAsync(
            order.OrderId,
            new UpdateOrderNoteRequest { Note = "Xuất hoá đơn công ty ABC", ChangedBy = staff.Id },
            CancellationToken.None));

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("Xuất hoá đơn công ty ABC", saved.Note);

        // Mọi thay đổi ghi chú đều được truy vết trong lịch sử đơn.
        var history = check.OrderStatusHistories
            .Where(x => x.OrderId == order.OrderId)
            .OrderByDescending(x => x.Id)
            .First();
        Assert.Contains("Updated bill note", history.Note);
        Assert.Equal(staff.Id, history.ChangedBy);
    }

    [Fact]
    public async Task UpdateNote_EmptyString_ClearsNote()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var staff = SeedUser();
        var order = await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1, note: "ghi chú cũ");

        Assert.True(await Orders.UpdateNoteAsync(
            order.OrderId,
            new UpdateOrderNoteRequest { Note = "   ", ChangedBy = staff.Id },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Null(check.Orders.Single(x => x.OrderId == order.OrderId).Note);
    }

    [Fact]
    public async Task UpdateNote_UnknownOrder_ReturnsFalse()
    {
        Assert.False(await Orders.UpdateNoteAsync(
            999999,
            new UpdateOrderNoteRequest { Note = "x" },
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateNote_SameValue_DoesNotAddHistory()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var staff = SeedUser();
        var order = await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1, note: "giữ nguyên");

        var before = Db.OrderStatusHistories.Count(x => x.OrderId == order.OrderId);

        Assert.True(await Orders.UpdateNoteAsync(
            order.OrderId,
            new UpdateOrderNoteRequest { Note = "giữ nguyên", ChangedBy = staff.Id },
            CancellationToken.None));

        Assert.Equal(before, Db.OrderStatusHistories.Count(x => x.OrderId == order.OrderId));
    }

    [Fact]
    public async Task CreateOrder_WithNote_StoresNote()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id,
            Note = "Khách đi trước, giữ phần món sau",
            Items = [new CreateOrderItemRequest { SizeId = scenario.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("Khách đi trước, giữ phần món sau", saved.Note);
    }

    [Fact]
    public async Task GetAsync_ReturnsNote()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var staff = SeedUser();
        var order = await SeedOrderWithItemsAsync(
            scenario.Table.TableId, staff.Id, scenario.Size.SizeId, 1, note: "ghi chú cần in");

        var detail = await Orders.GetAsync(order.OrderId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("ghi chú cần in", detail!.Note);
    }

    // ==========================================================
    // B4 — tồn kho thấp
    // ==========================================================

    [Fact]
    public async Task GetLowStock_ReturnsOnlyItemsAtOrBelowMinimum()
    {
        var low = SeedIngredient(1m, minimumStock: 5m, unit: "kg");
        var ok = SeedIngredient(10m, minimumStock: 5m, unit: "kg");

        var result = await InventorySvc.GetLowStockAsync(CancellationToken.None);

        Assert.Single(result);
        var row = result[0];
        Assert.Equal(low.IngredientId, row.IngredientId);
        Assert.Equal("kg", row.Unit);
        Assert.Equal(1m, row.Quanlity);
        Assert.Equal(5m, row.MinimumStock);
        Assert.Equal(4m, row.Shortage);
        Assert.False(row.IsOutOfStock);
        Assert.DoesNotContain(result, x => x.IngredientId == ok.IngredientId);
    }

    [Fact]
    public async Task GetLowStock_MarksOutOfStock()
    {
        var ingredient = SeedIngredient(0m, minimumStock: 2m);

        var row = Assert.Single(await InventorySvc.GetLowStockAsync(CancellationToken.None));

        Assert.Equal(ingredient.IngredientId, row.IngredientId);
        Assert.True(row.IsOutOfStock);
        Assert.Equal(2m, row.Shortage);
    }

    [Fact]
    public async Task GetLowStock_ExcludesInactiveIngredients()
    {
        var ingredient = SeedIngredient(0m, minimumStock: 2m);
        Db.Ingredients.Single(x => x.IngredientId == ingredient.IngredientId).IsActive = false;
        Db.SaveChanges();

        Assert.Empty(await InventorySvc.GetLowStockAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetLowStock_OrdersByShortageDescending()
    {
        SeedIngredient(4m, minimumStock: 5m);   // thiếu 1
        SeedIngredient(0m, minimumStock: 10m);  // thiếu 10

        var result = await InventorySvc.GetLowStockAsync(CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(10m, result[0].Shortage);
        Assert.Equal(1m, result[1].Shortage);
    }

    // ==========================================================
    // B6 — bảng bếp: ?since= và phân trang
    // ==========================================================

    [Fact]
    public async Task GetOrders_Since_ReturnsOnlyChangedTickets()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = scenario.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        var ticket = Db.KitchenOrders.Single();

        // Mốc nằm sau khi phiếu được gửi ⇒ không có gì thay đổi ⇒ không trả phiếu nào.
        var future = ticket.SentAt.AddMinutes(5);
        Assert.Empty(await Kitchen.GetOrdersAsync(CancellationToken.None, "open", future));

        // Mốc nằm trước khi gửi ⇒ phiếu mới xuất hiện.
        var past = ticket.SentAt.AddMinutes(-5);
        var result = await Kitchen.GetOrdersAsync(CancellationToken.None, "open", past);
        Assert.Single(result);
        Assert.Equal(ticket.KitchenOrderId, result[0].KitchenOrderId);
    }

    [Fact]
    public async Task GetOrders_Since_IncludesTicketThatStartedAfterMark()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = scenario.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        var ticket = Db.KitchenOrders.Single();
        var mark = ticket.SentAt.AddMinutes(-5);

        // Phiếu được gửi trước mốc, nhưng bếp bắt đầu nấu sau mốc ⇒ phải trả lại.
        ticket.StartedAt = mark.AddMinutes(2);
        Db.SaveChanges();

        var result = await Kitchen.GetOrdersAsync(CancellationToken.None, "open", mark);

        Assert.Single(result);
        Assert.Equal(ticket.KitchenOrderId, result[0].KitchenOrderId);
    }

    [Fact]
    public async Task GetOrders_WithoutSince_ReturnsEverything()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = scenario.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        var result = await Kitchen.GetOrdersAsync(CancellationToken.None);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetOrders_Page_ReturnsSlice()
    {
        for (var i = 0; i < 3; i++)
        {
            var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
            await Orders.CreateAsync(new CreateOrderRequest
            {
                TableId = scenario.Table.TableId,
                CreatedBy = scenario.Staff.Id,
                Items = [new CreateOrderItemRequest { SizeId = scenario.Size.SizeId, Quanlity = 1 }]
            }, CancellationToken.None);
        }

        var all = await Kitchen.GetOrdersAsync(CancellationToken.None);
        Assert.Equal(3, all.Count);

        var firstPage = await Kitchen.GetOrdersAsync(CancellationToken.None, null, null, 1, 2);
        Assert.Equal(2, firstPage.Count);

        var secondPage = await Kitchen.GetOrdersAsync(CancellationToken.None, null, null, 2, 2);
        Assert.Single(secondPage);
    }
}

/// <summary>
/// Đợt 3 — hợp đồng HTTP: bill theo bàn, ghi chú hoá đơn, tồn kho thấp, bảng bếp.
/// </summary>
public sealed class Doi3OperationsHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public Doi3OperationsHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    private (int TableId, int OrderId, int IngredientId) SeedScenario()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var suffix = Guid.NewGuid().ToString("N");
        var userName = $"d3-{suffix}";
        var user = new Users
        {
            UserName = userName,
            UserPassword = "not-used",
            UserFullName = "Đợt 3 Test",
            UserEmail = $"{userName}@test.local",
            UserPhone = "0000000000",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);

        var table = new RestaurantTable
        {
            TableNumber = $"D{Guid.NewGuid():N}"[..12],
            Capacity = 4,
            Status = RestaurantTableStatuses.Available,
            IsActive = true
        };
        db.RestaurantTables.Add(table);

        var category = new Category
        {
            CategoryName = $"cat-{suffix}",
            CategoryDescription = "desc",
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
            FoodDescription = "desc",
            FoodImageUrl = "img.png",
            FoodPrice = 100m,
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
            SizePrice = 100m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.FoodSizes.Add(size);

        var ingredient = new Ingredient
        {
            Name = $"ing-{suffix}",
            Unit = "kg",
            MinimumStock = 5m,
            IsActive = true
        };
        db.Ingredients.Add(ingredient);
        db.SaveChanges();

        db.Inventories.Add(new Inventory
        {
            IngredientId = ingredient.IngredientId,
            Quanlity = 1m,
            UpdatedAt = DateTime.UtcNow
        });
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

        db.OrderItems.Add(new OrderItem
        {
            OrderId = order.OrderId,
            SizeId = size.SizeId,
            Quanlity = 1,
            UnitPrice = 100m,
            Status = "Ready",
            CreatedAt = DateTime.UtcNow,
            OrderItemGroupId = Guid.NewGuid()
        });
        db.SaveChanges();

        return (table.TableId, order.OrderId, ingredient.IngredientId);
    }

    [Fact]
    public async Task GetTableBill_ReturnsBill()
    {
        var (tableId, orderId, _) = SeedScenario();
        using var client = _factory.CreateClient();

        var response = await client.GetFromJsonAsync<JsonElement>(
            $"/api/orders/table/{tableId}/bill");

        Assert.Equal(tableId, response.GetProperty("tableId").GetInt32());
        Assert.Equal(1, response.GetProperty("orderCount").GetInt32());
        Assert.Equal(new[] { orderId }, response.GetProperty("orderIds").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(100m, response.GetProperty("totalAmount").GetDecimal());
        Assert.Single(response.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GetTableBill_NoOpenOrder_ReturnsNotFound()
    {
        var (tableId, _, _) = SeedScenario();
        using var client = _factory.CreateClient();

        // Huỷ đơn ⇒ bàn không còn đơn mở.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var order in db.Orders.Where(x => x.TableId == tableId))
            {
                order.Status = OrderStatuses.Cancelled;
            }
            db.SaveChanges();
        }

        var response = await client.GetAsync($"/api/orders/table/{tableId}/bill");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PatchOrderNote_SetsNote_AndIsReturnedInDetail()
    {
        var (_, orderId, _) = SeedScenario();
        using var client = _factory.CreateClient();

        var patch = await client.PatchAsJsonAsync($"/api/orders/{orderId}/note", new
        {
            note = "Xuất hoá đơn công ty ABC",
            changedBy = 0
        });

        Assert.Equal(HttpStatusCode.NoContent, patch.StatusCode);

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/orders/{orderId}");
        Assert.Equal("Xuất hoá đơn công ty ABC", detail.GetProperty("note").GetString());
    }

    [Fact]
    public async Task GetLowStock_ReturnsList()
    {
        var (_, _, ingredientId) = SeedScenario();
        using var client = _factory.CreateClient();

        var rows = await client.GetFromJsonAsync<JsonElement>(
            "/api/management/inventory/low-stock");

        var row = rows.EnumerateArray()
            .Single(x => x.GetProperty("ingredientId").GetInt32() == ingredientId);

        Assert.Equal("kg", row.GetProperty("unit").GetString());
        Assert.Equal(1m, row.GetProperty("quanlity").GetDecimal());
        Assert.Equal(5m, row.GetProperty("minimumStock").GetDecimal());
        Assert.Equal(4m, row.GetProperty("shortage").GetDecimal());
        Assert.False(row.GetProperty("isOutOfStock").GetBoolean());
    }

    [Fact]
    public async Task GetKitchenOrders_WithSince_ReturnsArray()
    {
        using var client = _factory.CreateClient();

        var since = DateTime.UtcNow.AddDays(-1).ToString("O");

        var response = await client.GetAsync($"/api/management/kitchen/orders?status=open&since={Uri.EscapeDataString(since)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Response vẫn là **mảng** (không đổi shape ⇒ không phá frontend).
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, payload.ValueKind);
    }

    [Fact]
    public async Task GetKitchenOrders_InvalidPageSize_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/management/kitchen/orders?page=1&pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
