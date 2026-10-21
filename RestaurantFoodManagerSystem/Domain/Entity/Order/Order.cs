public class Order
{
    public int OrderId { get; set; }
    public int TableId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }

    /// <summary>
    /// Số tiền VAT của đơn. Trước Đợt 2 cột này <b>luôn bằng 0</b> vì không có nơi nào gán;
    /// giờ được tính bởi <see cref="OrderTotalsCalculator"/> theo cấu hình quán.
    /// </summary>
    public decimal TaxAmount { get; set; }

    /// <summary>
    /// Phí dịch vụ của đơn (Đợt 2 / A1). Luôn cộng thêm vào <see cref="TotalAmount"/>.
    /// </summary>
    public decimal ServiceChargeAmount { get; set; }

    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Ghi chú cho **cả hoá đơn** (Đợt 3 / B3): "xuất hoá đơn công ty ABC", "chia đôi với
    /// bàn 6", "khách đợi bạn"… Khác với <c>OrderItem.Note</c> là ghi chú cho từng món.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>
    /// Khuyến mãi đang áp dụng cho đơn (Đợt 2 / A3). Null = đơn không có khuyến mãi.
    /// </summary>
    public int? PromotionId { get; set; }

    /// <summary>
    /// Snapshot loại giảm giá của khuyến mãi tại lúc áp dụng (Đợt 2 / A3).
    /// Giữ lại để sửa/xoá khuyến mãi sau này không làm đổi nghĩa của đơn cũ.
    /// </summary>
    public string? PromotionDiscountType { get; set; }

    /// <summary>
    /// Snapshot giá trị giảm giá của khuyến mãi tại lúc áp dụng (Đợt 2 / A3).
    /// </summary>
    public decimal? PromotionDiscountValue { get; set; }

    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Concurrency token (Nhóm B7). Trên SQL Server là cột <c>rowversion</c> do DB tự sinh;
    /// trên provider khác (SQLite dùng cho test) giá trị được <c>AppDbContext</c> tự cập nhật.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
