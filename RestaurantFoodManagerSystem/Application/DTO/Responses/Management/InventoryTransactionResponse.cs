/// <summary>
/// Một dòng biến động kho trong <c>GET /api/management/inventory-transactions</c> (P1).
/// <c>ingredientName</c> là join tiện dụng từ <c>Ingredients.Name</c>; null nếu nguyên liệu
/// đã bị xoá mềm/không tồn tại.
/// </summary>
public sealed class InventoryTransactionResponse
{
    public int Id { get; set; }
    public int IngredientId { get; set; }
    public string? IngredientName { get; set; }
    public string Type { get; set; } = string.Empty;
    public decimal Quanlity { get; set; }
    public decimal QuanlityBefore { get; set; }
    public decimal QuanlityAfter { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }
}
