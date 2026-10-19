public class Inventory
{
    public int InventoryId { get; set; }
    public int IngredientId { get; set; }
    public decimal Quanlity { get; set; } // Lưu ý: tên cột trong sơ đồ là 'quality' (hoặc quantity)
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Concurrency token (Nhóm B7). Trên SQL Server là cột <c>rowversion</c> do DB tự sinh;
    /// trên provider khác (SQLite dùng cho test) giá trị được <c>AppDbContext</c> tự cập nhật.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}