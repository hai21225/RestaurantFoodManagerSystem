/// <summary>
/// Kết quả <c>GET /api/reports/dashboard</c> (Batch A): số liệu tổng hợp cho trang Tổng quan.
///
/// Doanh thu chỉ tính payment <c>Paid</c> trong ngày hôm nay theo giờ Việt Nam.
/// Trạng thái bàn được tính từ các đơn còn mở (không dựa vào cột <c>Status</c> có thể cũ).
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

/// <summary>Tình trạng bàn đang hoạt động (bàn đã soft-delete không được tính).</summary>
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
