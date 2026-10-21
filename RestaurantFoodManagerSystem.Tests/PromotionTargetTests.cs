using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// C-18 (gap-review C4) — API quản lý **target** của khuyến mãi.
///
/// <para>
/// Trước C-18: <c>promotion_foods</c> / <c>promotion_categories</c> được đọc bởi
/// <c>OrderService.ResolveDiscountBaseAsync</c> nhưng **không API nào ghi** ⇒ hai bảng
/// luôn rỗng ⇒ mọi khuyến mãi âm thầm giảm trên **toàn bộ** hoá đơn.
/// </para>
///
/// <para>
/// Nay đã có đường ghi ⇒ cơ chế targeting thực sự chạy được: khuyến mãi chỉ giảm trên
/// những dòng món thuộc phạm vi target.
/// </para>
/// </summary>
public sealed class PromotionTargetTests : TestBase
{
    /// <summary>Một món độc lập: danh mục riêng + size riêng + công thức riêng (bắt buộc có công thức).</summary>
    private (FoodSize Size, Category Category) SeedDish(decimal price)
    {
        var category = SeedCategory($"cat-{Guid.NewGuid():N}");
        var food = SeedFood(category.CategoryId, price);
        var size = SeedSize(food.FoodId, price);
        var ingredient = SeedIngredient(100m);
        SeedRecipe(size.SizeId, (ingredient.IngredientId, 2m));
        return (size, category);
    }

    private async Task<Promotion> CreateActivePromotionAsync(
        string discountType = "Percent",
        decimal discountValue = 10m)
    {
        var nowVn = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        return await Promotions.CreateAsync(new PromotionRequest
        {
            Name = "Khuyến mãi test",
            DiscountType = discountType,
            DiscountValue = discountValue,
            StartAt = nowVn.AddDays(-1),
            EndAt = nowVn.AddDays(1),
            MinimumOrderAmount = 0m,
            MaxDiscountAmount = 0m
        }, CancellationToken.None);
    }

    // ==========================================================
    // Ghi / đọc target
    // ==========================================================

    [Fact]
    public async Task SetTargets_LinksSizesAndCategories()
    {
        var dish = SeedDish(100m);
        var other = SeedDish(200m);
        var promotion = await CreateActivePromotionAsync();

        Assert.True(await Promotions.SetTargetsAsync(promotion.Id, new PromotionTargetsRequest
        {
            SizeIds = [dish.Size.SizeId],
            CategoryIds = [other.Category.CategoryId]
        }, CancellationToken.None));

        var foodRow = Assert.Single(Db.PromotionFoods.Where(x => x.PromotionId == promotion.Id));
        Assert.Equal(dish.Size.SizeId, foodRow.SizeId);

        var categoryRow = Assert.Single(Db.PromotionCategories.Where(x => x.PromotionId == promotion.Id));
        Assert.Equal(other.Category.CategoryId, categoryRow.CategoryId);
    }

    [Fact]
    public async Task SetTargets_ReplacesPreviousTargets_WithoutDuplicates()
    {
        var first = SeedDish(100m);
        var second = SeedDish(200m);
        var promotion = await CreateActivePromotionAsync();

        await Promotions.SetTargetsAsync(promotion.Id, new PromotionTargetsRequest
        {
            SizeIds = [first.Size.SizeId]
        }, CancellationToken.None);

        // Gửi lại cùng danh sách (lặp lại) ⇒ không được sinh dòng trùng.
        await Promotions.SetTargetsAsync(promotion.Id, new PromotionTargetsRequest
        {
            SizeIds = [first.Size.SizeId, first.Size.SizeId]
        }, CancellationToken.None);

        Assert.Single(Db.PromotionFoods.Where(x => x.PromotionId == promotion.Id));

        // Gửi danh sách khác ⇒ thay thế, không cộng dồn.
        await Promotions.SetTargetsAsync(promotion.Id, new PromotionTargetsRequest
        {
            SizeIds = [second.Size.SizeId]
        }, CancellationToken.None);

        var rows = Db.PromotionFoods.Where(x => x.PromotionId == promotion.Id).ToList();
        Assert.Single(rows);
        Assert.Equal(second.Size.SizeId, rows[0].SizeId);
    }

    [Fact]
    public async Task SetTargets_EmptyLists_ClearsTargets_SoPromotionAppliesToWholeBill()
    {
        var dish = SeedDish(100m);
        var promotion = await CreateActivePromotionAsync();

        await Promotions.SetTargetsAsync(promotion.Id, new PromotionTargetsRequest
        {
            SizeIds = [dish.Size.SizeId]
        }, CancellationToken.None);
        Assert.Single(Db.PromotionFoods);

        await Promotions.SetTargetsAsync(promotion.Id, new PromotionTargetsRequest(), CancellationToken.None);

        Assert.Empty(Db.PromotionFoods);
        Assert.Empty(Db.PromotionCategories);
    }

