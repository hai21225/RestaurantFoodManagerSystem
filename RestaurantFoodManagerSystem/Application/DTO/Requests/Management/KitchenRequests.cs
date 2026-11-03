using System.ComponentModel.DataAnnotations;

public sealed class KitchenItemStatusRequest
{
    [Required] public string Status { get; set; } = string.Empty;

    /// <summary>Người thực hiện (bếp). Khi có token thì token thắng; không token thì dùng id này.</summary>
    public int ChangedBy { get; set; }

    /// <summary>Lý do huỷ món (bắt buộc trên UI bếp; để trống sẽ dùng "Cancelled from kitchen").</summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>
    /// Lý do huỷ: <c>CustomerChange</c> (mặc định), <c>OutOfStock</c>,
    /// <c>PhysicalShortage</c>, hoặc <c>KitchenError</c>. Chỉ dùng khi <see cref="Status"/>
    /// = <c>Cancelled</c>; quyết định có hoàn kho hay ghi wastage.
    /// </summary>
    [MaxLength(50)]
    public string? ReasonCode { get; set; }
}

/// <summary>
/// C-06: mở lại một món đã <c>Ready/Completed</c> trên phiếu bếp chưa đóng
/// (<c>POST /api/management/kitchen/items/{id}/reopen</c>).
/// </summary>
public sealed class ReopenKitchenItemRequest
{
    /// <summary>Người thực hiện (bếp). Khi có token thì token thắng; không token thì dùng id này.</summary>
    public int ChangedBy { get; set; }

    [MaxLength(500)] public string? Reason { get; set; }
}

/// <summary>
/// Bếp báo đã nấu bao nhiêu phần của một món
/// (<c>PATCH /api/management/kitchen/items/{id}/prepared-quantity</c>).
/// Nếu nhỏ hơn số lượng hiện tại, dòng món được tách thành phần đã nấu và phần còn lại.
/// </summary>
public sealed class PreparedQuantityRequest
{
    [Range(1, int.MaxValue)] public int PreparedQuantity { get; set; }

    public int ChangedBy { get; set; }

    [MaxLength(500)] public string? Reason { get; set; }
}

/// <summary>
/// Bếp báo thiếu nguyên liệu thực tế (<c>POST /api/management/kitchen/items/{id}/shortage</c>).
/// Phần làm được giữ lại; phần còn lại bị huỷ với reason <c>OutOfStock/PhysicalShortage</c>.
/// </summary>
public sealed class ReportShortageRequest
{
    /// <summary>Số phần thực tế còn làm được (0..số lượng hiện tại).</summary>
    [Range(0, int.MaxValue)] public int PreparedQuantity { get; set; }

    /// <summary><c>OutOfStock</c> hoặc <c>PhysicalShortage</c>.</summary>
    [Required, MaxLength(50)] public string ReasonCode { get; set; } = string.Empty;

    public int ChangedBy { get; set; }

    [MaxLength(500)] public string? Reason { get; set; }

    /// <summary>
    /// Nguyên liệu nào thực sự hư/hết (C-13). Khi có, ledger wastage chỉ ghi cho đúng
    /// nguyên liệu này thay vì ghi cho cả công thức, và trạng thái nguyên liệu được đổi
    /// (OutOfStock / Spoiled) để kho quản lý.
    /// </summary>
    public int? IngredientId { get; set; }

    /// <summary>
    /// Ngừng bán món này (món bị vô hiệu hoá + lưu lý do) vì lô nguyên liệu hư/hết hạn
    /// — dùng làm minh chứng cho báo cáo. Nguyên liệu vẫn được giữ nguyên.
    /// </summary>
    public bool DeactivateDish { get; set; }

    /// <summary>Lý do vô hiệu hoá món (lưu cùng món để làm báo cáo).</summary>
    [MaxLength(500)] public string? InactiveReason { get; set; }
}
