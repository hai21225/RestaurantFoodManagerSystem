/// <summary>
/// Tham số cho <c>GET /api/reports/revenue</c> và <c>.../revenue/export.csv</c> (P3).
/// </summary>
public sealed class RevenueReportQuery
{
    /// <summary><c>day</c> (mặc định) | <c>week</c> | <c>month</c>.</summary>
    public string? Period { get; set; }

    /// <summary>Ngày bất kỳ trong kỳ cần báo cáo (giờ VN); mặc định hôm nay.</summary>
    public DateTime? Date { get; set; }
}
