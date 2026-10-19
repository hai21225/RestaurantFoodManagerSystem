using Microsoft.EntityFrameworkCore;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>Pins the repository-based services (Food, Category, FoodSize, Topping, FoodTopping).</summary>
public sealed class FoodCatalogTests : TestBase
{
    // ============================ FOOD ============================

    [Fact]
    public async Task AddFood_CreatesActiveFood_AndReturnsDto()
    {
        var category = SeedCategory();

        var dto = await Foods.AddFoodAsync(new CreateFoodRequest
        {
            CategoryId = category.CategoryId,
            FoodName = "  Pho  ",
            FoodDescription = "  beef  ",
            FoodImageUrl = "pho.png",
            FoodPrice = 45m,
            IsAvailable = true
        });

        Assert.Equal("Pho", dto.FoodName);
        Assert.Equal("beef", dto.FoodDescription);
        Assert.Equal(45m, dto.FoodPrice);
        Assert.True(dto.IsActive);
        Assert.True(dto.IsAvailable);

        var stored = Db.Foods.Single(x => x.FoodId == dto.FoodId);
        Assert.True(stored.IsActive);
        Assert.True(stored.CreatedAt > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task AddFood_Throws_WhenCategoryDoesNotExist()
    {
        var ex = await Assert.ThrowsAsync<Exception>(() => Foods.AddFoodAsync(new CreateFoodRequest
        {
            CategoryId = 999999,
            FoodName = "Ghost"
        }));

        Assert.Equal("Category not found", ex.Message);
    }

    [Fact]
    public async Task GetFoodById_ReturnsNull_WhenMissing()
    {
        Assert.Null(await Foods.GetFoodByIdAsync(999999));
    }

    [Fact]
    public async Task GetAllFoods_ExcludesInactive_UnlessRequested()
    {
        var category = SeedCategory();
        var active = SeedFood(category.CategoryId);
        var inactive = SeedFood(category.CategoryId, isActive: false);

        var visible = await Foods.GetAllFoodsAsync(includeInactive: false);
        var all = await Foods.GetAllFoodsAsync(includeInactive: true);

        Assert.Contains(visible, x => x.FoodId == active.FoodId);
        Assert.DoesNotContain(visible, x => x.FoodId == inactive.FoodId);
        Assert.Contains(all, x => x.FoodId == inactive.FoodId);
    }

    [Fact]
    public async Task GetFoodsByCategory_ReturnsOnlyActiveFoodsOfThatCategory()
    {
        var first = SeedCategory();
        var second = SeedCategory();
        var expected = SeedFood(first.CategoryId);
        SeedFood(first.CategoryId, isActive: false);
        SeedFood(second.CategoryId);

        var result = await Foods.GetFoodsByCategoryIdAsync(first.CategoryId);

        Assert.Single(result);
        Assert.Equal(expected.FoodId, result[0].FoodId);
    }

    [Fact]
    public async Task UpdateFood_ReturnsNull_WhenMissing_AndThrowsWhenCategoryMissing()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);

        Assert.Null(await Foods.UpdateFoodAsync(999999, new UpdateFoodRequest { CategoryId = category.CategoryId, FoodName = "x" }));

        await Assert.ThrowsAsync<Exception>(() => Foods.UpdateFoodAsync(food.FoodId, new UpdateFoodRequest
        {
            CategoryId = 999999,
            FoodName = "x"
        }));
    }

    [Fact]
    public async Task UpdateFood_UpdatesEditableFields()
    {
        var category = SeedCategory();
        var other = SeedCategory();
        var food = SeedFood(category.CategoryId);

        var dto = await Foods.UpdateFoodAsync(food.FoodId, new UpdateFoodRequest
        {
            CategoryId = other.CategoryId,
            FoodName = " Updated ",
            FoodDescription = " d ",
            FoodImageUrl = " img ",
            FoodPrice = 99m,
            IsAvailable = false
        });

        Assert.NotNull(dto);
        Assert.Equal(other.CategoryId, dto!.CategoryId);
        Assert.Equal("Updated", dto.FoodName);
        Assert.Equal(99m, dto.FoodPrice);
        Assert.False(dto.IsAvailable);
    }

