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
    public async Task<IActionResult> Revenue([FromQuery] RevenueReportQuery query, [FromServices] ReportService s, CancellationToken ct)
    {
        try { return Ok(await s.GetRevenueAsync(query, ct)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [HttpGet("revenue/export.csv")]
    public async Task<IActionResult> ExportRevenueCsv([FromQuery] RevenueReportQuery query, [FromServices] ReportService s, CancellationToken ct)
    {
        try
        {
            var bytes = await s.ExportRevenueCsvAsync(query, ct);
            var fileName = $"revenue-{VietnamTime.NormalizePeriod(query.Period)}-{DateTime.UtcNow:yyyyMMddHHmm}.csv";
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
