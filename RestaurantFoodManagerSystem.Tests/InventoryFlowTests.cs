using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Pins the feature services (ingredients, inventory, suppliers, purchase orders, promotions, recipes)
/// plus the retained ManagementService kitchen duplicate.
/// </summary>
public sealed class InventoryFlowTests : TestBase
{
    // ============================ INGREDIENTS ============================

    [Fact]
    public async Task Ingredients_AreOrderedByName()
    {
        Db.Ingredients.Add(new Ingredient { Name = "Zeta", Unit = "kg", IsActive = true });
        Db.Ingredients.Add(new Ingredient { Name = "Alpha", Unit = "kg", IsActive = true });
        Db.SaveChanges();

        var result = await Ingredients.GetAllAsync(CancellationToken.None);

        Assert.Equal(new[] { "Alpha", "Zeta" }, result.Select(x => x.Name));
    }

    [Fact]
    public async Task CreateIngredient_AlsoCreatesZeroQuantityInventoryRow()
    {
        var ingredient = await Ingredients.CreateAsync(new IngredientRequest
        {
            Name = "  Flour  ",
            Unit = "  kg  ",
            MinimumStock = 5m
        }, CancellationToken.None);

        Assert.Equal("Flour", ingredient.Name);
        Assert.Equal("kg", ingredient.Unit);
        Assert.Equal(5m, ingredient.MinimumStock);
        Assert.True(ingredient.IsActive);

        var inventory = Assert.Single(Db.Inventories.Where(x => x.IngredientId == ingredient.IngredientId));
        Assert.Equal(0m, inventory.Quanlity);
    }

    [Fact]
    public async Task UpdateIngredient_ReturnsFalseWhenMissing_AndUpdatesFields()
    {
        var ingredient = SeedIngredient(10m);

        Assert.False(await Ingredients.UpdateAsync(999999, new IngredientRequest { Name = "x", Unit = "y" }, CancellationToken.None));

        Assert.True(await Ingredients.UpdateAsync(ingredient.IngredientId, new IngredientRequest
        {
            Name = "  Salt  ",
            Unit = "  g  ",
            MinimumStock = 2m
        }, CancellationToken.None));

        var stored = Db.Ingredients.Single(x => x.IngredientId == ingredient.IngredientId);
        Assert.Equal("Salt", stored.Name);
        Assert.Equal("g", stored.Unit);
        Assert.Equal(2m, stored.MinimumStock);
    }

    [Fact]
    public async Task DeleteIngredient_IsSoftDelete()
    {
        var ingredient = SeedIngredient(10m);

        Assert.True(await Ingredients.DeleteAsync(ingredient.IngredientId, CancellationToken.None));
        Assert.False(Db.Ingredients.Single(x => x.IngredientId == ingredient.IngredientId).IsActive);
        Assert.False(await Ingredients.DeleteAsync(999999, CancellationToken.None));
    }

    // ============================ INVENTORY ============================

    [Fact]
    public async Task Inventory_IsOrderedByIngredientId()
    {
        SeedIngredient(1m);
        SeedIngredient(2m);

        var result = await InventorySvc.GetAllAsync(CancellationToken.None);

        Assert.Equal(result.Select(x => x.IngredientId).OrderBy(x => x), result.Select(x => x.IngredientId));
    }

    [Fact]
    public async Task AdjustInventory_Positive_WritesAdjustmentInTransaction()
    {
        var ingredient = SeedIngredient(10m);
        var staff = SeedUser();

        Assert.True(await InventorySvc.AdjustAsync(ingredient.IngredientId, new InventoryAdjustmentRequest
        {
            Quanlity = 5m,
            CreatedBy = staff.Id,
            Note = "restock"
        }, CancellationToken.None));

        Assert.Equal(15m, Db.Inventories.Single(x => x.IngredientId == ingredient.IngredientId).Quanlity);

        var transaction = Assert.Single(Db.InventoryTransactions);
        Assert.Equal("AdjustmentIn", transaction.Type);
        Assert.Equal(5m, transaction.Quanlity);
        Assert.Equal(10m, transaction.QuanlityBefore);
        Assert.Equal(15m, transaction.QuanlityAfter);
        Assert.Equal("restock", transaction.Note);
        Assert.Equal(staff.Id, transaction.CreatedBy);
    }

