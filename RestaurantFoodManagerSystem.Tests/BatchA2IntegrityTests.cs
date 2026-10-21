using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Batch A2 — backend integrity (mục 15.1–15.7 của audit):
/// transaction khi tạo đơn/huỷ đơn, trạng thái cuối của bếp, hoàn kho, promotion targeting
/// và validate dữ liệu đầu vào. Các test pin thứ tự/isolation của transaction thay vì chạy
/// hai request song song thật trên SQLite in-memory (một connection dùng chung).
/// </summary>
public sealed class BatchA2IntegrityTests : TestBase
{
    private static CreateOrderRequest CreateRequest(int tableId, int createdBy, params (int SizeId, int Quantity)[] items)
    {
        var request = new CreateOrderRequest { TableId = tableId, CreatedBy = createdBy };
        foreach (var (sizeId, quantity) in items)
        {
            request.Items.Add(new CreateOrderItemRequest { SizeId = sizeId, Quanlity = quantity });
        }
        return request;
    }

    private static Promotion NewPromotion(string discountType = "Percent", decimal value = 10m) => new()
    {
        Name = $"promo-{Guid.NewGuid():N}",
        Description = "test",
        DiscountType = discountType,
        DiscountValue = value,
        MinimumOrderAmount = 0m,
        MaxDiscountAmount = 0m,
        StartAt = DateTime.UtcNow.AddDays(-1),
        EndAt = DateTime.UtcNow.AddDays(1),
        IsActive = true
    };

    // ============================ 15.1 TẠO ĐƠN ============================

    [Fact]
    public async Task CreateOrder_ChecksOpenOrder_InsideSerializableTransaction()
    {
        var s = SeedOrderScenario(stock: 100m);
        var state = new WatchState();

        using var db = NewContext(new TransactionWatchInterceptor(state), new CommandWatchInterceptor(state));
        var service = new OrderService(
            db,
            new RestaurantTableService(db),
            CurrentUser,
            NullLogger<OrderService>.Instance,
            OrderIdempotency,
            new OrderLifecycleCoordinator(db),
            new RestaurantSettingsService(db));

        await service.CreateAsync(
            CreateRequest(s.Table.TableId, s.Staff.Id, (s.Size.SizeId, 1)),
            CancellationToken.None);

        Assert.Equal(IsolationLevel.Serializable, state.IsolationLevel);
        Assert.True(state.TransactionStartedAt >= 0);
        Assert.True(
            state.OrdersReadAt > state.TransactionStartedAt,
            string.Join("\n---\n", state.Commands));
    }

    // ============================ 15.2 BẾP ============================

