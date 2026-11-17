/// <summary>
/// Tham số cho <c>GET /api/reports/top-dishes</c> và <c>.../top-dishes/export.csv</c>.
/// </summary>
public sealed class TopDishesQuery
{
    /// <summary><c>day</c> (mặc định) | <c>week</c> | <c>month</c>.</summary>
    public string? Period { get; set; }

    /// <summary>Ngày bất kỳ trong kỳ cần báo cáo (giờ VN); mặc định hôm nay.</summary>
    public DateTime? Date { get; set; }

    /// <summary>Số lượng món top tối đa (mặc định 20).</summary>
    public int? Limit { get; set; }
}
