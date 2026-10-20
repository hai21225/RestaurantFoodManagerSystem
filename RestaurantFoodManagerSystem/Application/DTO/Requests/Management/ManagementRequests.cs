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
public sealed class PromotionRequest { [Required, MaxLength(150)] public string Name { get; set; } = ""; [MaxLength(1000)] public string Description { get; set; } = ""; [Required, MaxLength(50)] public string DiscountType { get; set; } = "Percent"; [Range(0, double.MaxValue)] public decimal DiscountValue { get; set; } public DateTime StartAt { get; set; } public DateTime EndAt { get; set; } [Range(0, double.MaxValue)] public decimal MinimumOrderAmount { get; set; } [Range(0, double.MaxValue)] public decimal MaxDiscountAmount { get; set; } }
public sealed class KitchenStatusRequest
{
    [Required, MaxLength(50)] public string Status { get; set; } = "";
    [MaxLength(500)] public string? Reason { get; set; }

    /// <summary>
    /// Lý do huỷ phiếu: <c>CustomerChange</c> (mặc định), <c>OutOfStock</c>,
    /// <c>PhysicalShortage</c>, hoặc <c>KitchenError</c>. Chỉ dùng khi <see cref="Status"/>
    /// = <c>Cancelled</c>; quyết định có hoàn kho hay ghi wastage.
    /// </summary>
    [MaxLength(50)] public string? ReasonCode { get; set; }
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
public sealed class PromotionLinkRequest { [Range(1, int.MaxValue)] public int Id { get; set; } }
