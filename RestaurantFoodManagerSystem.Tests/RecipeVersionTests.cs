using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// C-19 (gap-review C3) — công thức có **phiên bản**: sửa công thức không được ghi đè
/// bản cũ, mà phải tạo bản mới (bản cũ chuyển sang inactive, giữ nguyên định lượng).
///
/// <para>
/// Unique index <c>(SizeId) WHERE IsActive = 1</c> đã có sẵn ⇒ không cần đổi schema và
/// luôn bảo đảm chỉ có một bản đang chạy cho mỗi size.
/// </para>
/// </summary>
public sealed class RecipeVersionTests : TestBase
{
    private (Recipe Recipe, FoodSize Size, Ingredient Ingredient) SeedRecipeWithIngredient(
        string version = "1")
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 50m);
        var ingredient = SeedIngredient(100m);

        var recipe = new Recipe
        {
            SizeId = size.SizeId,
            Name = "Công thức gốc",
            Version = version,
            IsActive = true
        };
        Db.Recipes.Add(recipe);
        Db.SaveChanges();

        Db.RecipeIngredients.Add(new RecipeIngredient
        {
            RecipeId = recipe.RecipeId,
            IngredientId = ingredient.IngredientId,
            Quanlity = 2m
        });
        Db.SaveChanges();

        return (recipe, size, ingredient);
    }

    [Fact]
    public async Task UpdateRecipe_SameVersion_AutomaticallyIncrements()
    {
        // Client (và form FE) gửi lại đúng version đang chạy ⇒ server tự tăng,
        // tránh có hai bản cùng tên phiên bản.
        var (recipe, size, ingredient) = SeedRecipeWithIngredient();

        Assert.True(await Recipes.UpdateAsync(recipe.RecipeId, new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "Công thức gốc",
            Version = "1",
            Ingredients = [new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 3m }]
        }, CancellationToken.None));

        var active = await Db.Recipes.AsNoTracking()
            .SingleAsync(r => r.SizeId == size.SizeId && r.IsActive);

        Assert.Equal("2", active.Version);
    }

    [Fact]
    public async Task UpdateRecipe_EmptyVersion_AutomaticallyIncrements()
    {
        var (recipe, size, ingredient) = SeedRecipeWithIngredient();

        Assert.True(await Recipes.UpdateAsync(recipe.RecipeId, new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "Công thức gốc",
            Version = "",
            Ingredients = [new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 3m }]
        }, CancellationToken.None));

        var active = await Db.Recipes.AsNoTracking()
            .SingleAsync(r => r.SizeId == size.SizeId && r.IsActive);

        Assert.Equal("2", active.Version);
    }

    [Fact]
    public async Task UpdateRecipe_DifferentVersion_KeepsRequestedVersion()
    {
        var (recipe, size, ingredient) = SeedRecipeWithIngredient();

        Assert.True(await Recipes.UpdateAsync(recipe.RecipeId, new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "Công thức gốc",
            Version = "v2-riêng",
            Ingredients = [new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 3m }]
        }, CancellationToken.None));

        var active = await Db.Recipes.AsNoTracking()
            .SingleAsync(r => r.SizeId == size.SizeId && r.IsActive);

        Assert.Equal("v2-riêng", active.Version);
    }

    [Fact]
    public async Task UpdateRecipe_KeepsOldVersionAsHistory()
    {
        var (recipe, size, ingredient) = SeedRecipeWithIngredient();

        await Recipes.UpdateAsync(recipe.RecipeId, new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "Công thức mới",
            Version = "2",
            Ingredients = [new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 5m }]
        }, CancellationToken.None);

        using var check = NewContext();

        // Bản cũ: không hoạt động, giữ nguyên định lượng cũ.
        var old = check.Recipes.Single(r => r.RecipeId == recipe.RecipeId);
        Assert.False(old.IsActive);
        var oldLine = check.RecipeIngredients.Single(ri => ri.RecipeId == recipe.RecipeId);
        Assert.Equal(2m, oldLine.Quanlity);

        // Bản mới: đang hoạt động, định lượng mới.
        var created = check.Recipes.Single(r => r.SizeId == size.SizeId && r.IsActive);
        var newLine = check.RecipeIngredients.Single(ri => ri.RecipeId == created.RecipeId);
        Assert.Equal(5m, newLine.Quanlity);
    }

    [Fact]
    public async Task UpdateRecipe_SupersededVersion_IsRejected()
    {
        // Sửa lần 2 bằng id của bản đã bị thay thế ⇒ phải báo lỗi, không tạo thêm bản.
        var (recipe, size, ingredient) = SeedRecipeWithIngredient();

        await Recipes.UpdateAsync(recipe.RecipeId, new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "Công thức mới",
            Version = "2",
            Ingredients = [new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 5m }]
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Recipes.UpdateAsync(
            recipe.RecipeId,
            new RecipeRequest
            {
                SizeId = size.SizeId,
                Name = "Công thức mới 2",
                Version = "3",
                Ingredients = [new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 6m }]
            },
            CancellationToken.None));

        Assert.Contains("no longer active", ex.Message);

        // Vẫn chỉ có đúng một bản đang chạy.
        Assert.Single(Db.Recipes.Where(r => r.SizeId == size.SizeId && r.IsActive));
    }

    [Fact]
    public async Task GetAll_IncludeInactive_ReturnsEveryVersion()
    {
        var (recipe, size, ingredient) = SeedRecipeWithIngredient();

        await Recipes.UpdateAsync(recipe.RecipeId, new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "Công thức mới",
            Version = "2",
            Ingredients = [new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 5m }]
        }, CancellationToken.None);

        var activeOnly = await Recipes.GetAllAsync(includeInactive: false, CancellationToken.None);
        var all = await Recipes.GetAllAsync(includeInactive: true, CancellationToken.None);

        Assert.Single(activeOnly);
        Assert.Equal(2, all.Count);
        Assert.Equal(2, all.Count(x => x.SizeId == size.SizeId));
    }

    [Fact]
    public async Task UpdateRecipe_ChangesStockDeduction_ThroughNewActiveVersion()
    {
        // C-19: công thức đang chạy quyết định trừ kho ⇒ sửa công thức phải đổi được
        // lượng trừ kho, và bản cũ không còn tác động.
        var (recipe, size, ingredient) = SeedRecipeWithIngredient();
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var table = SeedTable();
        var staff = SeedUser();

        // Size của công thức vừa sửa phải là size có thể gọi món.
        await Recipes.UpdateAsync(recipe.RecipeId, new RecipeRequest
        {
            SizeId = size.SizeId,
            Name = "Công thức mới",
            Version = "2",
            Ingredients = [new RecipeIngredientRequest { IngredientId = ingredient.IngredientId, Quanlity = 4m }]
        }, CancellationToken.None);

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = table.TableId,
            CreatedBy = staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        Assert.Equal(50m, order.Subtotal);

        // 4 đơn vị/1 phần theo công thức MỚI (bản cũ là 2, đã bị thay thế).
        var stock = Db.Inventories.Single(x => x.IngredientId == ingredient.IngredientId);
        Assert.Equal(96m, stock.Quanlity);
    }
}