    [Fact]
    public async Task UpdateAvailability_TogglesFlag_AndReturnsFalseWhenMissing()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId, isAvailable: true);

        Assert.True(await Foods.UpdateAvailabilityAsync(food.FoodId, false));
        Assert.False(Db.Foods.Single(x => x.FoodId == food.FoodId).IsAvailable);
        Assert.False(await Foods.UpdateAvailabilityAsync(999999, true));
    }

    [Fact]
    public async Task DeleteFood_SoftDeletes_AndMakesItUnavailable()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId, isAvailable: true);

        Assert.True(await Foods.DeleteFoodAsync(food.FoodId));

        var stored = Db.Foods.Single(x => x.FoodId == food.FoodId);
        Assert.False(stored.IsActive);
        Assert.False(stored.IsAvailable);
        Assert.False(await Foods.DeleteFoodAsync(999999));
    }

    // ============================ CATEGORY ============================

    [Fact]
    public async Task Category_Crud_RoundTrip()
    {
        Assert.True(await Categories.CreateCategoryAsync(new CategoryDTO
        {
            Name = "Drinks",
            Description = "beverages",
            DisplayOrder = false
        }));

        var all = await Categories.GetAllCategoriesAsync();
        var created = Assert.Single(all);
        Assert.Equal("Drinks", created.Name);
        Assert.True(created.IsActive);

        Assert.Equal("Drinks", (await Categories.GetCategoryByIdAsync(created.Id)).Name);
        Assert.Single(await Categories.GetCategoriesByNameAsync("rin"));
        Assert.Empty(await Categories.GetCategoriesByNameAsync("nothing"));

        Assert.True(await Categories.UpdateCategoryAsync(new CategoryDTO
        {
            Id = created.Id,
            Name = "Cold Drinks",
            Description = "d",
            DisplayOrder = false,
            IsActive = false
        }));
        Assert.False((await Categories.GetCategoryByIdAsync(created.Id)).IsActive);

        Assert.True(await Categories.DeleteCategoryAsync(created.Id));
        Assert.False((await Categories.GetCategoryByIdAsync(created.Id)).IsActive);
    }

    [Fact]
    public async Task Category_Throws_WhenEntityIsMissing()
    {
        var ex = await Assert.ThrowsAsync<Exception>(() => Categories.GetCategoryByIdAsync(999999));
        Assert.Equal("Category not found", ex.Message);

        await Assert.ThrowsAsync<ArgumentNullException>(() => Categories.CreateCategoryAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Categories.UpdateCategoryAsync(null!));
        await Assert.ThrowsAsync<Exception>(() => Categories.DeleteCategoryAsync(999999));
    }

    // ============================ FOOD SIZE ============================

    [Fact]
    public async Task FoodSize_Create_Throws_WhenFoodMissing()
    {
        var ex = await Assert.ThrowsAsync<Exception>(() => FoodSizes.CreateAsync(999999, new CreateFoodSizeRequest { SizeName = "M", SizePrice = 10m }));
        Assert.Equal("Food not found", ex.Message);
    }

    [Fact]
    public async Task FoodSize_Create_RejectsDuplicateNameForSameFood()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        SeedSize(food.FoodId, 10m, name: "M");

        var ex = await Assert.ThrowsAsync<Exception>(() => FoodSizes.CreateAsync(food.FoodId, new CreateFoodSizeRequest { SizeName = "M", SizePrice = 12m }));
        Assert.Equal("This size already exists for this food", ex.Message);
    }

    [Fact]
    public async Task FoodSize_Create_WithDefault_RemovesPreviousDefault()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var originalDefault = SeedSize(food.FoodId, 10m, isDefault: true, name: "M");

        // Simulate a fresh request scope: the service reads with AsNoTracking and re-attaches.
        Db.ChangeTracker.Clear();

        var created = await FoodSizes.CreateAsync(food.FoodId, new CreateFoodSizeRequest
        {
            SizeName = "L",
            SizePrice = 15m,
            IsDefault = true
        });

        Assert.True(created.IsDefault);
        Assert.False(Db.FoodSizes.Single(x => x.SizeId == originalDefault.SizeId).IsDefault);
    }

    [Fact]
    public async Task FoodSize_GetByFoodId_ThrowsWhenFoodMissing_AndSkipsInactive()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        SeedSize(food.FoodId, 10m, name: "M");
        var inactive = SeedSize(food.FoodId, 11m, name: "X");
        inactive.IsActive = false;
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<Exception>(() => FoodSizes.GetByFoodIdAsync(999999));
        Assert.Equal("Food not found", ex.Message);

        var sizes = await FoodSizes.GetByFoodIdAsync(food.FoodId);
        Assert.Single(sizes);
        Assert.Equal("M", sizes[0].SizeName);
    }

    [Fact]
    public async Task FoodSize_GetById_ReturnsNull_WhenFoodDoesNotMatch()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var otherFood = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, 10m);

        Assert.NotNull(await FoodSizes.GetByIdAsync(food.FoodId, size.SizeId));
        Assert.Null(await FoodSizes.GetByIdAsync(otherFood.FoodId, size.SizeId));
        Assert.Null(await FoodSizes.GetByIdAsync(food.FoodId, 999999));
    }

    [Fact]
    public async Task FoodSize_Update_RejectsDuplicate_AndReturnsNullWhenMissing()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        SeedSize(food.FoodId, 10m, name: "M");
        var large = SeedSize(food.FoodId, 15m, name: "L");

        Assert.Null(await FoodSizes.UpdateAsync(food.FoodId, 999999, new UpdateFoodSizeRequest { SizeName = "X", SizePrice = 1m }));

        var ex = await Assert.ThrowsAsync<Exception>(() => FoodSizes.UpdateAsync(food.FoodId, large.SizeId, new UpdateFoodSizeRequest
        {
            SizeName = "M",
            SizePrice = 15m
        }));
        Assert.Equal("This size already exists for this food", ex.Message);
    }

    [Fact]
    public async Task FoodSize_SetDefault_And_Delete_BehaveAsExpected()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var small = SeedSize(food.FoodId, 10m, isDefault: true, name: "S");
        var large = SeedSize(food.FoodId, 20m, isDefault: false, name: "L");

        // Each act runs against a fresh tracker, mirroring one request scope per operation.
        Db.ChangeTracker.Clear();

        Assert.True(await FoodSizes.SetDefaultAsync(food.FoodId, large.SizeId));
        using (var checkAfterDefault = NewContext())
        {
            Assert.True(checkAfterDefault.FoodSizes.Single(x => x.SizeId == large.SizeId).IsDefault);
            Assert.False(checkAfterDefault.FoodSizes.Single(x => x.SizeId == small.SizeId).IsDefault);
        }

        Db.ChangeTracker.Clear();
        Assert.True(await FoodSizes.DeleteAsync(food.FoodId, large.SizeId));
        using (var checkAfterDelete = NewContext())
        {
            var deleted = checkAfterDelete.FoodSizes.Single(x => x.SizeId == large.SizeId);
            Assert.False(deleted.IsActive);
            Assert.False(deleted.IsDefault);
        }

        Assert.False(await FoodSizes.DeleteAsync(food.FoodId, 999999));
        Assert.False(await FoodSizes.SetDefaultAsync(food.FoodId, 999999));
    }

    // ============================ TOPPING ============================

    [Fact]
    public async Task Topping_Create_RejectsDuplicateName()
    {
        await Toppings.CreateAsync(new CreateToppingRequest { ToppingName = "Cheese", ToppingPrice = 5m });

        var ex = await Assert.ThrowsAsync<Exception>(() => Toppings.CreateAsync(new CreateToppingRequest
        {
            ToppingName = "Cheese",
            ToppingPrice = 6m
        }));
        Assert.Equal("Topping already exists", ex.Message);
    }

    [Fact]
    public async Task Topping_Update_RejectsDuplicateName_AndReturnsNullWhenMissing()
    {
        await Toppings.CreateAsync(new CreateToppingRequest { ToppingName = "A", ToppingPrice = 1m });
        var second = await Toppings.CreateAsync(new CreateToppingRequest { ToppingName = "B", ToppingPrice = 2m });

        Assert.Null(await Toppings.UpdateAsync(999999, new UpdateToppingRequest { ToppingName = "C", ToppingPrice = 1m }));

        var ex = await Assert.ThrowsAsync<Exception>(() => Toppings.UpdateAsync(second.ToppingId, new UpdateToppingRequest
        {
            ToppingName = "A",
            ToppingPrice = 2m
        }));
        Assert.Equal("Topping name already exists", ex.Message);
    }

    [Fact]
    public async Task Topping_Availability_And_SoftDelete()
    {
        var topping = await Toppings.CreateAsync(new CreateToppingRequest { ToppingName = "Cheese", ToppingPrice = 5m, IsAvailable = true });

        Assert.True(await Toppings.UpdateAvailabilityAsync(topping.ToppingId, false));
        Assert.False(Db.Toppings.Single(x => x.ToppingId == topping.ToppingId).IsAvailable);
        Assert.False(await Toppings.UpdateAvailabilityAsync(999999, true));

        Assert.True(await Toppings.DeleteAsync(topping.ToppingId));
        var stored = Db.Toppings.Single(x => x.ToppingId == topping.ToppingId);
        Assert.False(stored.IsActive);
        Assert.False(stored.IsAvailable);
        Assert.False(await Toppings.DeleteAsync(999999));
    }

    [Fact]
    public async Task Topping_GetAll_ExcludesInactive_UnlessRequested()
    {
        var active = await Toppings.CreateAsync(new CreateToppingRequest { ToppingName = "Active", ToppingPrice = 1m });
        await Toppings.DeleteAsync(active.ToppingId);
        var visible = await Toppings.CreateAsync(new CreateToppingRequest { ToppingName = "Visible", ToppingPrice = 1m });

        var visibleOnly = await Toppings.GetAllAsync();
        var all = await Toppings.GetAllAsync(includeInactive: true);

        Assert.Contains(visibleOnly, x => x.ToppingId == visible.ToppingId);
        Assert.DoesNotContain(visibleOnly, x => x.ToppingId == active.ToppingId);
        Assert.Contains(all, x => x.ToppingId == active.ToppingId);
        Assert.Null(await Toppings.GetByIdAsync(999999));
    }

    [Fact]
    public async Task Topping_CannotBeRecreatedWithSameNameAfterSoftDelete_UniqueIndexStillApplies()
    {
        // Baseline behaviour: the unique index on toppings.ToppingName is NOT filtered by IsActive,
        // so a soft-deleted topping still blocks recreating the same name at the database level.
        var topping = await Toppings.CreateAsync(new CreateToppingRequest { ToppingName = "Recycled", ToppingPrice = 1m });
        await Toppings.DeleteAsync(topping.ToppingId);

        await Assert.ThrowsAsync<DbUpdateException>(() => Toppings.CreateAsync(new CreateToppingRequest
        {
            ToppingName = "Recycled",
            ToppingPrice = 2m
        }));
    }

    // ============================ FOOD TOPPING ============================

    [Fact]
    public async Task FoodTopping_Get_ThrowsWhenFoodMissing_AndReturnsEmptyWhenUnassigned()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);

        var ex = await Assert.ThrowsAsync<Exception>(() => FoodToppings.GetToppingsByFoodIdAsync(999999));
        Assert.Equal("Food not found", ex.Message);

        Assert.Empty(await FoodToppings.GetToppingsByFoodIdAsync(food.FoodId));
    }

    [Fact]
    public async Task FoodTopping_Assign_ValidatesFoodTopping_AndDuplicates()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var topping = SeedTopping();

        await Assert.ThrowsAsync<Exception>(() => FoodToppings.AssignAsync(999999, topping.ToppingId));
        await Assert.ThrowsAsync<Exception>(() => FoodToppings.AssignAsync(food.FoodId, 999999));

        var inactive = SeedTopping(isActive: false);
        await Assert.ThrowsAsync<Exception>(() => FoodToppings.AssignAsync(food.FoodId, inactive.ToppingId));

        Assert.True(await FoodToppings.AssignAsync(food.FoodId, topping.ToppingId));
        await Assert.ThrowsAsync<Exception>(() => FoodToppings.AssignAsync(food.FoodId, topping.ToppingId));

        var result = await FoodToppings.GetToppingsByFoodIdAsync(food.FoodId);
        Assert.Single(result);
        Assert.Equal(topping.ToppingId, result[0].ToppingId);
    }

    [Fact]
    public async Task FoodTopping_Get_FiltersOutInactiveToppings()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var topping = SeedTopping();
        Db.FoodToppings.Add(new FoodTopping { FoodId = food.FoodId, ToppingId = topping.ToppingId });
        Db.SaveChanges();
        topping.IsActive = false;
        Db.SaveChanges();

        Assert.Empty(await FoodToppings.GetToppingsByFoodIdAsync(food.FoodId));
    }

    [Fact]
    public async Task FoodTopping_Remove_ReturnsTrueThenFalse()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var topping = SeedTopping();
        Db.FoodToppings.Add(new FoodTopping { FoodId = food.FoodId, ToppingId = topping.ToppingId });
        Db.SaveChanges();
        Db.ChangeTracker.Clear();

        Assert.True(await FoodToppings.RemoveAsync(food.FoodId, topping.ToppingId));
        Assert.False(await FoodToppings.RemoveAsync(food.FoodId, topping.ToppingId));
    }
}
