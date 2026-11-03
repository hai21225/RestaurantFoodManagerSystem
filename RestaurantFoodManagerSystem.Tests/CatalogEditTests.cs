using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// GĐ6/A3 + A4 — cập nhật công thức (<c>PUT /api/management/recipes/{id}</c>)
/// và bật/tắt món (<c>PATCH /api/foods/{id}/active</c>).
/// </summary>
public sealed class CatalogEditTests : TestBase
{
    // ---------- PATCH /api/foods/{id}/active ----------

    [Fact]
    public async Task UpdateActive_ReactivatesFood()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId, isActive: false, isAvailable: false);

        var result = await Foods.UpdateActiveAsync(food.FoodId, true);

        Assert.True(result);
        var reloaded = await Db.Foods.AsNoTracking().FirstAsync(f => f.FoodId == food.FoodId);
        Assert.True(reloaded.IsActive);
    }

    [Fact]
    public async Task UpdateActive_Deactivating_AlsoClearsAvailable()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId, isActive: true, isAvailable: true);

        var result = await Foods.UpdateActiveAsync(food.FoodId, false);

        Assert.True(result);
        var reloaded = await Db.Foods.AsNoTracking().FirstAsync(f => f.FoodId == food.FoodId);
        Assert.False(reloaded.IsActive);
        Assert.False(reloaded.IsAvailable);
    }

    [Fact]
    public async Task UpdateActive_ReturnsFalse_WhenFoodMissing()
    {
        Assert.False(await Foods.UpdateActiveAsync(999_999, true));
    }

    // ---------- PUT /api/management/recipes/{id} ----------

    private Recipe SeedActiveRecipeWithIngredients(out FoodSize size, out Ingredient first, out Ingredient second)
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        size = SeedSize(food.FoodId, 50m);
        first = SeedIngredient(100m);
        second = SeedIngredient(100m);
        return SeedRecipe(size.SizeId, (first.IngredientId, 1m));
    }

    [Fact]
    public async Task UpdateRecipe_ReplacesIngredients_AndUpdatesFields()
    {
        var recipe = SeedActiveRecipeWithIngredients(out var size, out var first, out var second);

        var request = new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "Công thức mới",
            Version = "2",
            Ingredients =
            [
                new RecipeIngredientRequest { IngredientId = first.IngredientId, Quanlity = 3m },
                new RecipeIngredientRequest { IngredientId = second.IngredientId, Quanlity = 0.5m }
            ]
        };

        var result = await Recipes.UpdateAsync(recipe.RecipeId, request, default);

        Assert.True(result);

        // C-19: bản cũ KHÔNG bị ghi đè — nó chuyển sang không hoạt động và giữ nguyên
        // định lượng cũ để làm minh chứng lịch sử.
        var old = await Db.Recipes.AsNoTracking().FirstAsync(r => r.RecipeId == recipe.RecipeId);
        Assert.False(old.IsActive);
        Assert.Equal("1", old.Version);

        var oldLines = await Db.RecipeIngredients.AsNoTracking()
            .Where(ri => ri.RecipeId == recipe.RecipeId)
            .ToListAsync();
        Assert.Single(oldLines);
        Assert.Equal(1m, oldLines[0].Quanlity);

        // Bản mới là bản đang chạy cho size này (unique index SizeId WHERE IsActive=1).
        var created = await Db.Recipes.AsNoTracking()
            .SingleAsync(r => r.SizeId == size.SizeId && r.IsActive);
        Assert.NotEqual(recipe.RecipeId, created.RecipeId);
        Assert.Equal("Công thức mới", created.Name);
        Assert.Equal("2", created.Version);

        var lines = await Db.RecipeIngredients.AsNoTracking()
            .Where(ri => ri.RecipeId == created.RecipeId)
            .OrderBy(ri => ri.IngredientId)
            .ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(3m, lines.Single(l => l.IngredientId == first.IngredientId).Quanlity);
        Assert.Equal(0.5m, lines.Single(l => l.IngredientId == second.IngredientId).Quanlity);
    }

    [Fact]
    public async Task UpdateRecipe_ReturnsFalse_WhenRecipeMissing()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 50m);
        var ingredient = SeedIngredient(10m);

        var request = new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "x",
            Version = "1",
            Ingredients = [new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m }]
        };

        Assert.False(await Recipes.UpdateAsync(999_999, request, default));
    }

    [Fact]
    public async Task UpdateRecipe_Throws_WhenIngredientsEmpty()
    {
        var recipe = SeedActiveRecipeWithIngredients(out var size, out _, out _);

        var request = new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "x",
            Version = "1",
            Ingredients = []
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Recipes.UpdateAsync(recipe.RecipeId, request, default));
    }

    [Fact]
    public async Task UpdateRecipe_Throws_WhenIngredientRepeated()
    {
        var recipe = SeedActiveRecipeWithIngredients(out var size, out var first, out _);

        var request = new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "x",
            Version = "1",
            Ingredients =
            [
                new RecipeIngredientRequest { IngredientId = first.IngredientId, Quanlity = 1m },
                new RecipeIngredientRequest { IngredientId = first.IngredientId, Quanlity = 2m }
            ]
        };

        await Assert.ThrowsAsync<ArgumentException>(
            () => Recipes.UpdateAsync(recipe.RecipeId, request, default));
    }

    [Fact]
    public async Task UpdateRecipe_Throws_WhenSizeMissing()
    {
        var recipe = SeedActiveRecipeWithIngredients(out _, out var first, out _);

        var request = new RecipeRequest
        {
            SizeId = 999_999,
            Name = "x",
            Version = "1",
            Ingredients = [new RecipeIngredientRequest { IngredientId = first.IngredientId, Quanlity = 1m }]
        };

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => Recipes.UpdateAsync(recipe.RecipeId, request, default));
    }
}
