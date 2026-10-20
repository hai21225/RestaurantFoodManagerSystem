using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// P1 — lỗi nghiệp vụ còn lại sau đợt auth:
/// - không được đặt món thuộc food đã ngừng bán / soft-delete;
/// - trùng tên nguyên liệu phải trả 409 thay vì 500.
/// </summary>
public sealed class P1OrderAvailabilityTests : TestBase
{
    [Theory]
    [InlineData(false, true)]  // food soft-delete, size vẫn active
    [InlineData(true, false)]  // food còn active nhưng đang ngừng bán
    public async Task CreateOrder_RejectsFoodThatIsInactiveOrUnavailable(
        bool isActive,
        bool isAvailable)
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId, isActive: isActive, isAvailable: isAvailable);
        var size = SeedSize(food.FoodId, price: 50m);
        var table = SeedTable();
        var staff = SeedUser();

        var request = new CreateOrderRequest
        {
            TableId = table.TableId,
            CreatedBy = staff.Id
        };
        request.Items.Add(new CreateOrderItemRequest
        {
            SizeId = size.SizeId,
            Quanlity = 1
        });

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => Orders.CreateAsync(request, CancellationToken.None));

        Assert.Contains("no longer available", ex.Message);
        Assert.Empty(Db.Orders);
        Assert.Empty(Db.KitchenOrders);
    }
}

public sealed class P1IngredientDuplicateTests : TestBase
{
    [Fact]
    public async Task Create_DuplicateActiveName_ThrowsInvalidOperation()
    {
        var existing = SeedIngredient(stock: 0m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Ingredients.CreateAsync(
                new IngredientRequest
                {
                    Name = existing.Name,
                    Unit = "kg",
                    MinimumStock = 0m
                },
                CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Single(Db.Ingredients.Where(x => x.Name == existing.Name));
    }

    [Fact]
    public async Task Update_ToNameOfAnotherActiveIngredient_ThrowsInvalidOperation()
    {
        var first = SeedIngredient(stock: 0m);
        var second = SeedIngredient(stock: 0m);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Ingredients.UpdateAsync(
                second.IngredientId,
                new IngredientRequest
                {
                    Name = first.Name,
                    Unit = second.Unit,
                    MinimumStock = second.MinimumStock
                },
                CancellationToken.None));

        Assert.Contains("already exists", ex.Message);

        using var check = NewContext();
        Assert.Equal(second.Name, check.Ingredients.Single(x => x.IngredientId == second.IngredientId).Name);
        Assert.Single(check.Ingredients.Where(x => x.Name == first.Name));
    }
}

public sealed class P1IngredientHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public P1IngredientHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateDuplicateIngredient_ReturnsConflict()
    {
        var name = $"P1 ingredient {Guid.NewGuid():N}";
        var request = new IngredientRequest
        {
            Name = name,
            Unit = "kg",
            MinimumStock = 0m
        };

        using var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/management/ingredients", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/management/ingredients", request);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}

/// <summary>Kiểm tra DataAnnotations của các request từng làm API trả 500 do vượt độ dài cột.</summary>
public sealed class P1RequestValidationTests
{
    private static List<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void OrderItemNote_Over500_IsRejected()
    {
        var request = new CreateOrderItemRequest
        {
            SizeId = 1,
            Quanlity = 1,
            Note = new string('x', 501)
        };

        Assert.Contains(Validate(request), x => x.MemberNames.Contains(nameof(CreateOrderItemRequest.Note)));
    }

    [Fact]
    public void OrderItemNote_At500_IsAccepted()
    {
        var request = new CreateOrderItemRequest
        {
            SizeId = 1,
            Quanlity = 1,
            Note = new string('x', 500)
        };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void KitchenItemReason_Over500_IsRejected()
    {
        var request = new KitchenItemStatusRequest
        {
            Status = "Cancelled",
            Reason = new string('x', 501)
        };

        Assert.Contains(Validate(request), x => x.MemberNames.Contains(nameof(KitchenItemStatusRequest.Reason)));
    }

    [Theory]
    [InlineData("hết")]
    [InlineData("khách")]
    public void CancelItemReason_ShortButNonEmpty_IsAccepted(string reason)
    {
        var request = new CancelOrderItemRequest { Reason = reason, ChangedBy = 1 };

        Assert.Empty(Validate(request));
    }

    [Fact]
    public void CancelItemReason_Over500_IsRejected()
    {
        var request = new CancelOrderItemRequest { Reason = new string('x', 501) };

        Assert.Contains(Validate(request), x => x.MemberNames.Contains(nameof(CancelOrderItemRequest.Reason)));
    }
}