    [Fact]
    public async Task KitchenOrder_CannotLeaveTerminalState()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Cancelled");
        var kitchen = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = "Cancelled",
            SentAt = DateTime.UtcNow
        };
        Db.KitchenOrders.Add(kitchen);
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Kitchen.UpdateOrderStatusAsync(
            kitchen.KitchenOrderId,
            new KitchenStatusRequest { Status = "Pending" },
            CancellationToken.None));

        Assert.Contains("can no longer change status", ex.Message);
        Assert.Equal("Cancelled", Db.KitchenOrders.Single().Status);
        Assert.Empty(Db.OrderStatusHistories);
    }

    [Fact]
    public async Task KitchenOrder_Completed_CannotBeReopened()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Ready");
        var kitchen = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = "Completed",
            SentAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        Db.KitchenOrders.Add(kitchen);
        Db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Kitchen.UpdateOrderStatusAsync(
            kitchen.KitchenOrderId,
            new KitchenStatusRequest { Status = "Preparing" },
            CancellationToken.None));
    }

    [Fact]
    public async Task KitchenItem_CannotChange_WhenParentTicketIsTerminal()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Ready");
        var orderItem = new OrderItem
        {
            OrderId = order.OrderId,
            SizeId = s.Size.SizeId,
            Quanlity = 1,
            UnitPrice = 50m,
            Status = "Ready",
            CreatedAt = DateTime.UtcNow
        };
        var kitchen = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = "Completed",
            SentAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow
        };
        Db.OrderItems.Add(orderItem);
        Db.KitchenOrders.Add(kitchen);
        Db.SaveChanges();

        var kitchenItem = new KitchenOrderItem
        {
            KitchenOrderId = kitchen.KitchenOrderId,
            OrderItemId = orderItem.Id,
            Quanlity = 1,
            Status = "Ready"
        };
        Db.KitchenOrderItems.Add(kitchenItem);
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Kitchen.UpdateItemStatusAsync(
            kitchenItem.Id,
            new KitchenItemStatusRequest { Status = "Preparing" },
            CancellationToken.None));

        Assert.Contains("can no longer change status", ex.Message);
        Assert.Equal("Ready", Db.KitchenOrderItems.Single().Status);
    }

    [Fact]
    public async Task KitchenStatusChange_WritesOrderStatusHistory()
    {
        var s = SeedOrderScenario();
        CurrentUser.UserId = s.Staff.Id;
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "SentToKitchen");
        var kitchen = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = "Pending",
            SentAt = DateTime.UtcNow
        };
        Db.KitchenOrders.Add(kitchen);
        Db.SaveChanges();

        Assert.True(await Kitchen.UpdateOrderStatusAsync(
            kitchen.KitchenOrderId,
            new KitchenStatusRequest { Status = "Preparing" },
            CancellationToken.None));

        var history = Db.OrderStatusHistories.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("SentToKitchen", history.OldStatus);
        Assert.Equal("Preparing", history.NewStatus);
        Assert.Equal(s.Staff.Id, history.ChangedBy);
        Assert.Equal("Updated from kitchen", history.Note);
    }

    [Fact]
    public async Task KitchenStatusChange_OnPaidOrder_DoesNotReopenOrder()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Paid");
        var kitchen = new KitchenOrder
        {
            OrderId = order.OrderId,
            Status = "Pending",
            SentAt = DateTime.UtcNow
        };
        Db.KitchenOrders.Add(kitchen);
        Db.SaveChanges();

        Assert.True(await Kitchen.UpdateOrderStatusAsync(
            kitchen.KitchenOrderId,
            new KitchenStatusRequest { Status = "Preparing" },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal("Paid", check.Orders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.Equal("Preparing", check.KitchenOrders.Single().Status);
        Assert.Empty(check.OrderStatusHistories);
    }

    // ============================ 15.3 HUỶ ĐƠN + HOÀN KHO ============================

    [Fact]
    public async Task CancelOrder_SyncsKitchen_ReturnsStock_AndFreesTable()
    {
        var s = SeedOrderScenario(sizePrice: 50m, toppingPrice: 5m, stock: 100m);
        var order = await Orders.CreateAsync(
            CreateRequest(s.Table.TableId, s.Staff.Id, (s.Size.SizeId, 2)),
            CancellationToken.None);

        Assert.Equal(96m, Db.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);

        Assert.True(await Orders.UpdateStatusAsync(order.OrderId, new UpdateOrderStatusRequest
        {
            Status = "Cancelled",
            ChangedBy = s.Staff.Id,
            Note = "customer changed mind"
        }, CancellationToken.None));

        using var check = NewContext();
        var stored = check.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("Cancelled", stored.Status);

        var kitchen = check.KitchenOrders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal("Cancelled", kitchen.Status);
        Assert.All(check.KitchenOrderItems.Where(x => x.KitchenOrderId == kitchen.KitchenOrderId),
            x => Assert.Equal("Cancelled", x.Status));
        Assert.All(check.OrderItems.Where(x => x.OrderId == order.OrderId),
            x => Assert.Equal("Cancelled", x.Status));

        Assert.Equal(100m, check.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);

        var returnTransaction = check.InventoryTransactions
            .Single(x => x.ReferenceType == "Order" && x.ReferenceId == order.OrderId && x.Type == "AdjustmentIn");
        Assert.Equal(4m, returnTransaction.Quanlity);
        Assert.Equal("Returned stock when order was cancelled", returnTransaction.Note);

        Assert.Equal("Available", check.RestaurantTables.Single(x => x.TableId == s.Table.TableId).Status);
    }

    [Fact]
    public async Task CancelOrder_DoesNotInventStock_WhenNothingWasDeducted()
    {
        // Đơn cũ/legacy chưa có InventoryTransaction: không được cộng tồn kho ảo từ công thức.
        var s = SeedOrderScenario(stock: 100m);
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, status: "SentToKitchen");

        Assert.True(await Orders.UpdateStatusAsync(order.OrderId, new UpdateOrderStatusRequest
        {
            Status = "Cancelled",
            ChangedBy = s.Staff.Id,
            Note = "legacy order cancellation"
        }, CancellationToken.None));

        Assert.Equal(100m, Db.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);
        Assert.Empty(Db.InventoryTransactions);
    }

    [Fact]
    public async Task KitchenCancel_AlsoCancelsOrder_AndReturnsStock()
    {
        var s = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        CurrentUser.UserId = s.Staff.Id;
        var order = await Orders.CreateAsync(
            CreateRequest(s.Table.TableId, s.Staff.Id, (s.Size.SizeId, 1)),
            CancellationToken.None);
        var kitchen = Db.KitchenOrders.Single(x => x.OrderId == order.OrderId);

        Assert.True(await Kitchen.UpdateOrderStatusAsync(
            kitchen.KitchenOrderId,
            new KitchenStatusRequest { Status = "Cancelled" },
            CancellationToken.None));

        using var check = NewContext();
        Assert.Equal("Cancelled", check.Orders.Single(x => x.OrderId == order.OrderId).Status);
        Assert.Equal(100m, check.Inventories.Single(x => x.IngredientId == s.Ingredient.IngredientId).Quanlity);

        // Lịch sử có dòng "đơn được tạo" (P0-03) rồi mới tới dòng huỷ.
        var history = check.OrderStatusHistories
            .Where(x => x.OrderId == order.OrderId)
            .OrderBy(x => x.Id)
            .Last();
        Assert.Equal("Cancelled from kitchen", history.Note);
        Assert.Equal(s.Staff.Id, history.ChangedBy);
    }

    // ============================ 15.4 PROMOTION ============================

    [Fact]
    public async Task ApplyPromotion_RejectsPaidOrCancelledOrder()
    {
        var s = SeedOrderScenario();
        var paid = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Paid");
        var cancelled = SeedOrder(s.Table.TableId, s.Staff.Id, status: "Cancelled");

        var promotion = NewPromotion();
        Db.Promotions.Add(promotion);
        Db.SaveChanges();

        Assert.False(await Orders.ApplyPromotionAsync(paid.OrderId, promotion.Id, CancellationToken.None));
        Assert.False(await Orders.ApplyPromotionAsync(cancelled.OrderId, promotion.Id, CancellationToken.None));
        Assert.All(Db.Orders.ToList(), x => Assert.Equal(0m, x.DiscountAmount));
    }

    [Fact]
    public async Task ApplyPromotion_WithInvalidDiscountType_ReturnsFalse()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 100m);
        var promotion = NewPromotion(discountType: "Bogus", value: 10m);
        Db.Promotions.Add(promotion);
        Db.SaveChanges();

        Assert.False(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));
        Assert.Equal(0m, Db.Orders.Single(x => x.OrderId == order.OrderId).DiscountAmount);
    }

    [Fact]
    public async Task ApplyPromotion_FoodTargeting_OnlyDiscountsTargetedLines()
    {
        var s = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var otherCategory = SeedCategory();
        var otherFood = SeedFood(otherCategory.CategoryId);
        var otherSize = SeedSize(otherFood.FoodId, 200m);
        var otherIngredient = SeedIngredient(100m);
        SeedRecipe(otherSize.SizeId, (otherIngredient.IngredientId, 1m));

        var order = await Orders.CreateAsync(
            CreateRequest(s.Table.TableId, s.Staff.Id, (s.Size.SizeId, 1), (otherSize.SizeId, 1)),
            CancellationToken.None);
        Assert.Equal(250m, order.Subtotal);

        var promotion = NewPromotion(value: 50m);
        Db.Promotions.Add(promotion);
        Db.SaveChanges();
        Db.PromotionFoods.Add(new PromotionFood { PromotionId = promotion.Id, SizeId = s.Size.SizeId });
        Db.SaveChanges();

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));

        var updated = Db.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(25m, updated.DiscountAmount); // 50% của dòng 50đ, không phải của 250đ
        Assert.Equal(225m, updated.TotalAmount);
    }

    [Fact]
    public async Task ApplyPromotion_CategoryTargeting_OnlyDiscountsMatchingCategory()
    {
        var s = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var otherCategory = SeedCategory();
        var otherFood = SeedFood(otherCategory.CategoryId);
        var otherSize = SeedSize(otherFood.FoodId, 200m);
        var otherIngredient = SeedIngredient(100m);
        SeedRecipe(otherSize.SizeId, (otherIngredient.IngredientId, 1m));

        var order = await Orders.CreateAsync(
            CreateRequest(s.Table.TableId, s.Staff.Id, (s.Size.SizeId, 1), (otherSize.SizeId, 1)),
            CancellationToken.None);

        var promotion = NewPromotion(value: 10m);
        Db.Promotions.Add(promotion);
        Db.SaveChanges();
        Db.PromotionCategories.Add(new PromotionCategory
        {
            PromotionId = promotion.Id,
            CategoryId = otherCategory.CategoryId
        });
        Db.SaveChanges();

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));

        var updated = Db.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(20m, updated.DiscountAmount); // 10% của dòng 200đ
        Assert.Equal(230m, updated.TotalAmount);
    }

    // ============================ 15.5 VALIDATE DỮ LIỆU ============================

    [Fact]
    public async Task CreateRecipe_WithNullIngredients_ThrowsArgumentException()
    {
        var s = SeedOrderScenario();

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = s.Size.SizeId,
            Name = "null recipe",
            Ingredients = null!
        }, CancellationToken.None));

        Assert.Equal("Recipe must contain at least one ingredient", ex.Message);
    }

    [Fact]
    public async Task CreatePurchase_WithNullOrEmptyItems_ThrowsArgumentException()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest
        {
            Name = $"BatchA2 supplier {Guid.NewGuid():N}",
            Phone = "1",
            Email = "batch-a2@test.local",
            Address = "A"
        }, CancellationToken.None);
        var staff = SeedUser();

        await Assert.ThrowsAsync<ArgumentException>(() => Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items = null!
        }, CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(() => Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items = []
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CreatePromotion_RejectsInvalidDiscountTypeOrValue()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Promotions.CreateAsync(new PromotionRequest
        {
            Name = "bad type",
            DiscountType = "Bogus",
            DiscountValue = 10m,
            StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddDays(1)
        }, CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(() => Promotions.CreateAsync(new PromotionRequest
        {
            Name = "too much",
            DiscountType = "Percent",
            DiscountValue = 101m,
            StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddDays(1)
        }, CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(() => Promotions.CreateAsync(new PromotionRequest
        {
            Name = "zero",
            DiscountType = "Fixed",
            DiscountValue = 0m,
            StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddDays(1)
        }, CancellationToken.None));
    }

    // ============================ 15.6 SETUP FIRST-ADMIN ============================

    [Fact]
    public async Task CreateFirstAdmin_UsesSerializableTransaction()
    {
        SeedRole("Admin");
        var state = new WatchState();

        using var db = NewContext(new TransactionWatchInterceptor(state), new CommandWatchInterceptor(state));
        var service = new SetupService(
            db,
            new AuthService(
                db,
                new UserService(db),
                new UserRoleService(db),
                new RoleService(db)),
            new UserService(db));

        Assert.True(await service.CreateFirstAdminAsync(new RegisterRequest
        {
            UserName = $"first-admin-{Guid.NewGuid():N}",
            Email = $"first-admin-{Guid.NewGuid():N}@test.local",
            Password = "secret123",
            ConfirmPassword = "secret123",
            FullName = "First Admin"
        }, CancellationToken.None));

        Assert.Equal(IsolationLevel.Serializable, state.IsolationLevel);
        Assert.True(state.TransactionStartedAt >= 0);
        Assert.True(state.UsersReadAt > state.TransactionStartedAt, string.Join("\n---\n", state.Commands));
    }

    // ============================ INTERCEPTORS ============================

    private sealed class WatchState
    {
        public int Sequence;
        public int TransactionStartedAt = -1;
        public int OrdersReadAt = -1;
        public int UsersReadAt = -1;
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
            if (text.Contains("FROM \"orders\"", StringComparison.OrdinalIgnoreCase))
            {
                OrdersReadAt = Interlocked.Increment(ref Sequence);
            }
            if (text.Contains("FROM \"users\"", StringComparison.OrdinalIgnoreCase))
            {
                UsersReadAt = Interlocked.Increment(ref Sequence);
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
