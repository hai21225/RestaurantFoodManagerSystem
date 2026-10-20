using System.Data;
using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Chặng 3 — defect #4/#5 (tuần tự hoá điều chỉnh kho/nhận phiếu) và #6/#11 (bỏ N+1).
/// Các test này pin thứ tự + isolation level của transaction và số truy vấn, thay vì
/// chạy hai request song song trên SQLite in-memory (một connection dùng chung).
/// </summary>
public sealed class ConcurrencyAndQueryGuardTests : TestBase
{
    [Fact]
    public async Task InventoryAdjust_StartsSerializableTransaction_BeforeReadingInventory()
    {
        var ingredient = SeedIngredient(10m);
        var staff = SeedUser();
        var state = new WatchState();

        using var db = NewContext(new TransactionWatchInterceptor(state), new CommandWatchInterceptor(state));
        var service = new InventoryService(db, CurrentUser, NullLogger<InventoryService>.Instance);

        Assert.True(await service.AdjustAsync(ingredient.IngredientId, new InventoryAdjustmentRequest
        {
            Quanlity = 5m,
            CreatedBy = staff.Id
        }, CancellationToken.None));

        Assert.Equal(IsolationLevel.Serializable, state.IsolationLevel);
        Assert.True(state.TransactionStartedAt >= 0);
        Assert.True(
            state.InventoryReadAt > state.TransactionStartedAt,
            string.Join("\n---\n", state.Commands));

        using var check = NewContext();
        Assert.Equal(15m, check.Inventories.Single(x => x.IngredientId == ingredient.IngredientId).Quanlity);
    }

    [Fact]
    public async Task ReceivePurchase_StartsSerializableTransaction_BeforeReadingPurchaseOrder()
    {
        var (purchase, _, staff) = await SeedDraftPurchaseAsync(quantity: 4m);
        var state = new WatchState();

        using var db = NewContext(new TransactionWatchInterceptor(state), new CommandWatchInterceptor(state));
        var service = new PurchaseOrderService(db, CurrentUser, NullLogger<PurchaseOrderService>.Instance);

        Assert.True(await service.ReceiveAsync(
            purchase.Id,
            new ReceivePurchaseRequest { ReceivedBy = staff.Id },
            CancellationToken.None));

        Assert.Equal(IsolationLevel.Serializable, state.IsolationLevel);
        Assert.True(state.PurchaseReadAt > state.TransactionStartedAt, string.Join("\n---\n", state.Commands));
        Assert.True(state.InventoryReadAt > state.TransactionStartedAt, string.Join("\n---\n", state.Commands));

        using var check = NewContext();
        Assert.Equal("Received", check.PurchaseOrders.Single(x => x.Id == purchase.Id).Status);
    }

    [Fact]
    public async Task CancelPurchase_StartsSerializableTransaction_BeforeReadingPurchaseOrder()
    {
        var (purchase, _, _) = await SeedDraftPurchaseAsync(quantity: 1m);
        var state = new WatchState();

        using var db = NewContext(new TransactionWatchInterceptor(state), new CommandWatchInterceptor(state));
        var service = new PurchaseOrderService(db, CurrentUser, NullLogger<PurchaseOrderService>.Instance);

        Assert.True(await service.CancelAsync(purchase.Id, CancellationToken.None));

        Assert.Equal(IsolationLevel.Serializable, state.IsolationLevel);
        Assert.True(state.PurchaseReadAt > state.TransactionStartedAt, string.Join("\n---\n", state.Commands));

        using var check = NewContext();
        Assert.Equal("Cancelled", check.PurchaseOrders.Single(x => x.Id == purchase.Id).Status);
    }

    [Fact]
    public async Task Menu_LoadsToppingsInOneQuery_AndReturnsOnlyLinkedToppings()
    {
        var category = SeedCategory();
        var food1 = SeedFood(category.CategoryId);
        var food2 = SeedFood(category.CategoryId);
        SeedSize(food1.FoodId, 50m, name: "M");
        SeedSize(food2.FoodId, 60m, name: "L");
        var topping1 = SeedTopping();
        var topping2 = SeedTopping();
        Db.FoodToppings.Add(new FoodTopping { FoodId = food1.FoodId, ToppingId = topping1.ToppingId });
        Db.FoodToppings.Add(new FoodTopping { FoodId = food2.FoodId, ToppingId = topping2.ToppingId });
        Db.SaveChanges();

        var state = new WatchState();
        using var db = NewContext(new CommandWatchInterceptor(state));
        var menu = await new MenuService(db).GetMenuAsync(CancellationToken.None);

        // Serialize SAU khi service trả về: nếu còn truy vấn lazy trong projection thì
        // command count sẽ tăng và/hoặc DbContext đã dispose sẽ ném lỗi.
        var json = JsonSerializer.Serialize(menu);

        Assert.Equal(5, state.Commands.Count);

        using var doc = JsonDocument.Parse(json);
        var categoryJson = doc.RootElement.EnumerateArray()
            .Single(x => x.GetProperty("CategoryId").GetInt32() == category.CategoryId);
        var food1Json = categoryJson.GetProperty("Foods").EnumerateArray()
            .Single(x => x.GetProperty("FoodId").GetInt32() == food1.FoodId);
        var toppingIds = food1Json.GetProperty("Toppings").EnumerateArray()
            .Select(x => x.GetProperty("ToppingId").GetInt32())
            .ToList();

        Assert.Equal(new[] { topping1.ToppingId }, toppingIds);
    }

