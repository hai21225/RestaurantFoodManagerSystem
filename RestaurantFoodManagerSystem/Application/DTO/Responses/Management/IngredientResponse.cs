/// <summary>
/// Kết quả của <c>GET/POST /api/management/ingredients</c> (C4).
/// Giữ nguyên tên field của entity <see cref="Ingredient"/> để frontend hiện tại
/// (inventory.js, recipes.js) không phải sửa.
/// </summary>
public sealed class IngredientResponse
{
    public int IngredientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal MinimumStock { get; set; }
    public bool IsActive { get; set; }

    public static IngredientResponse From(Ingredient x) => new()
    {
        IngredientId = x.IngredientId,
        Name = x.Name,
        Unit = x.Unit,
        MinimumStock = x.MinimumStock,
        IsActive = x.IsActive
    };
}
