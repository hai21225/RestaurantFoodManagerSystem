using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// B1 — chuyển đơn sang bàn khác (khách đổi chỗ ngồi).
///
/// <para>Luật đã chốt 2026-10-21 (chủ dự án): Waiter + Admin được chuyển; bàn đích đang có
/// đơn mở thì **chặn** (ngoài bàn đó đang có người nên không thể cho vào).</para>
/// </summary>
public sealed class Phase3TableTransferTests : TestBase
{
    private async Task<Order> CreateOpenOrderAsync(int tableId, int createdBy)
    {
        var order = SeedOrder(tableId, createdBy, subtotal: 100m);
        await Db.SaveChangesAsync();
        return order;
    }

    [Fact]
    public async Task ChangeTable_MovesOrder_SwapsTableStatus_AndWritesHistory()
    {
        var s = SeedOrderScenario();
        var order = await CreateOpenOrderAsync(s.Table.TableId, s.Staff.Id);
        var target = SeedTable();

        Assert.True(await Orders.ChangeTableAsync(
            order.OrderId,
            new ChangeOrderTableRequest { TableId = target.TableId, ChangedBy = s.Staff.Id },
            CancellationToken.None));

        using var check = NewContext();

        Assert.Equal(target.TableId, check.Orders.Single(x => x.OrderId == order.OrderId).TableId);

        // Bàn cũ trống lại, bàn mới có đơn.
        Assert.Equal("Available", check.RestaurantTables.Single(x => x.TableId == s.Table.TableId).Status);
        Assert.Equal("Occupied", check.RestaurantTables.Single(x => x.TableId == target.TableId).Status);

        // Lịch sử đơn có 1 dòng ghi việc chuyển bàn.
        var note = check.OrderStatusHistories.Single(x => x.OrderId == order.OrderId);
        Assert.Contains("Moved from table", note.Note);
        Assert.Contains(target.TableId.ToString(), note.Note);
    }

    [Fact]
    public async Task ChangeTable_TargetTableHasOpenOrder_IsBlocked()
    {
        var s = SeedOrderScenario();
        var order = await CreateOpenOrderAsync(s.Table.TableId, s.Staff.Id);

        var busy = SeedTable();
        await CreateOpenOrderAsync(busy.TableId, s.Staff.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.ChangeTableAsync(
            order.OrderId,
            new ChangeOrderTableRequest { TableId = busy.TableId, ChangedBy = s.Staff.Id },
            CancellationToken.None));

        Assert.Contains("already has an open order", ex.Message);

        using var check = NewContext();
        Assert.Equal(s.Table.TableId, check.Orders.Single(x => x.OrderId == order.OrderId).TableId);
    }

    [Fact]
    public async Task ChangeTable_TargetTableUnderMaintenance_IsBlocked()
    {
        var s = SeedOrderScenario();
        var order = await CreateOpenOrderAsync(s.Table.TableId, s.Staff.Id);

        var target = SeedTable();
        target.Status = RestaurantTableStatuses.Maintenance;
        await Db.SaveChangesAsync();

        // C-23: bàn đích bảo trì là lỗi ĐẦU VÀO ⇒ 400 (ArgumentException), không phải 409.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Orders.ChangeTableAsync(
            order.OrderId,
            new ChangeOrderTableRequest { TableId = target.TableId, ChangedBy = s.Staff.Id },
            CancellationToken.None));

        Assert.Contains("maintenance", ex.Message);
    }

    [Fact]
    public async Task ChangeTable_PaidOrder_IsBlocked()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: OrderStatuses.Paid, subtotal: 100m);
        var target = SeedTable();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Orders.ChangeTableAsync(
            order.OrderId,
            new ChangeOrderTableRequest { TableId = target.TableId, ChangedBy = s.Staff.Id },
            CancellationToken.None));

        Assert.Contains("can no longer change status", ex.Message);
    }

    [Fact]
    public async Task ChangeTable_SameTable_IsRejected()
    {
        var s = SeedOrderScenario();
        var order = await CreateOpenOrderAsync(s.Table.TableId, s.Staff.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => Orders.ChangeTableAsync(
            order.OrderId,
            new ChangeOrderTableRequest { TableId = s.Table.TableId, ChangedBy = s.Staff.Id },
            CancellationToken.None));
    }

    [Fact]
    public async Task ChangeTable_UnknownTable_IsRejected()
    {
        var s = SeedOrderScenario();
        var order = await CreateOpenOrderAsync(s.Table.TableId, s.Staff.Id);

        // C-23: bàn đích không tồn tại là lỗi ĐẦU VÀO ⇒ 400 (ArgumentException), không phải 404.
        await Assert.ThrowsAsync<ArgumentException>(() => Orders.ChangeTableAsync(
            order.OrderId,
            new ChangeOrderTableRequest { TableId = 999999, ChangedBy = s.Staff.Id },
            CancellationToken.None));
    }

    [Fact]
    public async Task ChangeTable_UnknownOrder_ReturnsFalse()
    {
        var s = SeedOrderScenario();
        var target = SeedTable();

        Assert.False(await Orders.ChangeTableAsync(
            999999,
            new ChangeOrderTableRequest { TableId = target.TableId, ChangedBy = s.Staff.Id },
            CancellationToken.None));
    }
}
