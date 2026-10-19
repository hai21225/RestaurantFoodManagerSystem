using System.Text.Json.Serialization;

/// <summary>
/// Kết quả của <c>GET /api/management/recipes/{id}</c>.
/// Nhóm C/GĐ7 bổ sung <see cref="FoodName"/>/<see cref="SizeName"/> đã join để tiêu đề chi tiết
/// công thức hiển thị đúng tên món/cỡ (trước đây frontend nhận "undefined").
/// </summary>
public sealed class RecipeResponse
{
    public int RecipeId { get; set; }
    public int SizeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string SizeName { get; set; } = string.Empty;
    public List<RecipeIngredientResponse> Ingredients { get; set; } = [];
}

/// <summary>
/// Dòng nguyên liệu trong <see cref="RecipeResponse"/>.
/// </summary>
public sealed class RecipeIngredientResponse
{
    public int IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    // C# dùng Quanlity theo quy ước project/DB; JSON vẫn là "quantity" cho frontend hiện tại.
    [JsonPropertyName("quantity")]
    public decimal Quanlity { get; set; }
}

/// <summary>
/// Kết quả của <c>GET /api/management/recipes</c> (Nhóm C/GĐ7).
/// Giữ nguyên mọi field cũ của entity <c>Recipe</c> và bổ sung <see cref="IngredientCount"/>
/// để danh sách công thức không hiển thị "undefined nguyên liệu".
/// </summary>
public sealed class RecipeSummaryResponse
{
    public int RecipeId { get; set; }
    public int SizeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int IngredientCount { get; set; }

    /// <summary>
    /// Dùng cho <c>POST /api/management/recipes</c> (C4) để không trả entity <see cref="Recipe"/> thô.
    /// </summary>
    public static RecipeSummaryResponse From(Recipe x, int ingredientCount) => new()
    {
        RecipeId = x.RecipeId,
        SizeId = x.SizeId,
        Name = x.Name,
        Version = x.Version,
        IsActive = x.IsActive,
        IngredientCount = ingredientCount
    };
}
