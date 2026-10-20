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

    /// <summary>
    /// Dòng món gây ra giao dịch (ledger theo món). Null cho giao dịch cấp đơn/nhập kho/
    /// điều chỉnh tay và cho dữ liệu cũ trước migration.
    /// </summary>
    public int? OrderItemId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

}