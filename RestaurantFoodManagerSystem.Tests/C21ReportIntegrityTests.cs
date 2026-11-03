using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// C-21 (2026-10-21) — 6 lỗi làm **báo cáo sai** (đợt sửa trước khi làm thêm báo cáo vận hành).
///
/// <list type="number">
/// <item>description><c>ReasonCode</c> mất ở cấp **đơn** khi huỷ 1 món / bếp huỷ món cuối.</description></item>
/// <item>description>Ledger <c>Before/After</c> sai khi tách dòng ⇒ dựng lại tồn kho từ ledger ra số sai.</description></item>
/// <item>description>Ledger điều chỉnh tay & nhập phiếu mua không có nguyên nhân.</description></item>
/// <item>description>Danh sách tồn kho thấp không thấy nguyên liệu bị đánh dấu <c>Spoiled</c>/<c>OutOfStock</c>.</description></item>
/// <item>description><c>?since=</c> của bảng bếp bỏ sót thay đổi trạng thái **món**.</description></item>
/// <item>description>CSV chưa chặn formula injection.</description></item>
/// </list>
/// </summary>
public sealed class C21ReportIntegrityTests : TestBase
{
    // ============================================================
    // C-21.1 — ReasonCode phải xuống tới lịch sử cấp ĐƠN
    // ============================================================

