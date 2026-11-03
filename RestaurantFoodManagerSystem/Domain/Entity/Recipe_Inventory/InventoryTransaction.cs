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

    /// <summary>
    /// Đơn vị tính của lượng tại thời điểm ghi dòng ledger — snapshot từ
    /// <see cref="Ingredient.Unit"/>. Nhờ vậy dòng kho vẫn đọc được sau này dù đơn vị
    /// của nguyên liệu có bị kho sửa (hiện đã chặn) hoặc nguyên liệu bị ẩn.
    /// Null cho dữ liệu cũ trước migration C-17.
    /// </summary>
    public string? Unit { get; set; }

    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; }

}