public class RestaurantTable
{
    public int TableId { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>
    /// Concurrency token (Nhóm B7). Trên SQL Server là cột <c>rowversion</c> do DB tự sinh;
    /// trên provider khác (SQLite dùng cho test) giá trị được <c>AppDbContext</c> tự cập nhật.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
