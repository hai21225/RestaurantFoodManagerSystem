using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

// Độ dài MaxLength khớp với cột DB để request quá dài bị chặn 400 ngay thay vì
// lỗi "String or binary data would be truncated" (500) khi lưu.
public sealed class IngredientRequest { [Required, MaxLength(150)] public string Name { get; set; } = ""; [Required, MaxLength(50)] public string Unit { get; set; } = ""; [Range(0, double.MaxValue)] public decimal MinimumStock { get; set; } }
public sealed class SupplierRequest { [Required, MaxLength(150)] public string Name { get; set; } = ""; [Required, MaxLength(20)] public string Phone { get; set; } = ""; [Required, EmailAddress, MaxLength(150)] public string Email { get; set; } = ""; [Required, MaxLength(500)] public string Address { get; set; } = ""; }
public sealed class RecipeRequest { [Range(1, int.MaxValue)] public int SizeId { get; set; } [Required, MaxLength(150)] public string Name { get; set; } = ""; [MaxLength(50)] public string Version { get; set; } = "1"; [MinLength(1)] public List<RecipeIngredientRequest> Ingredients { get; set; } = []; }
public sealed class RecipeIngredientRequest { [Range(1, int.MaxValue)] public int IngredientId { get; set; } [Range(0.001, double.MaxValue)] [JsonPropertyName("quantity")] public decimal Quanlity { get; set; } }
public sealed class PurchaseOrderRequest { [Range(1, int.MaxValue)] public int SupplierId { get; set; } [Range(1, int.MaxValue)] public int CreatedBy { get; set; } public List<PurchaseOrderItemRequest> Items { get; set; } = []; }
public sealed class PurchaseOrderItemRequest { [Range(1, int.MaxValue)] public int IngredientId { get; set; } [Range(0.001, double.MaxValue)] [JsonPropertyName("quantity")] public decimal Quanlity { get; set; } [Range(0, double.MaxValue)] public decimal UnitPrice { get; set; } }
public sealed class ReceivePurchaseRequest { [Range(1, int.MaxValue)] public int ReceivedBy { get; set; } }
/// <summary>
/// Tạo/sửa khuyến mãi (<c>POST</c>/<c>PUT /api/management/promotions</c>).
///
/// <para>
/// <b>Quy ước múi giờ (Đợt 2 / P0-05):</b> <c>StartAt</c>/<c>EndAt</c> nhận theo
/// <b>giờ Việt Nam (UTC+7)</b>. Giá trị <b>không kèm múi giờ</b> được hiểu là giờ VN và
/// server quy đổi sang UTC khi lưu. Nếu client gửi kèm múi giờ (ISO-8601 có <c>Z</c> hoặc
/// offset) thì server giữ nguyên mốc thời gian đó.
/// </para>
/// </summary>
public sealed class PromotionRequest
{
    [Required, MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(1000)] public string Description { get; set; } = "";
    [Required, MaxLength(50)] public string DiscountType { get; set; } = "Percent";
    [Range(0, double.MaxValue)] public decimal DiscountValue { get; set; }

    /// <summary>Mốc bắt đầu (giờ Việt Nam nếu không kèm múi giờ).</summary>
    public DateTime StartAt { get; set; }

    /// <summary>Mốc kết thúc (giờ Việt Nam nếu không kèm múi giờ).</summary>
    public DateTime EndAt { get; set; }

    [Range(0, double.MaxValue)] public decimal MinimumOrderAmount { get; set; }
    [Range(0, double.MaxValue)] public decimal MaxDiscountAmount { get; set; }
}
public sealed class KitchenStatusRequest
{
    [Required, MaxLength(50)] public string Status { get; set; } = "";
    [MaxLength(500)] public string? Reason { get; set; }

    /// <summary>Người thực hiện (bếp). Khi có token thì token thắng; không token thì dùng id này.</summary>
    public int ChangedBy { get; set; }

    /// <summary>
    /// Lý do huỷ phiếu: <c>CustomerChange</c> (mặc định), <c>OutOfStock</c>,
    /// <c>PhysicalShortage</c>, hoặc <c>KitchenError</c>. Chỉ dùng khi <see cref="Status"/>
    /// = <c>Cancelled</c>; quyết định có hoàn kho hay ghi wastage.
    /// </summary>
    [MaxLength(50)] public string? ReasonCode { get; set; }
}

/// <summary>
/// Kho đánh dấu trạng thái đáp ứng của nguyên liệu: <c>Available</c> / <c>OutOfStock</c> /
/// <c>Spoiled</c> (C-13). Không xoá nguyên liệu — chỉ đổi trạng thái để quản lý và báo cáo.
/// </summary>
public sealed class IngredientStatusRequest
{
    [Required] public string StockStatus { get; set; } = IngredientStockStatuses.Available;

    [MaxLength(500)] public string? Note { get; set; }
}
public sealed class InventoryAdjustmentRequest { [Range(-1000000, 1000000)] [JsonPropertyName("quantity")] public decimal Quanlity { get; set; } [Range(1, int.MaxValue)] public int CreatedBy { get; set; } [MaxLength(500)] public string? Note { get; set; } }

/// <summary>
/// Tham số lọc/phân trang cho <c>GET /api/management/inventory-transactions</c> (P1). Tất cả tùy chọn.
/// <c>From</c>/<c>To</c> lọc theo <c>CreatedAt</c> (bao gồm cả hai đầu).
/// </summary>
public sealed class InventoryTransactionQuery
{
    public int? IngredientId { get; set; }
    public string? Type { get; set; }
    public string? ReferenceType { get; set; }
    public int? ReferenceId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
/// <summary>
/// C-18: target của khuyến mãi — khuyến mãi chỉ áp cho các món/danh mục nằm trong danh sách.
///
/// <para>
/// Gửi mảng **rỗng** = bỏ giới hạn ⇒ khuyến mãi áp cho **toàn bộ** hoá đơn (hành vi mặc định).
/// </para>
/// </summary>
public sealed class PromotionTargetsRequest
{
    /// <summary>Các size (món cụ thể) được áp khuyến mãi.</summary>
    public int[] SizeIds { get; set; } = [];

    /// <summary>Các danh mục được áp khuyến mãi (áp cho mọi món thuộc danh mục).</summary>
    public int[] CategoryIds { get; set; } = [];
}
