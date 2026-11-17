using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

/// <summary>
/// Báo cáo doanh thu (P3). Chỉ Admin — dữ liệu doanh thu là thông tin nhạy cảm.
/// </summary>
[ApiController, Route("api/reports")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public sealed class ReportsController : ControllerBase
{
    /// <summary>Tổng hợp số liệu cho trang Tổng quan (doanh thu hôm nay, đơn/bàn/bếp/tồn kho).</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromServices] ReportService s, CancellationToken ct) =>
        Ok(await s.GetDashboardAsync(ct));

    [HttpGet("revenue")]
    public async Task<IActionResult> Revenue([FromQuery] RevenueReportQuery query, [FromServices] ReportService s, CancellationToken ct) =>
        Ok(await s.GetRevenueAsync(query, ct));

    [HttpGet("revenue/export.csv")]
    public async Task<IActionResult> ExportRevenueCsv([FromQuery] RevenueReportQuery query, [FromServices] ReportService s, CancellationToken ct) =>
        CsvFileResult(await s.ExportRevenueCsvAsync(query, ct), "revenue", query.Period);

    [HttpGet("top-dishes")]
    public async Task<IActionResult> TopDishes([FromQuery] TopDishesQuery query, [FromServices] ReportService s, CancellationToken ct) =>
        Ok(await s.GetTopDishesAsync(query, ct));

    [HttpGet("top-dishes/export.csv")]
    public async Task<IActionResult> ExportTopDishesCsv([FromQuery] TopDishesQuery query, [FromServices] ReportService s, CancellationToken ct) =>
        CsvFileResult(await s.ExportTopDishesCsvAsync(query, ct), "top-dishes", query.Period);

    [HttpGet("profitability")]
    public async Task<IActionResult> Profitability([FromQuery] RevenueReportQuery query, [FromServices] ReportService s, CancellationToken ct) =>
        Ok(await s.GetProfitabilityAsync(query, ct));

    [HttpGet("profitability/export.csv")]
    public async Task<IActionResult> ExportProfitabilityCsv([FromQuery] RevenueReportQuery query, [FromServices] ReportService s, CancellationToken ct) =>
        CsvFileResult(await s.ExportProfitabilityCsvAsync(query, ct), "profitability", query.Period);

    private FileContentResult CsvFileResult(byte[] bytes, string prefix, string? period)
    {
        // Tên file dùng giờ VN cho khớp với nội dung CSV (giờ VN).
        var stamp = (DateTime.UtcNow + VietnamTime.Offset).ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture);
        var fileName = $"{prefix}-{VietnamTime.NormalizePeriod(period)}-{stamp}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}