    [Fact]
    public async Task CancelLastItem_WithReasonCode_OrderHistoryKeepsReasonCode()
    {
        var s = SeedOrderScenario();
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        var item = await Db.OrderItems.SingleAsync(x => x.OrderId == order.OrderId);

        // Huỷ món cuối cùng ⇒ đơn cũng chuyển Cancelled ⇒ dòng lịch sử cấp ĐƠN phải
        // mang ReasonCode, nếu không báo cáo "đơn huỷ vì hết hàng" bỏ sót mọi đơn
        // bị huỷ dần từng món.
        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            item.Id,
            new CancelOrderItemRequest
            {
                Reason = "Hết nguyên liệu",
                ReasonCode = "OutOfStock",
                ChangedBy = s.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();
        var orderHistory = check.OrderStatusHistories
            .Where(x => x.OrderId == order.OrderId)
            .ToList();

        Assert.Contains(orderHistory, x =>
            x.NewStatus == OrderStatuses.Cancelled && x.ReasonCode == "OutOfStock");
    }

    [Fact]
    public async Task KitchenCancelsLastItem_WithReasonCode_OrderHistoryKeepsReasonCode()
    {
        var s = SeedOrderScenario();
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        var item = await Db.OrderItems.SingleAsync(x => x.OrderId == order.OrderId);
        var kitchenItem = await Db.KitchenOrderItems.SingleAsync(x => x.OrderItemId == item.Id);

        // Bếp báo thiếu nguyên liệu (reasonCode "PhysicalShortage") trên món cuối cùng.
        await Kitchen.UpdateItemStatusAsync(
            kitchenItem.Id,
            new KitchenItemStatusRequest
            {
                Status = KitchenStatuses.Cancelled,
                Reason = "Lô gà bị hư",
                ReasonCode = "PhysicalShortage",
                ChangedBy = s.Staff.Id
            },
            CancellationToken.None);

        using var check = NewContext();
        var orderHistory = check.OrderStatusHistories
            .Where(x => x.OrderId == order.OrderId)
            .ToList();

        Assert.Contains(orderHistory, x =>
            x.NewStatus == OrderStatuses.Cancelled && x.ReasonCode == "PhysicalShortage");
    }

    // ============================================================
    // C-21.2 — Ledger giữ chuỗi running-balance khi tách dòng
    // ============================================================

    [Fact]
    public async Task SplitLedger_KeepsRunningBalanceChain_AndTotalUnchanged()
    {
        var s = SeedOrderScenario(stock: 100m);
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 2 }]
        }, CancellationToken.None);

        var item = await Db.OrderItems.SingleAsync(x => x.OrderId == order.OrderId);
        var kitchenItem = await Db.KitchenOrderItems.SingleAsync(x => x.OrderItemId == item.Id);

        // Tồn 100, công thức 2/phan ⇒ 2 phần = 4 ⇒ ledger: 100 -> 96.
        using (var before = NewContext())
        {
            var sale = before.InventoryTransactions
                .Single(x => x.OrderItemId == item.Id && x.Type == "Sale");
            Assert.Equal(4m, sale.Quanlity);
            Assert.Equal(100m, sale.QuanlityBefore);
            Assert.Equal(96m, sale.QuanlityAfter);
        }

        // Bếp báo nấu được 1 phần ⇒ tách dòng: 1 phần Completed + 1 phần Preparing.
        await Kitchen.UpdatePreparedQuantityAsync(
            kitchenItem.Id,
            new PreparedQuantityRequest { PreparedQuantity = 1, ChangedBy = s.Staff.Id },
            CancellationToken.None);

        using var check = NewContext();
        var rows = check.InventoryTransactions
            .Where(x => x.OrderItemId != null && x.Type == "Sale"
                        && check.OrderItems.Any(oi => oi.Id == x.OrderItemId && oi.OrderId == order.OrderId))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .ToList();

        Assert.Equal(2, rows.Count);

        // Mỗi dòng phải tự mô tả được: Before - After == Quanlity.
        foreach (var row in rows)
        {
            Assert.Equal(row.Quanlity, row.QuanlityBefore - row.QuanlityAfter);
        }

        // Tổng tiêu hao KHÔNG đổi (2 phần × 2 = 4) và mốc cuối vẫn là 96.
        Assert.Equal(4m, rows.Sum(x => x.Quanlity));
        Assert.Equal(100m, rows[0].QuanlityBefore);
        Assert.Equal(96m, rows[^1].QuanlityAfter);
    }

    // ============================================================
    // C-21.3 — Ledger điều chỉnh tay / nhập phiếu mua phải có nguyên nhân
    // ============================================================

    [Fact]
    public async Task AdjustInventory_WithoutNote_IsRejected()
    {
        var ingredient = SeedIngredient(50m);
        var staff = SeedUser();

        await Assert.ThrowsAsync<ArgumentException>(() => InventorySvc.AdjustAsync(
            ingredient.IngredientId,
            new InventoryAdjustmentRequest { Quanlity = -5m, CreatedBy = staff.Id },
            CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(() => InventorySvc.AdjustAsync(
            ingredient.IngredientId,
            new InventoryAdjustmentRequest { Quanlity = -5m, CreatedBy = staff.Id, Note = "   " },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Empty(check.InventoryTransactions);
        Assert.Equal(50m, check.Inventories
            .Single(x => x.IngredientId == ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task AdjustInventory_WithNote_WritesNoteAndManualAdjustmentReference()
    {
        var ingredient = SeedIngredient(50m);
        var staff = SeedUser();

        Assert.True(await InventorySvc.AdjustAsync(
            ingredient.IngredientId,
            new InventoryAdjustmentRequest
            {
                Quanlity = -5m,
                CreatedBy = staff.Id,
                Note = "Kiểm kê hao mất 5kg"
            },
            CancellationToken.None));

        using var check = NewContext();
        var row = Assert.Single(check.InventoryTransactions);
        Assert.Equal("AdjustmentOut", row.Type);
        Assert.Equal("Kiểm kê hao mất 5kg", row.Note);
        Assert.Equal("ManualAdjustment", row.ReferenceType);
        Assert.Null(row.ReferenceId);
    }

    [Fact]
    public async Task ReceivePurchase_WritesNoteWithSourceDocument()
    {
        var ingredient = SeedIngredient(10m);
        var supplier = SeedSupplier();
        var staff = SeedUser();

        var po = await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items = [new PurchaseOrderItemRequest { IngredientId = ingredient.IngredientId, Quanlity = 5m }]
        }, CancellationToken.None);

        Assert.True(await Purchases.ReceiveAsync(
            po.Id,
            new ReceivePurchaseRequest { ReceivedBy = staff.Id },
            CancellationToken.None));

        using var check = NewContext();
        var row = Assert.Single(check.InventoryTransactions);
        Assert.Equal("Purchase", row.Type);
        Assert.Equal("PurchaseOrder", row.ReferenceType);
        Assert.Equal(po.Id, row.ReferenceId);
        Assert.NotNull(row.Note);
        Assert.Contains(po.Id.ToString(), row.Note);
    }

    // ============================================================
    // C-21.4 — Danh sách tồn kho thấp phải thấy nguyên liệu hư/hết
    // ============================================================

    [Fact]
    public async Task LowStock_IncludesIngredientMarkedSpoiled_EvenWithPlentyOfStock()
    {
        var spoiled = SeedIngredient(50m);          // tồn vẫn rất nhiều
        spoiled.StockStatus = IngredientStockStatuses.Spoiled;
        Db.SaveChanges();

        var outOfStock = SeedIngredient(30m);
        outOfStock.StockStatus = IngredientStockStatuses.OutOfStock;
        Db.SaveChanges();

        var normal = SeedIngredient(20m);           // tồn cao, status Available ⇒ KHÔNG vào list
        normal.StockStatus = IngredientStockStatuses.Available;
        Db.SaveChanges();

        var low = SeedIngredient(1m, minimumStock: 5m);  // tồn thấp thật sự
        Db.SaveChanges();

        var rows = await InventorySvc.GetLowStockAsync(CancellationToken.None);

        Assert.Contains(rows, x => x.IngredientId == spoiled.IngredientId
                                   && x.StockStatus == IngredientStockStatuses.Spoiled);
        Assert.Contains(rows, x => x.IngredientId == outOfStock.IngredientId
                                   && x.StockStatus == IngredientStockStatuses.OutOfStock);
        Assert.Contains(rows, x => x.IngredientId == low.IngredientId);
        Assert.DoesNotContain(rows, x => x.IngredientId == normal.IngredientId);
    }

    // ============================================================
    // C-21.5 — ?since= của bảng bếp phải bắt được đổi trạng thái MÓN
    // ============================================================

    [Fact]
    public async Task KitchenOrders_Since_ReturnsTicketWhoseOnlyDishStatusChanged()
    {
        var s = SeedOrderScenario();
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.Staff.Id,
            Items =
            [
                new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 },
                new CreateOrderItemRequest { SizeId = s.Size.SizeId, Quanlity = 1 }
            ]
        }, CancellationToken.None);

        var ticket = await Db.KitchenOrders.SingleAsync(k => k.OrderId == order.OrderId);
        var kitchenItems = await Db.KitchenOrderItems
            .Where(x => x.KitchenOrderId == ticket.KitchenOrderId)
            .OrderBy(x => x.Id)
            .ToListAsync();

        // Đẩy mốc của PHIẾU về 1 giờ trước ⇒ mọi mốc của phiếu (Sent/Started/Completed)
        // đều nằm trước `mark` ⇒ bộ lọc `since` cũ KHÔNG trả về phiếu này.
        var ticketMark = DateTime.UtcNow.AddHours(-1);
        ticket.SentAt = ticketMark;
        ticket.StartedAt = ticketMark;
        ticket.CompletedAt = null;
        await Db.SaveChangesAsync();

        // Món 1 Ready, món 2 vẫn Preparing ⇒ phiếu vẫn Preparing ⇒ không có mốc mới nào
        // trên phiếu. Chỉ có lịch sử MÓN là thay đổi.
        await Kitchen.UpdateItemStatusAsync(
            kitchenItems[0].Id,
            new KitchenItemStatusRequest { Status = KitchenStatuses.Ready, ChangedBy = s.Staff.Id },
            CancellationToken.None);

        // `mark` nằm GIỮA mốc của phiếu và lúc đổi trạng thái món.
        var mark = ticketMark.AddMinutes(1);

        var result = await Kitchen.GetOrdersAsync(
            CancellationToken.None, since: mark, page: null, pageSize: null);

        Assert.Contains(result, x => x.KitchenOrderId == ticket.KitchenOrderId);
    }

    // ============================================================
    // C-21.6 — CSV chặn formula injection
    // ============================================================

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd|'/c calc'!A0", "'+cmd|'/c calc'!A0")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-cmd|'/c calc'!A0", "'-cmd|'/c calc'!A0")]
    public void EscapeCsvCell_NeutralizesFormula(string input, string expected)
    {
        Assert.Equal(expected, ReportService.EscapeCsvCell(input));
    }

    [Theory]
    [InlineData("150")]
    [InlineData("-5")]      // số âm là dữ liệu số hợp lệ ⇒ KHÔNG bị chặn
    [InlineData("Gà rán")]
    [InlineData("")]
    public void EscapeCsvCell_KeepsNormalValues(string input)
    {
        Assert.Equal(input, ReportService.EscapeCsvCell(input));
    }

    [Fact]
    public void EscapeCsvCell_StillEscapesSeparatorsAndQuotes()
    {
        Assert.Equal("\"a,b\"", ReportService.EscapeCsvCell("a,b"));
        Assert.Equal("\"a\"\"b\"", ReportService.EscapeCsvCell("a\"b"));
    }
}
