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
}
