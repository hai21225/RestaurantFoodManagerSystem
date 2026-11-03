using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Thống nhất vòng đời soft-delete: DELETE chỉ đặt IsActive = false, dữ liệu lịch sử
/// vẫn còn, và mọi entity có thể được khôi phục qua SetActiveAsync.
/// </summary>
public sealed class SoftDeleteLifecycleTests : TestBase
{
    [Fact]
    public async Task Topping_Delete_IsSoft_AndCanBeRestored()
    {
        var topping = SeedTopping();

        Assert.True(await Toppings.DeleteAsync(topping.ToppingId));

        Assert.DoesNotContain(
            await Toppings.GetAllAsync(),
            x => x.ToppingId == topping.ToppingId);
        Assert.Contains(
            await Toppings.GetAllAsync(includeInactive: true),
            x => x.ToppingId == topping.ToppingId);

        Assert.True(await Toppings.SetActiveAsync(topping.ToppingId, true));

        var reloaded = await Db.Toppings.AsNoTracking()
            .SingleAsync(x => x.ToppingId == topping.ToppingId);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Topping_Restore_Conflicts_WhenActiveNameAlreadyExists()
    {
        var name = $"dup-topping-{Guid.NewGuid():N}";
        var active = new Topping
        {
            ToppingName = name,
            ToppingPrice = 1m,
            IsActive = true,
            IsAvailable = true
        };
        var deleted = new Topping
        {
            ToppingName = name,
            ToppingPrice = 2m,
            IsActive = false,
            IsAvailable = false
        };
        Db.Toppings.AddRange(active, deleted);
        Db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Toppings.SetActiveAsync(deleted.ToppingId, true));
    }

    [Fact]
    public async Task Ingredient_Delete_IsSoft_AndCanBeRestored()
    {
        var ingredient = SeedIngredient(stock: 10m);

        Assert.True(await Ingredients.DeleteAsync(ingredient.IngredientId, default));

        var deleted = await Ingredients.GetByIdAsync(ingredient.IngredientId);
        Assert.NotNull(deleted);
        Assert.False(deleted!.IsActive);

        Assert.True(await Ingredients.SetActiveAsync(ingredient.IngredientId, true));

        var reloaded = await Db.Ingredients.AsNoTracking()
            .SingleAsync(x => x.IngredientId == ingredient.IngredientId);
        Assert.True(reloaded.IsActive);

        // Dòng tồn kho vẫn còn sau soft-delete/restore.
        Assert.True(await Db.Inventories.AsNoTracking()
            .AnyAsync(x => x.IngredientId == ingredient.IngredientId));
    }

    [Fact]
    public async Task Supplier_Delete_IsSoft_AndCanBeRestored()
    {
        var supplier = SeedSupplier();

        Assert.True(await Suppliers.DeleteAsync(supplier.SupplierId, default));

        var deleted = await Suppliers.GetByIdAsync(supplier.SupplierId);
        Assert.NotNull(deleted);
        Assert.False(deleted!.IsActive);

        Assert.True(await Suppliers.SetActiveAsync(supplier.SupplierId, true));

        var reloaded = await Db.Suppliers.AsNoTracking()
            .SingleAsync(x => x.SupplierId == supplier.SupplierId);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Recipe_Delete_IsSoft_AndCanBeRestored()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 50m);
        var ingredient = SeedIngredient(stock: 100m);
        var recipe = SeedRecipe(size.SizeId, (ingredient.IngredientId, 1m));

        Assert.True(await Recipes.DeleteAsync(recipe.RecipeId, default));

        Assert.DoesNotContain(
            await Recipes.GetAllAsync(CancellationToken.None),
            x => x.RecipeId == recipe.RecipeId);
        Assert.Contains(
            await Recipes.GetAllAsync(includeInactive: true, CancellationToken.None),
            x => x.RecipeId == recipe.RecipeId);

        Assert.True(await Recipes.SetActiveAsync(recipe.RecipeId, true));

        var reloaded = await Db.Recipes.AsNoTracking()
            .SingleAsync(x => x.RecipeId == recipe.RecipeId);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Recipe_Restore_Conflicts_WhenSizeAlreadyHasActiveRecipe()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 50m);
        var ingredient = SeedIngredient(stock: 100m);
        SeedRecipe(size.SizeId, (ingredient.IngredientId, 1m));

        var deleted = new Recipe
        {
            SizeId = size.SizeId,
            Name = $"deleted-recipe-{Guid.NewGuid():N}",
            Version = "1",
            IsActive = false
        };
        Db.Recipes.Add(deleted);
        Db.SaveChanges();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Recipes.SetActiveAsync(deleted.RecipeId, true));
    }

    [Fact]
    public async Task Promotion_Delete_IsSoft_AndCanBeRestored()
    {
        var promotion = await Promotions.CreateAsync(new PromotionRequest
        {
            Name = $"promo-{Guid.NewGuid():N}",
            DiscountType = "Percent",
            DiscountValue = 10m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1)
        }, default);

        Assert.True(await Promotions.DeleteAsync(promotion.Id, default));

        var deleted = await Promotions.GetByIdAsync(promotion.Id);
        Assert.NotNull(deleted);
        Assert.False(deleted!.IsActive);

        Assert.True(await Promotions.SetActiveAsync(promotion.Id, true, default));

        var reloaded = await Db.Promotions.AsNoTracking()
            .SingleAsync(x => x.Id == promotion.Id);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task Category_Delete_IsSoft_AndCanBeRestored()
    {
        var category = SeedCategory();

        Assert.True(await Categories.DeleteCategoryAsync(category.CategoryId, default));

        var deleted = await Categories.GetCategoryByIdAsync(category.CategoryId);
        Assert.False(deleted.IsActive);

        Assert.True(await Categories.SetActiveAsync(category.CategoryId, true));

        var reloaded = await Db.Categories.AsNoTracking()
            .SingleAsync(x => x.CategoryId == category.CategoryId);
        Assert.True(reloaded.IsActive);
    }
}
