using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Nhóm B (phần đổi schema) — B3 unique <c>Payment.OrderId</c>, B5 một recipe active/size,
/// B6 unique index có lọc soft-delete, B7 concurrency token.
/// </summary>
public sealed class NhomBDataIntegrityTests : TestBase
{
    // ============================ B3 — PAYMENT ============================

    [Fact]
    public async Task Payment_UniquePaidIndex_BlocksSecondPaidRowForSameOrder()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 100m);

        Db.Payments.Add(NewPaidPayment(order.OrderId, 100m, s.Staff.Id, "TXN-A"));
        Db.SaveChanges();

        Db.Payments.Add(NewPaidPayment(order.OrderId, 100m, s.Staff.Id, "TXN-B"));
        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Payment_UniquePaidIndex_AllowsNonPaidRowsForSameOrder()
    {
        // Filter là [Status] = 'Paid' ⇒ payment chưa/thất bại vẫn được nhiều dòng.
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 100m);

        Db.Payments.Add(NewPaidPayment(order.OrderId, 100m, s.Staff.Id, "TXN-1", status: "Failed"));
        Db.Payments.Add(NewPaidPayment(order.OrderId, 100m, s.Staff.Id, "TXN-2", status: "Failed"));
        await Db.SaveChangesAsync();

        Assert.Equal(2, await Db.Payments.CountAsync(x => x.OrderId == order.OrderId));
    }

    [Fact]
    public async Task Payment_RejectsZeroTotalOrder()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id, subtotal: 0m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Payments.CreateAsync(
            new CreatePaymentRequest { OrderId = order.OrderId, PaymentMethod = "Cash", CreatedBy = s.Staff.Id },
            CancellationToken.None));

        Assert.Equal("Order total must be greater than zero", ex.Message);
        Assert.Empty(Db.Payments);
        Assert.Equal("Pending", Db.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    private static Payment NewPaidPayment(int orderId, decimal amount, int createdBy, string code, string status = "Paid") =>
        new()
        {
            OrderId = orderId,
            Amount = amount,
            PaymentMethod = "Cash",
            Status = status,
            TransactionCode = code,
            PaidAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

    // ============================ B5 — RECIPE ============================

    [Fact]
    public async Task Recipe_CannotCreateSecondActiveRecipeForSameSize()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 10m);
        var ingredient = SeedIngredient(10m);

        await Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "first",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "second",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None));

        Assert.Equal("This size already has an active recipe", ex.Message);
        Assert.Equal(1, await Db.Recipes.CountAsync(x => x.SizeId == size.SizeId && x.IsActive));
    }

    [Fact]
    public async Task Recipe_AllowsNewActiveRecipeAfterPreviousIsSoftDeleted()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 10m);
        var ingredient = SeedIngredient(10m);

        var first = await Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "v1",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None);

        await Recipes.DeleteAsync(first.RecipeId, CancellationToken.None);

        var second = await Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "v2",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 2m } }
        }, CancellationToken.None);

        Assert.True(second.IsActive);
        Assert.Equal(2, await Db.Recipes.CountAsync(x => x.SizeId == size.SizeId));
    }

    [Fact]
    public async Task Recipe_UpdateCannotMoveOntoSizeThatAlreadyHasActiveRecipe()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var sizeA = SeedSize(food.FoodId, 10m, name: "M");
        var sizeB = SeedSize(food.FoodId, 20m, isDefault: false, name: "L");
        var ingredient = SeedIngredient(10m);

        var recipeA = await Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = sizeA.SizeId,
            Name = "a",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None);

        await Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = sizeB.SizeId,
            Name = "b",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Recipes.UpdateAsync(recipeA.RecipeId, new RecipeRequest
        {
            SizeId = sizeB.SizeId,
            Name = "a",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None));

        Assert.Equal("This size already has an active recipe", ex.Message);
        Assert.Equal(sizeA.SizeId, Db.Recipes.Single(x => x.RecipeId == recipeA.RecipeId).SizeId);
    }

    // ============================ B6 — SOFT DELETE + UNIQUE ============================

    [Fact]
    public async Task Ingredient_CanBeRecreatedWithSameNameAfterSoftDelete()
    {
        var ingredient = await Ingredients.CreateAsync(new IngredientRequest
        {
            Name = "Sugar",
            Unit = "kg"
        }, CancellationToken.None);

        Assert.True(await Ingredients.DeleteAsync(ingredient.IngredientId, CancellationToken.None));

        var recreated = await Ingredients.CreateAsync(new IngredientRequest
        {
            Name = "Sugar",
            Unit = "kg"
        }, CancellationToken.None);

        Assert.NotEqual(ingredient.IngredientId, recreated.IngredientId);
        Assert.True(recreated.IsActive);
    }

    [Fact]
    public async Task Category_CanBeRecreatedWithSameNameAfterSoftDelete()
    {
        Assert.True(await Categories.CreateCategoryAsync(new CategoryDTO { Name = "Drinks" }));
        var category = Db.Categories.Single(x => x.CategoryName == "Drinks");

        Assert.True(await Categories.DeleteCategoryAsync(category.CategoryId));

        Assert.True(await Categories.CreateCategoryAsync(new CategoryDTO { Name = "Drinks" }));
        Assert.Equal(2, await Db.Categories.CountAsync(x => x.CategoryName == "Drinks"));
        Assert.Equal(1, await Db.Categories.CountAsync(x => x.CategoryName == "Drinks" && x.IsActive));
    }

    [Fact]
    public async Task FoodSize_CanBeRecreatedWithSameNameAfterSoftDelete()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);

        var size = await FoodSizes.CreateAsync(food.FoodId, new CreateFoodSizeRequest
        {
            SizeName = "XL",
            SizePrice = 10m,
            IsDefault = true
        });

        Db.ChangeTracker.Clear();
        Assert.True(await FoodSizes.DeleteAsync(food.FoodId, size.SizeId));

        Db.ChangeTracker.Clear();

        var recreated = await FoodSizes.CreateAsync(food.FoodId, new CreateFoodSizeRequest
        {
            SizeName = "XL",
            SizePrice = 12m,
            IsDefault = true
        });

        Assert.NotEqual(size.SizeId, recreated.SizeId);
        Assert.True(recreated.IsActive);
    }

    [Fact]
    public async Task TableNumber_CanBeReusedAfterSoftDelete()
    {
        var table = SeedTable(number: "T-99");

        Assert.True(await Tables.DeleteRestaurantTableAsync(table.TableId));

        var recreated = await Tables.AddRestaurantTableAsync(new RestaurantTableDto
        {
            TableNumber = "T-99",
            Capacity = 4
        });

        Assert.NotEqual(table.TableId, recreated.TableId);
        Assert.True(recreated.IsActive);
    }

    // ============================ B7 — CONCURRENCY ============================

    [Fact]
    public async Task Inventory_StaleWriteIsRejected()
    {
        var ingredient = SeedIngredient(10m);
        var inventoryId = Db.Inventories.Single(x => x.IngredientId == ingredient.IngredientId).InventoryId;

        using var first = NewContext();
        using var second = NewContext();

        var firstRow = await first.Inventories.SingleAsync(x => x.InventoryId == inventoryId);
        var secondRow = await second.Inventories.SingleAsync(x => x.InventoryId == inventoryId);

        firstRow.Quanlity = 9m;
        firstRow.UpdatedAt = DateTime.UtcNow;
        await first.SaveChangesAsync();

        secondRow.Quanlity = 8m;
        secondRow.UpdatedAt = DateTime.UtcNow;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => second.SaveChangesAsync());

        using var check = NewContext();
        Assert.Equal(9m, check.Inventories.Single(x => x.InventoryId == inventoryId).Quanlity);
    }

    [Fact]
    public async Task RestaurantTable_StaleWriteIsRejected()
    {
        var table = SeedTable();

        using var first = NewContext();
        using var second = NewContext();

        var firstRow = await first.RestaurantTables.SingleAsync(x => x.TableId == table.TableId);
        var secondRow = await second.RestaurantTables.SingleAsync(x => x.TableId == table.TableId);

        firstRow.Capacity = 6;
        await first.SaveChangesAsync();

        secondRow.Capacity = 8;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => second.SaveChangesAsync());

        using var check = NewContext();
        Assert.Equal(6, check.RestaurantTables.Single(x => x.TableId == table.TableId).Capacity);
    }

    [Fact]
    public async Task Order_StaleStatusWriteIsRejected()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        using var first = NewContext();
        using var second = NewContext();

        var firstRow = await first.Orders.SingleAsync(x => x.OrderId == order.OrderId);
        var secondRow = await second.Orders.SingleAsync(x => x.OrderId == order.OrderId);

        firstRow.Status = OrderStatuses.Preparing;
        await first.SaveChangesAsync();

        secondRow.Status = OrderStatuses.Cancelled;
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => second.SaveChangesAsync());

        using var check = NewContext();
        Assert.Equal(OrderStatuses.Preparing, check.Orders.Single(x => x.OrderId == order.OrderId).Status);
    }

    [Fact]
    public async Task RowVersion_ChangesAfterEachUpdate()
    {
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);

        var before = Db.Orders.AsNoTracking().Single(x => x.OrderId == order.OrderId).RowVersion;
        Assert.True(await Orders.UpdateStatusAsync(order.OrderId, new UpdateOrderStatusRequest { Status = OrderStatuses.Preparing, ChangedBy = s.Staff.Id }, CancellationToken.None));
        Db.ChangeTracker.Clear();
        var after = Db.Orders.AsNoTracking().Single(x => x.OrderId == order.OrderId).RowVersion;

        Assert.False(before.SequenceEqual(after));
    }
}
