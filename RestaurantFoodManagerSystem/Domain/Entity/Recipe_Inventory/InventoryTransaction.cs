public class InventoryTransaction
{
    public int Id { get; set; }
    public int IngredientId { get; set; }
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