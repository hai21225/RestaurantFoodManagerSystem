using System.Collections;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>Pins the public menu projection (active categories, available foods, active sizes, assignable toppings).</summary>
public sealed class MenuTests : TestBase
{
    private static IReadOnlyList<object> AsList(object value)
    {
        var items = new List<object>();
        foreach (var item in (IEnumerable)value)
        {
            items.Add(item!);
        }
        return items;
    }

    private static object Prop(object target, string name) =>
        target.GetType().GetProperty(name)!.GetValue(target)!;

    private static IReadOnlyList<object> FoodsOf(object category) => AsList(Prop(category, "Foods"));

    private static IReadOnlyList<object> ToppingsOf(object food) => AsList(Prop(food, "Toppings"));

    [Fact]
    public async Task Menu_ReturnsOnlyActiveAndAvailableData()
    {
        var visibleCategory = SeedCategory();
        var hiddenCategory = SeedCategory();
        hiddenCategory.IsActive = false;

        var visibleFood = SeedFood(visibleCategory.CategoryId, price: 20m, isActive: true, isAvailable: true);
        SeedFood(visibleCategory.CategoryId, isActive: true, isAvailable: false);
        SeedFood(visibleCategory.CategoryId, isActive: false, isAvailable: true);

        var visibleSize = SeedSize(visibleFood.FoodId, 20m, name: "M");
        var hiddenSize = SeedSize(visibleFood.FoodId, 25m, name: "L");
        hiddenSize.IsActive = false;

        var visibleTopping = SeedTopping(price: 3m);
        var unassignedTopping = SeedTopping(price: 4m);
        Db.FoodToppings.Add(new FoodTopping { FoodId = visibleFood.FoodId, ToppingId = visibleTopping.ToppingId });
        Db.SaveChanges();

        var menu = await Menu.GetMenuAsync(CancellationToken.None);
        var categories = AsList(menu);

        var category = Assert.Single(categories);
        Assert.Equal(visibleCategory.CategoryId, (int)Prop(category, "CategoryId"));
        Assert.NotEqual(hiddenCategory.CategoryId, (int)Prop(category, "CategoryId"));

        var food = Assert.Single(FoodsOf(category));
        Assert.Equal(visibleFood.FoodId, (int)Prop(food, "FoodId"));
        Assert.Equal(20m, (decimal)Prop(food, "FoodPrice"));

        var size = Assert.Single(AsList(Prop(food, "Sizes")));
        Assert.Equal(visibleSize.SizeId, (int)Prop(size, "SizeId"));
        Assert.NotEqual(hiddenSize.SizeId, (int)Prop(size, "SizeId"));

        var toppings = ToppingsOf(food);
        Assert.Single(toppings);
        Assert.Equal(visibleTopping.ToppingId, (int)Prop(toppings[0], "ToppingId"));
        Assert.DoesNotContain(toppings, x => (int)Prop(x, "ToppingId") == unassignedTopping.ToppingId);
    }

    [Fact]
    public async Task Menu_ExcludesUnavailableAndInactiveToppings()
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var inactive = SeedTopping(isActive: false);
        var unavailable = SeedTopping(isAvailable: false);
        Db.FoodToppings.Add(new FoodTopping { FoodId = food.FoodId, ToppingId = inactive.ToppingId });
        Db.FoodToppings.Add(new FoodTopping { FoodId = food.FoodId, ToppingId = unavailable.ToppingId });
        Db.SaveChanges();

        var menu = await Menu.GetMenuAsync(CancellationToken.None);
        var onlyFood = Assert.Single(FoodsOf(Assert.Single(AsList(menu))));

        Assert.Empty(ToppingsOf(onlyFood));
    }

    [Fact]
    public async Task Menu_OrdersCategoriesByDisplayOrderThenName()
    {
        var bravo = SeedCategory(name: "Bravo");
        var alpha = SeedCategory(name: "Alpha");
        bravo.DisplayOrder = true;  // DisplayOrder is modelled as bool today
        alpha.DisplayOrder = false;
        Db.SaveChanges();

        var menu = await Menu.GetMenuAsync(CancellationToken.None);

        Assert.Equal(new[] { "Alpha", "Bravo" }, AsList(menu).Select(x => (string)Prop(x, "CategoryName")));
    }
}
