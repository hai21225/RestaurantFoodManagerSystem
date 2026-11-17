using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

public sealed class ToppingStockTests : TestBase
{
    [Fact]
    public async Task CreateAndUpdateTopping_PersistsStockQuantity()
    {
        var created = await Toppings.CreateAsync(new CreateToppingRequest
        {
            ToppingName = "Trân châu đen",
            ToppingPrice = 8m,
            IsAvailable = true,
            StockQuantity = 15
        });

        Assert.Equal(15, created.StockQuantity);

        var updated = await Toppings.UpdateAsync(created.ToppingId, new UpdateToppingRequest
        {
            ToppingName = "Trân châu đen",
            ToppingPrice = 10m,
            IsAvailable = true,
            StockQuantity = 4
        });

        Assert.NotNull(updated);
        Assert.Equal(4, updated!.StockQuantity);

        var unlimited = await Toppings.UpdateAsync(created.ToppingId, new UpdateToppingRequest
        {
            ToppingName = "Trân châu đen",
            ToppingPrice = 10m,
            IsAvailable = true,
            StockQuantity = null
        });

        Assert.NotNull(unlimited);
        Assert.Null(unlimited!.StockQuantity);
    }

    [Fact]
    public async Task CreateOrder_DeductsToppingStockQuantity()
    {
        var s = SeedOrderScenario();
        s.Topping.StockQuantity = 5;
        Db.SaveChanges();

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.User.Id,
            Items =
            [
                new CreateOrderItemRequest
                {
                    SizeId = s.Size.SizeId,
                    Quanlity = 2,
                    Toppings = [new CreateOrderToppingRequest { ToppingId = s.Topping.ToppingId, Quanlity = 1 }]
                }
            ]
        }, CancellationToken.None);

        await using var verifyDb = NewContext();
        var savedTopping = await verifyDb.Toppings.FindAsync(s.Topping.ToppingId);
        Assert.NotNull(savedTopping);
        Assert.Equal(3, savedTopping!.StockQuantity);
        Assert.True(order.OrderId > 0);
    }

    [Fact]
    public async Task CreateOrder_ExceedingToppingStock_ThrowsAndRollsBack()
    {
        var s = SeedOrderScenario();
        s.Topping.StockQuantity = 2;
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Orders.CreateAsync(new CreateOrderRequest
            {
                TableId = s.Table.TableId,
                CreatedBy = s.User.Id,
                Items =
                [
                    new CreateOrderItemRequest
                    {
                        SizeId = s.Size.SizeId,
                        Quanlity = 3,
                        Toppings = [new CreateOrderToppingRequest { ToppingId = s.Topping.ToppingId, Quanlity = 1 }]
                    }
                ]
            }, CancellationToken.None));

        Assert.Contains("chỉ còn 2 phần", ex.Message);

        await using var verifyDb = NewContext();
        var savedTopping = await verifyDb.Toppings.FindAsync(s.Topping.ToppingId);
        Assert.Equal(2, savedTopping!.StockQuantity);
    }

    [Fact]
    public async Task CancelPendingOrderItem_RestoresToppingStockQuantity()
    {
        var s = SeedOrderScenario();
        s.Topping.StockQuantity = 5;
        Db.SaveChanges();

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.User.Id,
            Items =
            [
                new CreateOrderItemRequest
                {
                    SizeId = s.Size.SizeId,
                    Quanlity = 2,
                    Toppings = [new CreateOrderToppingRequest { ToppingId = s.Topping.ToppingId, Quanlity = 1 }]
                }
            ]
        }, CancellationToken.None);

        var orderItem = await Db.OrderItems.FirstAsync(x => x.OrderId == order.OrderId);
        var cancelled = await Orders.CancelItemAsync(order.OrderId, orderItem.Id, new CancelOrderItemRequest
        {
            Reason = "Khách đổi ý",
            ReasonCode = OrderItemMutations.CustomerChange,
            ChangedBy = s.User.Id
        }, CancellationToken.None);

        Assert.True(cancelled);

        await using var verifyDb = NewContext();
        var savedTopping = await verifyDb.Toppings.FindAsync(s.Topping.ToppingId);
        Assert.Equal(5, savedTopping!.StockQuantity);
    }

    [Fact]
    public async Task CancelPreparingOrderItem_DoesNotRestoreToppingStockQuantity()
    {
        var s = SeedOrderScenario();
        s.Topping.StockQuantity = 5;
        Db.SaveChanges();

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = s.Table.TableId,
            CreatedBy = s.User.Id,
            Items =
            [
                new CreateOrderItemRequest
                {
                    SizeId = s.Size.SizeId,
                    Quanlity = 2,
                    Toppings = [new CreateOrderToppingRequest { ToppingId = s.Topping.ToppingId, Quanlity = 1 }]
                }
            ]
        }, CancellationToken.None);

        var kitchenItem = await Db.KitchenOrderItems
            .OrderByDescending(x => x.Id)
            .FirstAsync();

        CurrentUser.UserId = s.User.Id;
        CurrentUser.RoleList.Add(AuthPolicies.Roles.Chef);

        await Kitchen.UpdateItemStatusAsync(kitchenItem.Id, new UpdateKitchenItemStatusRequest
        {
            Status = KitchenStatuses.Preparing,
            ChangedBy = s.User.Id
        }, CancellationToken.None);

        var orderItem = await Db.OrderItems.FirstAsync(x => x.OrderId == order.OrderId);
        await Orders.CancelItemAsync(order.OrderId, orderItem.Id, new CancelOrderItemRequest
        {
            Reason = "Bếp làm nhầm",
            ReasonCode = OrderItemMutations.KitchenError,
            ChangedBy = s.User.Id
        }, CancellationToken.None);

        await using var verifyDb = NewContext();
        var savedTopping = await verifyDb.Toppings.FindAsync(s.Topping.ToppingId);
        Assert.Equal(3, savedTopping!.StockQuantity);
    }
}
