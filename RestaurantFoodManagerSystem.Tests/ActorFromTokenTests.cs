using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Auth;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Phase 6c (step 1): actor (CreatedBy/ChangedBy/ReceivedBy) must come from the token
/// claim when a user is authenticated, and fall back to the request body otherwise.
/// </summary>
public sealed class ActorFromTokenTests : TestBase
{
    private static CreateOrderRequest OrderRequest(int tableId, int createdBy, int sizeId)
    {
        var request = new CreateOrderRequest { TableId = tableId, CreatedBy = createdBy };
        request.Items.Add(new CreateOrderItemRequest { SizeId = sizeId, Quanlity = 1 });
        return request;
    }

    [Fact]
    public async Task CreateOrder_UsesTokenActor_AndIgnoresBodyValue()
    {
        var scenario = SeedOrderScenario();
        var bodyActor = scenario.Staff;
        var tokenActor = SeedUser();
        CurrentUser.UserId = tokenActor.Id;

        var order = await Orders.CreateAsync(
            OrderRequest(scenario.Table.TableId, bodyActor.Id, scenario.Size.SizeId),
            CancellationToken.None);

        Assert.Equal(tokenActor.Id, order.CreatedBy);
        var transaction = Db.InventoryTransactions.Single();
        Assert.Equal(tokenActor.Id, transaction.CreatedBy);
    }

    [Fact]
    public async Task UpdateOrderStatus_UsesTokenActor()
    {
        var table = SeedTable();
        var bodyActor = SeedUser();
        var tokenActor = SeedUser();
        var order = SeedOrder(table.TableId, bodyActor.Id);
        CurrentUser.UserId = tokenActor.Id;

        var updated = await Orders.UpdateStatusAsync(order.OrderId, new UpdateOrderStatusRequest
        {
            Status = "Preparing",
            ChangedBy = bodyActor.Id,
            Note = "token test"
        }, CancellationToken.None);

        Assert.True(updated);
        var history = Db.OrderStatusHistories.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(tokenActor.Id, history.ChangedBy);
    }

    [Fact]
    public async Task CreatePayment_UsesTokenActor()
    {
        var table = SeedTable();
        var bodyActor = SeedUser();
        var tokenActor = SeedUser();
        var order = SeedOrder(table.TableId, bodyActor.Id, status: "SentToKitchen");
        CurrentUser.UserId = tokenActor.Id;

        var payment = await Payments.CreateAsync(new CreatePaymentRequest
        {
            OrderId = order.OrderId,
            PaymentMethod = "Cash",
            CreatedBy = bodyActor.Id
        }, CancellationToken.None);

        Assert.NotNull(payment);
        Assert.Equal(tokenActor.Id, payment!.CreatedBy);
    }

    [Fact]
    public async Task CreatePurchaseOrder_UsesTokenActor()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest { Name = "S", Phone = "1", Email = "a@b.test", Address = "A" }, CancellationToken.None);
        var ingredient = SeedIngredient(0m);
        var bodyActor = SeedUser();
        var tokenActor = SeedUser();
        CurrentUser.UserId = tokenActor.Id;

        var purchase = await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = bodyActor.Id,
            Items = { new PurchaseOrderItemRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m, UnitPrice = 1m } }
        }, CancellationToken.None);

        Assert.Equal(tokenActor.Id, purchase.CreatedBy);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_UsesTokenActor()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest { Name = "S", Phone = "1", Email = "a@b.test", Address = "A" }, CancellationToken.None);
        var ingredient = SeedIngredient(0m);
        var bodyActor = SeedUser();
        var tokenActor = SeedUser();
        var purchase = await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = bodyActor.Id,
            Items = { new PurchaseOrderItemRequest { IngredientId = ingredient.IngredientId, Quanlity = 2m, UnitPrice = 1m } }
        }, CancellationToken.None);

        CurrentUser.UserId = tokenActor.Id;
        Assert.True(await Purchases.ReceiveAsync(purchase.Id, new ReceivePurchaseRequest { ReceivedBy = bodyActor.Id }, CancellationToken.None));

        var transaction = Db.InventoryTransactions.Single(x => x.ReferenceId == purchase.Id);
        Assert.Equal(tokenActor.Id, transaction.CreatedBy);
    }

    [Fact]
    public async Task AdjustInventory_UsesTokenActor()
    {
        var ingredient = SeedIngredient(10m);
        var bodyActor = SeedUser();
        var tokenActor = SeedUser();
        CurrentUser.UserId = tokenActor.Id;

        Assert.True(await InventorySvc.AdjustAsync(ingredient.IngredientId, new InventoryAdjustmentRequest
        {
            Quanlity = 5m,
            CreatedBy = bodyActor.Id,
            Note = "token test"
        }, CancellationToken.None));

        var transaction = Db.InventoryTransactions.Single();
        Assert.Equal(tokenActor.Id, transaction.CreatedBy);
    }

    [Fact]
    public async Task FallsBackToBodyActor_WhenNoToken()
    {
        var ingredient = SeedIngredient(10m);
        var bodyActor = SeedUser();
        CurrentUser.UserId = null;

        Assert.True(await InventorySvc.AdjustAsync(ingredient.IngredientId, new InventoryAdjustmentRequest
        {
            Quanlity = 5m,
            CreatedBy = bodyActor.Id
        }, CancellationToken.None));

        Assert.Equal(bodyActor.Id, Db.InventoryTransactions.Single().CreatedBy);
    }

    // ==========================================================
    // HttpContextCurrentUser : đọc claim "sub" / NameIdentifier
    // ==========================================================

    private static ICurrentUser CurrentUserFromClaims(params Claim[] claims)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"))
        };
        return new HttpContextCurrentUser(new HttpContextAccessor { HttpContext = context });
    }

    [Fact]
    public void HttpContextCurrentUser_ReadsSubClaim()
    {
        Assert.Equal(7, CurrentUserFromClaims(new Claim("sub", "7")).UserId);
    }

    [Fact]
    public void HttpContextCurrentUser_ReadsNameIdentifierClaim()
    {
        Assert.Equal(9, CurrentUserFromClaims(new Claim(ClaimTypes.NameIdentifier, "9")).UserId);
    }

    [Fact]
    public void HttpContextCurrentUser_ReturnsNull_WhenNoUser()
    {
        var currentUser = new HttpContextCurrentUser(new HttpContextAccessor());

        Assert.Null(currentUser.UserId);
    }

    [Fact]
    public void HttpContextCurrentUser_ReturnsNull_WhenClaimIsNotNumeric()
    {
        Assert.Null(CurrentUserFromClaims(new Claim("sub", "not-a-number")).UserId);
    }
}
