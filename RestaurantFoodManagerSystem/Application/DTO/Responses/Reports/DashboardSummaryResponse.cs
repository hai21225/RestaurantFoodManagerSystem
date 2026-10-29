/// <summary>
/// Kết quả <c>GET /api/reports/dashboard</c> (Batch A): số liệu tổng hợp cho trang Tổng quan.
///
/// Doanh thu chỉ tính payment <c>Paid</c> trong ngày hôm nay theo giờ Việt Nam.
/// <c>Occupied</c> tính từ đơn còn mở; <c>Maintenance</c> lấy từ trạng thái vận hành
/// và cờ soft-delete của bàn.
/// </summary>
public sealed class DashboardSummaryResponse
{
    public DateTime GeneratedAt { get; set; }
    public string TimeZone { get; set; } = VietnamTime.TimeZoneId;
    public string BusinessDate { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }

    public decimal RevenueToday { get; set; }
    public int PaidOrderCountToday { get; set; }
    public int OpenOrderCount { get; set; }
    public int LowStockCount { get; set; }

    public DashboardTablesSummary Tables { get; set; } = new();
    public DashboardKitchenSummary Kitchen { get; set; } = new();
    public List<DashboardOrderItemResponse> RecentOrders { get; set; } = [];
}

/// <summary>
/// Tổng hợp tất cả bàn (kể cả bàn đã soft-delete). Bàn soft-delete hoặc đang bảo trì
/// được tính vào <see cref="Maintenance"/> để <c>Available + Occupied + Maintenance = Total</c>.
/// </summary>
public sealed class DashboardTablesSummary
{
    public int Total { get; set; }
    public int Available { get; set; }
    public int Occupied { get; set; }
    public int Maintenance { get; set; }
}

/// <summary>Số phiếu bếp theo nhóm trạng thái.</summary>
public sealed class DashboardKitchenSummary
{
    public int Open { get; set; }
    public int Pending { get; set; }
    public int Preparing { get; set; }
    public int Ready { get; set; }
}

/// <summary>Một đơn còn mở trong danh sách gần đây của dashboard.</summary>
public sealed class DashboardOrderItemResponse
{
    public int OrderId { get; set; }
    public int TableId { get; set; }
    public string TableNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}