    [Fact]
    public async Task RestaurantTables_GetAll_UsesTwoQueries_NoNPlusOne()
    {
        var staff = SeedUser();
        var free = SeedTable();
        var busy1 = SeedTable();
        var busy2 = SeedTable();
        SeedOrder(busy1.TableId, staff.Id, status: OrderStatuses.Pending);
        SeedOrder(busy2.TableId, staff.Id, status: OrderStatuses.Preparing);
        SeedOrder(free.TableId, staff.Id, status: OrderStatuses.Paid);

        var state = new WatchState();
        using var db = NewContext(new CommandWatchInterceptor(state));
        var all = await new RestaurantTableService(db).GetAllRestaurantTablesAsync(CancellationToken.None);

        Assert.Equal(2, state.Commands.Count);
        Assert.Equal("Available", all.Single(x => x.TableId == free.TableId).Status);
        Assert.Equal("Occupied", all.Single(x => x.TableId == busy1.TableId).Status);
        Assert.Equal("Occupied", all.Single(x => x.TableId == busy2.TableId).Status);

        using var check = NewContext();
        Assert.Equal("Available", check.RestaurantTables.Single(x => x.TableId == busy1.TableId).Status);
    }

    private async Task<(PurchaseOrder Purchase, Ingredient Ingredient, Users Staff)> SeedDraftPurchaseAsync(decimal quantity)
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest
        {
            Name = "Chặng 3",
            Phone = "1",
            Email = "chang3@test.local",
            Address = "A"
        }, CancellationToken.None);

        var ingredient = SeedIngredient(0m);
        var staff = SeedUser();
        var purchase = await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items = { new PurchaseOrderItemRequest { IngredientId = ingredient.IngredientId, Quanlity = quantity, UnitPrice = 1m } }
        }, CancellationToken.None);

        return (purchase, ingredient, staff);
    }

    private sealed class WatchState
    {
        public int Sequence;
        public int TransactionStartedAt = -1;
        public int InventoryReadAt = -1;
        public int PurchaseReadAt = -1;
        public IsolationLevel IsolationLevel = IsolationLevel.Unspecified;
        public List<string> Commands { get; } = [];

        public void MarkTransaction(DbTransaction transaction)
        {
            IsolationLevel = transaction.IsolationLevel;
            TransactionStartedAt = Interlocked.Increment(ref Sequence);
        }

        public void MarkCommand(DbCommand command)
        {
            Commands.Add(command.CommandText);
            var text = command.CommandText;
            if (text.Contains("FROM \"inventory\"", StringComparison.OrdinalIgnoreCase))
            {
                InventoryReadAt = Interlocked.Increment(ref Sequence);
            }
            if (text.Contains("FROM \"purchase_orders\"", StringComparison.OrdinalIgnoreCase))
            {
                PurchaseReadAt = Interlocked.Increment(ref Sequence);
            }
        }
    }

    private sealed class TransactionWatchInterceptor : DbTransactionInterceptor
    {
        private readonly WatchState _state;

        public TransactionWatchInterceptor(WatchState state) => _state = state;

        public override DbTransaction TransactionStarted(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction transaction)
        {
            _state.MarkTransaction(transaction);
            return base.TransactionStarted(connection, eventData, transaction);
        }

        public override ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection,
            TransactionEndEventData eventData,
            DbTransaction transaction,
            CancellationToken cancellationToken = default)
        {
            _state.MarkTransaction(transaction);
            return base.TransactionStartedAsync(connection, eventData, transaction, cancellationToken);
        }
    }

    private sealed class CommandWatchInterceptor : DbCommandInterceptor
    {
        private readonly WatchState _state;

        public CommandWatchInterceptor(WatchState state) => _state = state;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _state.MarkCommand(command);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            _state.MarkCommand(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
