public sealed class RecipeResponse
{
    public int RecipeId { get; set; }
    public int SizeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<RecipeIngredientResponse> Ingredients { get; set; } = [];
}

public sealed class RecipeIngredientResponse
{
    public int IngredientId { get; set; }
    public string IngredientName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
}
