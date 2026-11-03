using System.Text.Json;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// C4: C# property được chuẩn hoá về <c>Quanlity</c> theo quy ước project/DB,
/// nhưng JSON contract của các endpoint cũ vẫn phải giữ <c>quantity</c> cho frontend.
/// </summary>
public sealed class DtoJsonContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void OrderItemDetail_SerializesQuantity_FromQuanlityProperty()
    {
        var json = JsonSerializer.Serialize(new OrderItemDetailResponse { Quanlity = 3 }, Web);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("quantity").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("quanlity", out _));
    }

    [Fact]
    public void KitchenItem_SerializesBothLegacyQuanlity_AndQuantityAlias()
    {
        var json = JsonSerializer.Serialize(new KitchenItemResponse { Quanlity = 4 }, Web);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(4, doc.RootElement.GetProperty("quanlity").GetInt32());
        Assert.Equal(4, doc.RootElement.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public void CreateOrderItem_DeserializesQuantityIntoQuanlity()
    {
        var item = JsonSerializer.Deserialize<CreateOrderItemRequest>("""{"sizeId":1,"quantity":2}""", Web);

        Assert.NotNull(item);
        Assert.Equal(2, item!.Quanlity);
    }

    [Fact]
    public void RecipeIngredient_DeserializesQuantityIntoQuanlity()
    {
        var item = JsonSerializer.Deserialize<RecipeIngredientRequest>("""{"ingredientId":1,"quantity":2.5}""", Web);

        Assert.NotNull(item);
        Assert.Equal(2.5m, item!.Quanlity);
    }

    [Fact]
    public void OrderCreatedResponse_CoversEveryOrderField_ExceptRowVersion()
    {
        var expected = typeof(Order).GetProperties()
            .Select(p => p.Name)
            .Where(name => name != nameof(Order.RowVersion))
            .ToList();

        var json = JsonSerializer.Serialize(new OrderCreatedResponse(), Web);
        using var doc = JsonDocument.Parse(json);

        foreach (var name in expected)
        {
            var camel = char.ToLowerInvariant(name[0]) + name[1..];
            Assert.True(doc.RootElement.TryGetProperty(camel, out _), $"Thiếu field '{camel}' so với entity Order.");
        }

        Assert.False(doc.RootElement.TryGetProperty("rowVersion", out _));
    }

    [Fact]
    public void UserProfileResponse_NeverSerializesPassword()
    {
        var json = JsonSerializer.Serialize(new UserProfileResponse
        {
            UserId = 7,
            UserName = "profile-user",
            FullName = "Profile User",
            Email = "profile@example.test",
            Roles = [AuthPolicies.Roles.Waiter]
        }, Web);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(7, doc.RootElement.GetProperty("userId").GetInt32());
        Assert.Equal("profile-user", doc.RootElement.GetProperty("userName").GetString());
        Assert.Equal(AuthPolicies.Roles.Waiter, doc.RootElement.GetProperty("roles")[0].GetString());
        Assert.False(doc.RootElement.TryGetProperty("password", out _));
        Assert.False(doc.RootElement.TryGetProperty("userPassword", out _));
    }

    [Fact]
    public void PaymentResponse_CoversEveryPaymentField()
    {
        var expected = typeof(Payment).GetProperties().Select(p => p.Name);

        var json = JsonSerializer.Serialize(new PaymentResponse(), Web);
        using var doc = JsonDocument.Parse(json);

        foreach (var name in expected)
        {
            var camel = char.ToLowerInvariant(name[0]) + name[1..];
            Assert.True(doc.RootElement.TryGetProperty(camel, out _), $"Thiếu field '{camel}' so với entity Payment.");
        }
    }
}