    [Fact]
    public async Task AdjustInventory_Negative_WritesAdjustmentOutTransaction()
    {
        var ingredient = SeedIngredient(10m);
        var staff = SeedUser();

        Assert.True(await InventorySvc.AdjustAsync(ingredient.IngredientId, new InventoryAdjustmentRequest
        {
            Quanlity = -4m,
            CreatedBy = staff.Id
        }, CancellationToken.None));

        Assert.Equal(6m, Db.Inventories.Single(x => x.IngredientId == ingredient.IngredientId).Quanlity);
        var transaction = Assert.Single(Db.InventoryTransactions);
        Assert.Equal("AdjustmentOut", transaction.Type);
        Assert.Equal(4m, transaction.Quanlity);
    }

    [Fact]
    public async Task AdjustInventory_RejectsNegativeResult()
    {
        var ingredient = SeedIngredient(3m);
        var staff = SeedUser();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InventorySvc.AdjustAsync(ingredient.IngredientId, new InventoryAdjustmentRequest
        {
            Quanlity = -4m,
            CreatedBy = staff.Id
        }, CancellationToken.None));

        Assert.Equal("Inventory cannot become negative", ex.Message);
        Assert.Equal(3m, Db.Inventories.Single(x => x.IngredientId == ingredient.IngredientId).Quanlity);
        Assert.Empty(Db.InventoryTransactions);
    }

    [Fact]
    public async Task AdjustInventory_ReturnsFalse_WhenInventoryRowMissing()
    {
        Db.Ingredients.Add(new Ingredient { Name = "NoStock", Unit = "kg", IsActive = true });
        Db.SaveChanges();

        Assert.False(await InventorySvc.AdjustAsync(Db.Ingredients.Single().IngredientId, new InventoryAdjustmentRequest { Quanlity = 1m }, CancellationToken.None));
    }

    // ============================ SUPPLIERS ============================

    [Fact]
    public async Task Supplier_Crud_RoundTrip()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest
        {
            Name = "  Fresh Co  ",
            Phone = " 0900 ",
            Email = " a@b.test ",
            Address = " 1 Street "
        }, CancellationToken.None);

        Assert.Equal("Fresh Co", supplier.Name);
        Assert.True(supplier.IsActive);

        var all = await Suppliers.GetAllAsync(CancellationToken.None);
        Assert.Contains(all, x => x.SupplierId == supplier.SupplierId);

        Assert.False(await Suppliers.UpdateAsync(999999, new SupplierRequest { Name = "x", Phone = "x", Email = "a@b.test", Address = "x" }, CancellationToken.None));
        Assert.True(await Suppliers.UpdateAsync(supplier.SupplierId, new SupplierRequest
        {
            Name = " Renamed ",
            Phone = " 1 ",
            Email = " c@d.test ",
            Address = " 2 Street "
        }, CancellationToken.None));
        Assert.Equal("Renamed", Db.Suppliers.Single(x => x.SupplierId == supplier.SupplierId).Name);

        Assert.True(await Suppliers.DeleteAsync(supplier.SupplierId, CancellationToken.None));
        Assert.False(Db.Suppliers.Single(x => x.SupplierId == supplier.SupplierId).IsActive);
    }

    // ============================ PURCHASE ORDERS ============================

    [Fact]
    public async Task CreatePurchase_ComputesTotal_AndCreatesItems()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest { Name = "S", Phone = "1", Email = "a@b.test", Address = "A" }, CancellationToken.None);
        var first = SeedIngredient(0m);
        var second = SeedIngredient(0m);
        var staff = SeedUser();

        var purchase = await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items =
            {
                new PurchaseOrderItemRequest { IngredientId = first.IngredientId, Quanlity = 3m, UnitPrice = 10m },
                new PurchaseOrderItemRequest { IngredientId = second.IngredientId, Quanlity = 2m, UnitPrice = 2.5m }
            }
        }, CancellationToken.None);

        Assert.Equal("Draft", purchase.Status);
        Assert.Equal(35m, purchase.TotalAmount);
        Assert.Equal(2, Db.PurchaseOrderItems.Count(x => x.PurchaseOrderId == purchase.Id));
        var item = Db.PurchaseOrderItems.Single(x => x.IngredientId == first.IngredientId);
        Assert.Equal(30m, item.TotalPrice);
    }

    [Fact]
    public async Task CreatePurchase_Throws_WhenSupplierOrIngredientIsNotActive()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest { Name = "S", Phone = "1", Email = "a@b.test", Address = "A" }, CancellationToken.None);
        var ingredient = SeedIngredient(0m);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = 999999,
            CreatedBy = 1,
            Items = { new PurchaseOrderItemRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m, UnitPrice = 1m } }
        }, CancellationToken.None));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = 1,
            Items = { new PurchaseOrderItemRequest { IngredientId = 999999, Quanlity = 1m, UnitPrice = 1m } }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task ReceivePurchase_AddsStock_WritesTransactions_AndMarksReceived()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest { Name = "S", Phone = "1", Email = "a@b.test", Address = "A" }, CancellationToken.None);
        var ingredient = SeedIngredient(1m);
        var staff = SeedUser();

        var purchase = await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items = { new PurchaseOrderItemRequest { IngredientId = ingredient.IngredientId, Quanlity = 9m, UnitPrice = 2m } }
        }, CancellationToken.None);

        Assert.True(await Purchases.ReceiveAsync(purchase.Id, new ReceivePurchaseRequest { ReceivedBy = staff.Id }, CancellationToken.None));

        var stored = Db.PurchaseOrders.Single(x => x.Id == purchase.Id);
        Assert.Equal("Received", stored.Status);
        Assert.NotNull(stored.ReceivedAt);
        Assert.Equal(10m, Db.Inventories.Single(x => x.IngredientId == ingredient.IngredientId).Quanlity);

        var transaction = Assert.Single(Db.InventoryTransactions.Where(x => x.ReferenceId == purchase.Id));
        Assert.Equal("Purchase", transaction.Type);
        Assert.Equal("PurchaseOrder", transaction.ReferenceType);
        Assert.Equal(9m, transaction.Quanlity);
        Assert.Equal(staff.Id, transaction.CreatedBy);
    }

    [Fact]
    public async Task ReceivePurchase_ReturnsFalse_WhenAlreadyReceivedOrMissing()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest { Name = "S", Phone = "1", Email = "a@b.test", Address = "A" }, CancellationToken.None);
        var ingredient = SeedIngredient(0m);
        var staff = SeedUser();
        var purchase = await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items = { new PurchaseOrderItemRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m, UnitPrice = 1m } }
        }, CancellationToken.None);

        Assert.True(await Purchases.ReceiveAsync(purchase.Id, new ReceivePurchaseRequest { ReceivedBy = staff.Id }, CancellationToken.None));
        Assert.False(await Purchases.ReceiveAsync(purchase.Id, new ReceivePurchaseRequest { ReceivedBy = staff.Id }, CancellationToken.None));
        Assert.False(await Purchases.ReceiveAsync(999999, new ReceivePurchaseRequest { ReceivedBy = staff.Id }, CancellationToken.None));
    }

    // ============================ PROMOTIONS ============================

    [Fact]
    public async Task CreatePromotion_RejectsEndBeforeStart()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Promotions.CreateAsync(new PromotionRequest
        {
            Name = "Bad",
            DiscountType = "Percent",
            DiscountValue = 10m,
            StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddDays(-1)
        }, CancellationToken.None));

        Assert.Equal("EndAt must be after StartAt", ex.Message);
    }

    [Fact]
    public async Task CreatePromotion_WithNullOptionalFields_DoesNotThrow()
    {
        // Nhóm B8: Description không bắt buộc → JSON null không được gây NRE.
        var promotion = await Promotions.CreateAsync(new PromotionRequest
        {
            Name = "  Tet  ",
            Description = null!,
            DiscountType = "Percent",
            DiscountValue = 10m,
            StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddDays(1)
        }, CancellationToken.None);

        Assert.Equal("Tet", promotion.Name);
        Assert.Equal(string.Empty, promotion.Description);
    }

    [Fact]
    public async Task Promotion_Lifecycle()
    {
        var promotion = await Promotions.CreateAsync(new PromotionRequest
        {
            Name = "  Tet  ",
            Description = " d ",
            DiscountType = " Percent ",
            DiscountValue = 10m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            MinimumOrderAmount = 100m,
            MaxDiscountAmount = 50m
        }, CancellationToken.None);

        Assert.Equal("Tet", promotion.Name);
        Assert.Equal("Percent", promotion.DiscountType);
        Assert.True(promotion.IsActive);

        Assert.True(await Promotions.SetActiveAsync(promotion.Id, false, CancellationToken.None));
        Assert.False(Db.Promotions.Single(x => x.Id == promotion.Id).IsActive);
        Assert.False(await Promotions.SetActiveAsync(999999, false, CancellationToken.None));

        Assert.True(await Promotions.UpdateAsync(promotion.Id, new PromotionRequest
        {
            Name = " Tet 2 ",
            Description = "d",
            DiscountType = "Fixed",
            DiscountValue = 5m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(2)
        }, CancellationToken.None));
        Assert.Equal("Tet 2", Db.Promotions.Single(x => x.Id == promotion.Id).Name);
        Assert.Equal("Fixed", Db.Promotions.Single(x => x.Id == promotion.Id).DiscountType);

        await Assert.ThrowsAsync<ArgumentException>(() => Promotions.UpdateAsync(promotion.Id, new PromotionRequest
        {
            Name = "x",
            StartAt = DateTime.UtcNow,
            EndAt = DateTime.UtcNow.AddDays(-1)
        }, CancellationToken.None));

        Assert.True(await Promotions.DeleteAsync(promotion.Id, CancellationToken.None));
        Assert.False(Db.Promotions.Single(x => x.Id == promotion.Id).IsActive);
        Assert.False(await Promotions.UpdateAsync(999999, new PromotionRequest { Name = "x" }, CancellationToken.None));
    }

    // ============================ RECIPES ============================

    [Fact]
    public async Task CreateRecipe_RequiresActiveFoodSize_AndAtLeastOneIngredient()
    {
        var ingredient = SeedIngredient(10m);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = 999999,
            Name = "r",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None));

        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 10m);

        await Assert.ThrowsAsync<ArgumentException>(() => Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "r"
        }, CancellationToken.None));
    }

    [Fact]
    public async Task CreateRecipe_WithNullVersion_DefaultsToOne()
    {
        // Nhóm B8: Version không bắt buộc → JSON null phải mặc định "1" thay vì NRE.
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 10m);
        var ingredient = SeedIngredient(10m);

        var recipe = await Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "  Pho  ",
            Version = null!,
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None);

        Assert.Equal("Pho", recipe.Name);
        Assert.Equal("1", recipe.Version);
    }

    [Fact]
    public async Task CreateRecipe_RejectsDuplicateOrUnknownIngredients()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 10m);
        var ingredient = SeedIngredient(10m);

        var duplicate = await Assert.ThrowsAsync<ArgumentException>(() => Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "r",
            Ingredients =
            {
                new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m },
                new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 2m }
            }
        }, CancellationToken.None));
        Assert.Equal("An ingredient cannot be repeated in the same recipe", duplicate.Message);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "r",
            Ingredients = { new RecipeIngredientRequest { IngredientId = 999999, Quanlity = 1m } }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task Recipe_Lifecycle_WithDetails()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 10m);
        var ingredient = SeedIngredient(10m);

        var recipe = await Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "  Pho broth  ",
            Version = " 2 ",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 2.5m } }
        }, CancellationToken.None);

        Assert.Equal("Pho broth", recipe.Name);
        Assert.Equal("2", recipe.Version);
        Assert.True(recipe.IsActive);

        var details = await Recipes.GetDetailsAsync(recipe.RecipeId, CancellationToken.None);
        Assert.NotNull(details);
        Assert.Equal(size.SizeId, details!.SizeId);
        Assert.Equal(food.FoodName, details.FoodName);
        Assert.Equal(size.SizeName, details.SizeName);
        var detailIngredient = Assert.Single(details.Ingredients);
        Assert.Equal(ingredient.IngredientId, detailIngredient.IngredientId);
        Assert.Equal(2.5m, detailIngredient.Quanlity);
        Assert.Equal(ingredient.Name, detailIngredient.IngredientName);

        Assert.Null(await Recipes.GetDetailsAsync(999999, CancellationToken.None));

        Assert.True(await Recipes.DeleteAsync(recipe.RecipeId, CancellationToken.None));
        Assert.False(Db.Recipes.Single(x => x.RecipeId == recipe.RecipeId).IsActive);
        Assert.False(await Recipes.DeleteAsync(999999, CancellationToken.None));
    }

    [Fact]
    public async Task Recipes_ListContainsOnlyActiveRecipes()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 10m, name: "M");
        var otherSize = SeedSize(food.FoodId, 12m, isDefault: false, name: "L");
        var ingredient = SeedIngredient(10m);
        var active = await Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "active",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None);
        // Nhóm B5: mỗi size chỉ có một công thức active ⇒ công thức thứ hai dùng size khác.
        var removed = await Recipes.CreateAsync(new RecipeRequest
        {
            SizeId = otherSize.SizeId,
            Name = "removed",
            Ingredients = { new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m } }
        }, CancellationToken.None);
        await Recipes.DeleteAsync(removed.RecipeId, CancellationToken.None);

        var all = await Recipes.GetAllAsync(CancellationToken.None);

        Assert.Contains(all, x => x.RecipeId == active.RecipeId);
        Assert.DoesNotContain(all, x => x.RecipeId == removed.RecipeId);
        Assert.Equal(1, all.Single(x => x.RecipeId == active.RecipeId).IngredientCount);
    }

    // ============================ KITCHEN (ManagementService duplicate) ============================

    [Fact]
    public async Task ManagementKitchen_UpdateSetsTimestamps_ButDoesNotValidateStatus()
    {
        // Baseline: ManagementService.UpdateKitchen accepts any status string,
        // unlike KitchenService which enforces an allow-list.
        var s = SeedOrderScenario();
        var order = SeedOrder(s.Table.TableId, s.Staff.Id);
        Db.KitchenOrders.Add(new KitchenOrder { OrderId = order.OrderId, Status = "Pending", SentAt = DateTime.UtcNow, Priority = 0 });
        Db.SaveChanges();
        var kitchenOrderId = Db.KitchenOrders.Single().KitchenOrderId;

        var all = await Management.Kitchen(CancellationToken.None);
        Assert.Contains(all, x => x.KitchenOrderId == kitchenOrderId);

        Assert.True(await Management.UpdateKitchen(kitchenOrderId, new KitchenStatusRequest { Status = "TotallyInvalid" }, CancellationToken.None));
        Assert.Equal("TotallyInvalid", Db.KitchenOrders.Single(x => x.KitchenOrderId == kitchenOrderId).Status);

        Assert.False(await Management.UpdateKitchen(999999, new KitchenStatusRequest { Status = "Ready" }, CancellationToken.None));
    }
}
