/// <summary>
/// Kết quả <c>GET /api/reports/revenue</c> (P3): chỉ tính tiền **đã thu** (payment <c>Paid</c>),
/// kỳ xác định theo giờ Việt Nam. <see cref="From"/> bao gồm, <see cref="To"/> loại trừ (UTC).
/// </summary>
public sealed class RevenueReportResponse
{
    public string Period { get; set; } = "day";
    public string TimeZone { get; set; } = VietnamTime.TimeZoneId;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal TotalRevenue { get; set; }
    public int OrderCount { get; set; }
    public decimal AveragePerOrder { get; set; }
    public List<RevenueGroupResponse> Groups { get; set; } = [];
}

/// <summary>
/// Một bucket doanh thu: giờ (kỳ <c>day</c>) hoặc ngày (kỳ <c>week</c>/<c>month</c>).
/// </summary>
public sealed class RevenueGroupResponse
{
    /// <summary>Nhãn giờ VN: <c>HH:00</c> cho kỳ ngày, <c>dd/MM</c> cho tuần/tháng.</summary>
    public string Label { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal Revenue { get; set; }
    public int OrderCount { get; set; }
}

/// <summary>Một dòng món bán chạy / lợi nhuận theo size.</summary>
public sealed class TopDishItemResponse
{
    public int FoodId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public int SizeId { get; set; }
    public string SizeName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public int QuantitySold { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cogs { get; set; }
    public decimal GrossProfit { get; set; }
    public decimal GrossMarginPct { get; set; }
    public bool HasMissingRecipe { get; set; }
    public bool HasMissingCost { get; set; }
}

/// <summary>Báo cáo lợi nhuận gộp (COGS) theo kỳ giờ Việt Nam.</summary>
public sealed class ProfitabilityReportResponse
{
    public string Period { get; set; } = "day";
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalCogs { get; set; }
    public decimal TotalGrossProfit { get; set; }
    public decimal GrossMarginPct { get; set; }
    public int MissingRecipeOrCostCount { get; set; }
    public List<TopDishItemResponse> Items { get; set; } = [];
}
