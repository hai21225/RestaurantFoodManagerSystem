using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// C-13 — luật vòng đời bếp ↔ kho ↔ hoàn kho (chốt 2026-10-21, xem
/// <c>docs/architecture/kitchen-stock-rule-2026-10-21.md</c>).
///
/// <para>Điểm cốt lõi: bếp bấm <c>Preparing</c> là mốc "nguyên liệu đã tiêu thật".
/// Từ đó món đó không hoàn kho được nữa — mọi thay đổi đều ghi hao hụt (wastage).</para>
/// </summary>
public sealed class KitchenStockRuleTests : TestBase
{
    private sealed record CreatedOrder(
        OrderScenario Scenario,
        Order Order,
        OrderItem Item,
        KitchenOrder Ticket,
        KitchenOrderItem KitchenItem);

    /// <summary>Tạo đơn thật (qua service) để có ledger kho theo món.</summary>
    private async Task<CreatedOrder> CreateOrderAsync(int quantity = 2, decimal stock = 100m)
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: stock);
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id,
            Items =
            [
                new CreateOrderItemRequest
                {
                    SizeId = scenario.Size.SizeId,
                    Quanlity = quantity
                }
            ]
        }, CancellationToken.None);

        var item = await Db.OrderItems.SingleAsync(x => x.OrderId == order.OrderId);
        var ticket = await Db.KitchenOrders.SingleAsync(x => x.OrderId == order.OrderId);
        var kitchenItem = await Db.KitchenOrderItems.SingleAsync(x => x.OrderItemId == item.Id);

        return new CreatedOrder(scenario, order, item, ticket, kitchenItem);
    }

    /// <summary>Đưa món + phiếu + đơn sang "đang nấu" (bếp đã bấm Preparing).</summary>
    private async Task MarkPreparingAsync(CreatedOrder created)
    {
        created.Item.Status = KitchenStatuses.Preparing;
        created.KitchenItem.Status = KitchenStatuses.Preparing;
        created.Ticket.Status = KitchenStatuses.Preparing;
        created.Order.Status = OrderStatuses.Preparing;
        await Db.SaveChangesAsync();
    }

    // ==================== Mốc Preparing: không hoàn kho ====================

    [Fact]
    public async Task KitchenCancelsPreparingItem_RecordsWastage_AndDoesNotReturnStock()
    {
        var created = await CreateOrderAsync(quantity: 2);
        await MarkPreparingAsync(created);

        CurrentUser.RoleList.Add(AuthPolicies.Roles.Chef);

        Assert.True(await Kitchen.UpdateItemStatusAsync(
            created.KitchenItem.Id,
            new KitchenItemStatusRequest
            {
                Status = KitchenStatuses.Cancelled,
                ReasonCode = "KitchenError",
                Reason = "Nguyên liệu hư"
            },
            CancellationToken.None));

        using var check = NewContext();

        // Tồn kho giữ nguyên 96 (100 - 2 phần * 2kg): đã tiêu thật ⇒ không hoàn.
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);

        var wastage = check.InventoryTransactions
            .Single(x => x.Type == "Wastage" && x.OrderItemId == created.Item.Id);
        Assert.Equal(4m, wastage.Quanlity);
        Assert.Equal(96m, wastage.QuanlityBefore);
        Assert.Equal(96m, wastage.QuanlityAfter);
        Assert.Contains("KitchenError", wastage.Note);

        // Món bị huỷ và có dấu vết lý do.
        Assert.Equal(KitchenStatuses.Cancelled, check.OrderItems
            .Single(x => x.Id == created.Item.Id).Status);
        Assert.Equal("Nguyên liệu hư", check.OrderItems
            .Single(x => x.Id == created.Item.Id).CancelReason);
    }

    [Fact]
    public async Task PosCancelsPreparingItem_AsAdmin_RecordsWastage_AndDoesNotReturnStock()
    {
        var created = await CreateOrderAsync(quantity: 2);
        await MarkPreparingAsync(created);

        CurrentUser.RoleList.Add(AuthPolicies.Roles.Admin);

        Assert.True(await Orders.CancelItemAsync(
            created.Order.OrderId,
            created.Item.Id,
            new CancelOrderItemRequest
            {
                Reason = "Khách đổi món sau khi bếp đã nấu",
                ReasonCode = "CustomerChange",
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();

        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);

        var wastage = check.InventoryTransactions
            .Single(x => x.Type == "Wastage" && x.OrderItemId == created.Item.Id);
        Assert.Equal(4m, wastage.Quanlity);
        Assert.Contains("KitchenError", wastage.Note);
    }

    [Fact]
    public async Task PendingItemCancelledByCustomer_StillReturnsStock()
    {
        var created = await CreateOrderAsync(quantity: 2);

        Assert.True(await Orders.CancelItemAsync(
            created.Order.OrderId,
            created.Item.Id,
            new CancelOrderItemRequest
            {
                Reason = "Khách đổi món",
                ReasonCode = "CustomerChange",
                ChangedBy = created.Scenario.Staff.Id
            },
            CancellationToken.None));

        using var check = NewContext();

        // Món chưa bếp bấm Preparing ⇒ còn hoàn kho được: 96 + 4 = 100.
        Assert.Equal(100m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.DoesNotContain(check.InventoryTransactions, x => x.Type == "Wastage");
    }

    /// <summary>Bếp huỷ món CHƯA bấm Preparing, khách đổi ⇒ vẫn hoàn kho (món 2).</summary>
    [Fact]
    public async Task KitchenCancelsPendingItem_ForCustomerChange_StillReturnsStock()
    {
        var created = await CreateOrderAsync(quantity: 2);
        CurrentUser.RoleList.Add(AuthPolicies.Roles.Chef);

        Assert.True(await Kitchen.UpdateItemStatusAsync(
            created.KitchenItem.Id,
            new KitchenItemStatusRequest
            {
                Status = KitchenStatuses.Cancelled,
                ReasonCode = "CustomerChange",
                Reason = "Khách đổi món"
            },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(100m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);
        Assert.DoesNotContain(check.InventoryTransactions, x => x.Type == "Wastage");
    }

    /// <summary>
    /// Bếp huỷ món CHƯA bấm Preparing vì nguyên liệu hư/hết ⇒ không hoàn kho (tránh tồn ảo),
    /// ghi hao hụt.
    /// </summary>
    [Fact]
    public async Task KitchenCancelsPendingItem_ForSpoiledIngredient_DoesNotReturnStock_RecordsWastage()
    {
        var created = await CreateOrderAsync(quantity: 2);
        CurrentUser.RoleList.Add(AuthPolicies.Roles.Chef);

        Assert.True(await Kitchen.UpdateItemStatusAsync(
            created.KitchenItem.Id,
            new KitchenItemStatusRequest
            {
                Status = KitchenStatuses.Cancelled,
                ReasonCode = "PhysicalShortage",
                Reason = "Nguyên liệu hư"
            },
            CancellationToken.None));

        using var check = NewContext();

        // Không hoàn kho: tồn giữ nguyên 96 (nguyên liệu thực tế đã hỏng).
        Assert.Equal(96m, check.Inventories
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId).Quanlity);

        var wastage = check.InventoryTransactions
            .Single(x => x.Type == "Wastage" && x.OrderItemId == created.Item.Id);
        Assert.Equal(4m, wastage.Quanlity);
        Assert.Contains("PhysicalShortage", wastage.Note);
    }

    // ==================== Trạng thái nguyên liệu ====================

    [Fact]
    public async Task ReportShortage_WithIngredient_SetsStockStatus_Spoiled_AndDeactivatesDish()
    {
        var created = await CreateOrderAsync(quantity: 2);
        CurrentUser.RoleList.Add(AuthPolicies.Roles.Chef);

        Assert.True(await Kitchen.ReportShortageAsync(
            created.KitchenItem.Id,
            new ReportShortageRequest
            {
                PreparedQuantity = 1,
                ReasonCode = "PhysicalShortage",
                Reason = "Lô gà bị hư",
                IngredientId = created.Scenario.Ingredient.IngredientId,
                DeactivateDish = true,
                InactiveReason = "Lô gà hư — ngừng bán gà rán"
            },
            CancellationToken.None));

        using var check = NewContext();

        // Nguyên liệu GIỮ NGUYÊN, chỉ đổi trạng thái để kho quản lý + làm minh chứng.
        var ingredient = check.Ingredients
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId);
        Assert.True(ingredient.IsActive);
        Assert.Equal(IngredientStockStatuses.Spoiled, ingredient.StockStatus);

        // Món bị vô hiệu hoá kèm lý do (món biến mất khỏi menu POS).
        var food = check.Foods.Single(x => x.FoodId == created.Scenario.Food.FoodId);
        Assert.False(food.IsActive);
        Assert.False(food.IsAvailable);
        Assert.Equal("Lô gà hư — ngừng bán gà rán", food.InactiveReason);
        Assert.NotNull(food.InactiveAt);
    }

    [Fact]
    public async Task ReportShortage_WithoutDeactivateDish_KeepsDishActive()
    {
        var created = await CreateOrderAsync(quantity: 2);
        CurrentUser.RoleList.Add(AuthPolicies.Roles.Chef);

        Assert.True(await Kitchen.ReportShortageAsync(
            created.KitchenItem.Id,
            new ReportShortageRequest
            {
                PreparedQuantity = 1,
                ReasonCode = "OutOfStock",
                Reason = "Hết gà",
                IngredientId = created.Scenario.Ingredient.IngredientId
            },
            CancellationToken.None));

        using var check = NewContext();

        var ingredient = check.Ingredients
            .Single(x => x.IngredientId == created.Scenario.Ingredient.IngredientId);
        Assert.Equal(IngredientStockStatuses.OutOfStock, ingredient.StockStatus);

        var food = check.Foods.Single(x => x.FoodId == created.Scenario.Food.FoodId);
        Assert.True(food.IsActive);
    }

    [Fact]
    public async Task SetStockStatus_ChangesStatus_AndRejectsUnknownValue()
    {
        var scenario = SeedOrderScenario();

        Assert.True(await Ingredients.SetStockStatusAsync(
            scenario.Ingredient.IngredientId, IngredientStockStatuses.OutOfStock, CancellationToken.None));

        using (var check = NewContext())
        {
            Assert.Equal(IngredientStockStatuses.OutOfStock, check.Ingredients
                .Single(x => x.IngredientId == scenario.Ingredient.IngredientId).StockStatus);
        }

        await Assert.ThrowsAsync<ArgumentException>(() =>
            Ingredients.SetStockStatusAsync(
                scenario.Ingredient.IngredientId, "KhongHopLe", CancellationToken.None));
    }

    // ==================== Màn hình bếp (poll) ====================

    [Fact]
    public async Task GetOrders_OpenFilter_ReturnsOnlyOpenTickets_WithTableWaiterAndItemCount()
    {
        var created = await CreateOrderAsync(quantity: 2);

        var closed = new KitchenOrder
        {
            OrderId = created.Order.OrderId,
            Status = KitchenStatuses.Completed,
            SentAt = DateTime.UtcNow,
            Priority = 0
        };
        // Phiếu thứ hai của cùng đơn để kiểm tra bộ lọc.
        Db.KitchenOrders.Add(closed);
        await Db.SaveChangesAsync();

        var open = await Kitchen.GetOrdersAsync(CancellationToken.None, "open");

        Assert.DoesNotContain(open, x => x.KitchenOrderId == closed.KitchenOrderId);
        var ticket = open.Single(x => x.KitchenOrderId == created.Ticket.KitchenOrderId);
        Assert.Equal(created.Scenario.Table.TableNumber, ticket.TableNumber);
        Assert.Equal(created.Scenario.Staff.UserFullName, ticket.WaiterName);
        Assert.Equal(1, ticket.ItemCount);
    }
}