    [Fact]
    public async Task SetTargets_UnknownSize_IsRejected()
    {
        var promotion = await CreateActivePromotionAsync();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => Promotions.SetTargetsAsync(
            promotion.Id,
            new PromotionTargetsRequest { SizeIds = [987654] },
            CancellationToken.None));

        Assert.Contains("Food size not found", ex.Message);
        Assert.Empty(Db.PromotionFoods);
    }

    [Fact]
    public async Task SetTargets_UnknownCategory_IsRejected()
    {
        var promotion = await CreateActivePromotionAsync();

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() => Promotions.SetTargetsAsync(
            promotion.Id,
            new PromotionTargetsRequest { CategoryIds = [987654] },
            CancellationToken.None));

        Assert.Contains("Category not found", ex.Message);
        Assert.Empty(Db.PromotionCategories);
    }

    [Fact]
    public async Task SetTargets_UnknownPromotion_ReturnsFalse()
    {
        Assert.False(await Promotions.SetTargetsAsync(
            987654,
            new PromotionTargetsRequest { SizeIds = [1] },
            CancellationToken.None));
    }

    [Fact]
    public async Task GetTargets_ReturnsTargetsPerPromotion()
    {
        var dish = SeedDish(100m);
        var other = SeedDish(200m);
        var first = await CreateActivePromotionAsync();
        var second = await CreateActivePromotionAsync();

        await Promotions.SetTargetsAsync(first.Id, new PromotionTargetsRequest
        {
            SizeIds = [dish.Size.SizeId],
            CategoryIds = [other.Category.CategoryId]
        }, CancellationToken.None);

        var targets = await Promotions.GetTargetsAsync([first.Id, second.Id], CancellationToken.None);

        Assert.Equal([dish.Size.SizeId], targets[first.Id].SizeIds);
        Assert.Equal([other.Category.CategoryId], targets[first.Id].CategoryIds);

        // Khuyến mãi chưa có target ⇒ danh sách rỗng (áp cho toàn bộ hoá đơn).
        Assert.Empty(targets[second.Id].SizeIds);
        Assert.Empty(targets[second.Id].CategoryIds);
    }

    // ==========================================================
    // Tác động thật: giảm giá chỉ trong phạm vi target
    // ==========================================================

    [Fact]
    public async Task ApplyPromotion_WithSizeTarget_DiscountsOnlyTargetedLine()
    {
        var dishA = SeedDish(100m);
        var dishB = SeedDish(200m);
        var table = SeedTable();
        var staff = SeedUser();

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = table.TableId,
            CreatedBy = staff.Id,
            Items =
            [
                new CreateOrderItemRequest { SizeId = dishA.Size.SizeId, Quanlity = 1 },
                new CreateOrderItemRequest { SizeId = dishB.Size.SizeId, Quanlity = 1 }
            ]
        }, CancellationToken.None);

        Assert.Equal(300m, order.Subtotal);

        var promotion = await CreateActivePromotionAsync(discountType: "Percent", discountValue: 10m);
        await Promotions.SetTargetsAsync(promotion.Id, new PromotionTargetsRequest
        {
            SizeIds = [dishA.Size.SizeId]
        }, CancellationToken.None);

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);

        // Chỉ dòng món A (100) được giảm 10% ⇒ giảm 10, không phải 10% của 300.
        Assert.Equal(10m, saved.DiscountAmount);
        Assert.Equal(300m, saved.Subtotal);
    }

    [Fact]
    public async Task ApplyPromotion_WithCategoryTarget_DiscountsWholeCategory()
    {
        var dishA = SeedDish(100m);
        var dishB = SeedDish(200m);
        var table = SeedTable();
        var staff = SeedUser();

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = table.TableId,
            CreatedBy = staff.Id,
            Items =
            [
                new CreateOrderItemRequest { SizeId = dishA.Size.SizeId, Quanlity = 1 },
                new CreateOrderItemRequest { SizeId = dishB.Size.SizeId, Quanlity = 1 }
            ]
        }, CancellationToken.None);

        var promotion = await CreateActivePromotionAsync(discountType: "Percent", discountValue: 10m);
        await Promotions.SetTargetsAsync(promotion.Id, new PromotionTargetsRequest
        {
            CategoryIds = [dishB.Category.CategoryId]
        }, CancellationToken.None);

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);

        // Target theo danh mục B (200) ⇒ giảm 20.
        Assert.Equal(20m, saved.DiscountAmount);
    }

    [Fact]
    public async Task ApplyPromotion_WithoutTargets_StillDiscountsWholeBill()
    {
        // Hành vi cũ (không có target) phải giữ nguyên: giảm trên toàn bộ hoá đơn.
        var dishA = SeedDish(100m);
        var dishB = SeedDish(200m);
        var table = SeedTable();
        var staff = SeedUser();

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = table.TableId,
            CreatedBy = staff.Id,
            Items =
            [
                new CreateOrderItemRequest { SizeId = dishA.Size.SizeId, Quanlity = 1 },
                new CreateOrderItemRequest { SizeId = dishB.Size.SizeId, Quanlity = 1 }
            ]
        }, CancellationToken.None);

        var promotion = await CreateActivePromotionAsync(discountType: "Percent", discountValue: 10m);

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);

        Assert.Equal(30m, saved.DiscountAmount);
    }

    [Fact]
    public async Task ApplyPromotion_TargetNotInBill_RejectsPromotion()
    {
        // Target không khớp dòng món nào ⇒ không có gì để giảm ⇒ không áp khuyến mãi.
        var dishA = SeedDish(100m);
        var other = SeedDish(500m);
        var table = SeedTable();
        var staff = SeedUser();

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = table.TableId,
            CreatedBy = staff.Id,
            Items = [new CreateOrderItemRequest { SizeId = dishA.Size.SizeId, Quanlity = 1 }]
        }, CancellationToken.None);

        var promotion = await CreateActivePromotionAsync(discountType: "Percent", discountValue: 10m);
        await Promotions.SetTargetsAsync(promotion.Id, new PromotionTargetsRequest
        {
            SizeIds = [other.Size.SizeId]
        }, CancellationToken.None);

        Assert.False(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));

        using var check = NewContext();
        var saved = check.Orders.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(0m, saved.DiscountAmount);
        Assert.Null(saved.PromotionId);
    }
}

