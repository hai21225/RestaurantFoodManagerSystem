using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// HTTP-level coverage cho soft-delete/restore và các endpoint GET by id mới.
/// </summary>
public sealed class SoftDeleteRestoreHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public SoftDeleteRestoreHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    private void Seed(Action<AppDbContext> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        db.SaveChanges();
    }

    private T Read<T>(Func<AppDbContext, T> read)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return read(db);
    }

    [Fact]
    public async Task GetIngredientById_ReturnsInactiveDto()
    {
        var id = 0;
        var name = $"http-ingredient-{Guid.NewGuid():N}";
        Seed(db =>
        {
            var ingredient = new Ingredient
            {
                Name = name,
                Unit = "kg",
                MinimumStock = 1m,
                IsActive = false
            };
            db.Ingredients.Add(ingredient);
            db.SaveChanges();
            id = ingredient.IngredientId;
        });

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/management/ingredients/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, row.GetProperty("ingredientId").GetInt32());
        Assert.Equal(name, row.GetProperty("name").GetString());
        Assert.False(row.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task GetSupplierById_ReturnsInactiveDto()
    {
        var id = 0;
        var name = $"http-supplier-{Guid.NewGuid():N}";
        Seed(db =>
        {
            var supplier = new Supplier
            {
                Name = name,
                Phone = "0000000000",
                Email = "supplier@test.local",
                Address = "Test",
                IsActive = false
            };
            db.Suppliers.Add(supplier);
            db.SaveChanges();
            id = supplier.SupplierId;
        });

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/management/suppliers/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, row.GetProperty("supplierId").GetInt32());
        Assert.Equal(name, row.GetProperty("name").GetString());
        Assert.False(row.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task GetPromotionById_ReturnsDto()
    {
        var id = 0;
        var name = $"http-promotion-{Guid.NewGuid():N}";
        Seed(db =>
        {
            var promotion = new Promotion
            {
                Name = name,
                Description = "Test",
                DiscountType = "Percent",
                DiscountValue = 10m,
                StartAt = DateTime.UtcNow.AddDays(-1),
                EndAt = DateTime.UtcNow.AddDays(1),
                IsActive = false
            };
            db.Promotions.Add(promotion);
            db.SaveChanges();
            id = promotion.Id;
        });

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/management/promotions/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, row.GetProperty("id").GetInt32());
        Assert.Equal(name, row.GetProperty("name").GetString());
        Assert.False(row.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task PatchIngredientActive_RestoresSoftDeletedIngredient()
    {
        var id = 0;
        Seed(db =>
        {
            var ingredient = new Ingredient
            {
                Name = $"restore-ingredient-{Guid.NewGuid():N}",
                Unit = "kg",
                IsActive = false
            };
            db.Ingredients.Add(ingredient);
            db.SaveChanges();
            id = ingredient.IngredientId;
        });

        using var client = _factory.CreateClient();
        var response = await client.PatchAsJsonAsync(
            $"/api/management/ingredients/{id}/active",
            new { isActive = true });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(Read(db => db.Ingredients.AsNoTracking()
            .Single(x => x.IngredientId == id).IsActive));
    }

    [Fact]
    public async Task PatchToppingActive_RestoresSoftDeletedTopping()
    {
        var id = 0;
        Seed(db =>
        {
            var topping = new Topping
            {
                ToppingName = $"restore-topping-{Guid.NewGuid():N}",
                ToppingPrice = 5m,
                IsActive = false,
                IsAvailable = false
            };
            db.Toppings.Add(topping);
            db.SaveChanges();
            id = topping.ToppingId;
        });

        using var client = _factory.CreateClient();
        var response = await client.PatchAsJsonAsync(
            $"/api/toppings/{id}/active",
            new { isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Read(db => db.Toppings.AsNoTracking()
            .Single(x => x.ToppingId == id).IsActive));
    }

    [Fact]
    public async Task PatchFoodSizeActive_RestoresSoftDeletedSize()
    {
        var foodId = 0;
        var sizeId = 0;
        Seed(db =>
        {
            var category = new Category
            {
                CategoryName = $"restore-size-cat-{Guid.NewGuid():N}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Categories.Add(category);
            db.SaveChanges();

            var food = new Food
            {
                CategoryId = category.CategoryId,
                FoodName = $"restore-size-food-{Guid.NewGuid():N}",
                FoodPrice = 10m,
                IsActive = true,
                IsAvailable = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Foods.Add(food);
            db.SaveChanges();

            var size = new FoodSize
            {
                FoodId = food.FoodId,
                SizeName = "M",
                SizePrice = 10m,
                IsActive = false,
                IsDefault = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.FoodSizes.Add(size);
            db.SaveChanges();

            foodId = food.FoodId;
            sizeId = size.SizeId;
        });

        using var client = _factory.CreateClient();
        var response = await client.PatchAsJsonAsync(
            $"/api/foods/{foodId}/sizes/{sizeId}/active",
            new { isActive = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(Read(db => db.FoodSizes.AsNoTracking()
            .Single(x => x.SizeId == sizeId).IsActive));
    }

    [Fact]
    public async Task GetFoodsByCategory_IncludeInactive_ReturnsSoftDeletedFood()
    {
        var categoryId = 0;
        var activeId = 0;
        var inactiveId = 0;
        Seed(db =>
        {
            var category = new Category
            {
                CategoryName = $"http-food-cat-{Guid.NewGuid():N}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Categories.Add(category);
            db.SaveChanges();

            var active = new Food
            {
                CategoryId = category.CategoryId,
                FoodName = $"active-{Guid.NewGuid():N}",
                FoodPrice = 10m,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var inactive = new Food
            {
                CategoryId = category.CategoryId,
                FoodName = $"inactive-{Guid.NewGuid():N}",
                FoodPrice = 10m,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Foods.AddRange(active, inactive);
            db.SaveChanges();

            categoryId = category.CategoryId;
            activeId = active.FoodId;
            inactiveId = inactive.FoodId;
        });

        using var client = _factory.CreateClient();
        var visible = await client.GetFromJsonAsync<JsonElement>(
            $"/api/foods?categoryId={categoryId}");
        var all = await client.GetFromJsonAsync<JsonElement>(
            $"/api/foods?categoryId={categoryId}&includeInactive=true");

        Assert.Contains(visible.EnumerateArray(), x => x.GetProperty("foodId").GetInt32() == activeId);
        Assert.DoesNotContain(visible.EnumerateArray(), x => x.GetProperty("foodId").GetInt32() == inactiveId);
        Assert.Contains(all.EnumerateArray(), x => x.GetProperty("foodId").GetInt32() == inactiveId);
    }

    [Fact]
    public async Task GetRecipes_IncludeInactive_ReturnsSoftDeletedRecipe()
    {
        var recipeId = 0;
        Seed(db =>
        {
            var category = new Category
            {
                CategoryName = $"http-recipe-cat-{Guid.NewGuid():N}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Categories.Add(category);
            db.SaveChanges();

            var food = new Food
            {
                CategoryId = category.CategoryId,
                FoodName = $"http-recipe-food-{Guid.NewGuid():N}",
                FoodPrice = 10m,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Foods.Add(food);
            db.SaveChanges();

            var size = new FoodSize
            {
                FoodId = food.FoodId,
                SizeName = "M",
                SizePrice = 10m,
                IsActive = true,
                IsDefault = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.FoodSizes.Add(size);
            db.SaveChanges();

            var recipe = new Recipe
            {
                SizeId = size.SizeId,
                Name = $"http-recipe-{Guid.NewGuid():N}",
                Version = "1",
                IsActive = false
            };
            db.Recipes.Add(recipe);
            db.SaveChanges();

            recipeId = recipe.RecipeId;
        });

        using var client = _factory.CreateClient();
        var visible = await client.GetFromJsonAsync<JsonElement>("/api/management/recipes");
        var all = await client.GetFromJsonAsync<JsonElement>(
            "/api/management/recipes?includeInactive=true");

        Assert.DoesNotContain(visible.EnumerateArray(), x => x.GetProperty("recipeId").GetInt32() == recipeId);
        Assert.Contains(all.EnumerateArray(), x => x.GetProperty("recipeId").GetInt32() == recipeId);
    }
}
