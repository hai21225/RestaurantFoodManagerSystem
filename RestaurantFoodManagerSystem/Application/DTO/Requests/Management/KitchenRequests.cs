using System.ComponentModel.DataAnnotations;

public sealed class KitchenItemStatusRequest
{
    [Required] public string Status { get; set; } = string.Empty;

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
}