/// <summary>
/// C-18 — hợp đồng HTTP: gắn target khuyến mãi và đọc lại target.
/// </summary>
public sealed class PromotionTargetHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public PromotionTargetHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    private (int PromotionId, int SizeId, int CategoryId) SeedScenario()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var suffix = Guid.NewGuid().ToString("N");

        var category = new Category
        {
            CategoryName = $"cat-{suffix}",
            CategoryDescription = "desc",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Categories.Add(category);
        db.SaveChanges();

        var food = new Food
        {
            CategoryId = category.CategoryId,
            FoodName = $"food-{suffix}",
            FoodDescription = "desc",
            FoodImageUrl = "img.png",
            FoodPrice = 100m,
            IsAvailable = true,
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
            SizePrice = 100m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.FoodSizes.Add(size);
        db.SaveChanges();

        var nowVn = DateTime.SpecifyKind(DateTime.UtcNow.AddHours(7), DateTimeKind.Unspecified);
        var promotion = new Promotion
        {
            Name = $"promo-{suffix}",
            Description = "",
            DiscountType = "Percent",
            DiscountValue = 10m,
            StartAt = nowVn.AddDays(-1).AddHours(-7),
            EndAt = nowVn.AddDays(1).AddHours(-7),
            MinimumOrderAmount = 0m,
            MaxDiscountAmount = 0m,
            IsActive = true
        };
        db.Promotions.Add(promotion);
        db.SaveChanges();

        return (promotion.Id, size.SizeId, category.CategoryId);
    }

    [Fact]
    public async Task PutPromotionTargets_SetsTargets_AndGetReturnsThem()
    {
        var (promotionId, sizeId, categoryId) = SeedScenario();
        using var client = _factory.CreateClient();

        var put = await client.PutAsJsonAsync($"/api/management/promotions/{promotionId}/targets", new
        {
            sizeIds = new[] { sizeId },
            categoryIds = new[] { categoryId }
        });

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        var get = await client.GetFromJsonAsync<JsonElement>($"/api/management/promotions/{promotionId}");
        Assert.Equal(new[] { sizeId }, get.GetProperty("sizeIds").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Equal(new[] { categoryId }, get.GetProperty("categoryIds").EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public async Task PutPromotionTargets_UnknownSize_ReturnsNotFoundWithMessage()
    {
        var (promotionId, _, _) = SeedScenario();
        using var client = _factory.CreateClient();

        var put = await client.PutAsJsonAsync($"/api/management/promotions/{promotionId}/targets", new
        {
            sizeIds = new[] { 987654 },
            categoryIds = Array.Empty<int>()
        });

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var body = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Food size not found", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PutPromotionTargets_UnknownPromotion_ReturnsNotFound()
    {
        using var client = _factory.CreateClient();

        var put = await client.PutAsJsonAsync("/api/management/promotions/987654/targets", new
        {
            sizeIds = Array.Empty<int>(),
            categoryIds = Array.Empty<int>()
        });

        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
    }

    [Fact]
    public async Task GetPromotions_IncludesTargets()
    {
        var (promotionId, sizeId, _) = SeedScenario();
        using var client = _factory.CreateClient();

        await client.PutAsJsonAsync($"/api/management/promotions/{promotionId}/targets", new
        {
            sizeIds = new[] { sizeId },
            categoryIds = Array.Empty<int>()
        });

        var list = await client.GetFromJsonAsync<JsonElement>("/api/management/promotions");
        var item = list.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == promotionId);

        Assert.Equal(new[] { sizeId }, item.GetProperty("sizeIds").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Empty(item.GetProperty("categoryIds").EnumerateArray());
    }
}
