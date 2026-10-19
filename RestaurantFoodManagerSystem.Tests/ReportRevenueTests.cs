using System.Net;
using System.Text;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// P3: báo cáo doanh thu theo ngày/tuần/tháng (giờ VN, chỉ tính payment <c>Paid</c>) + CSV.
/// </summary>
public sealed class ReportRevenueServiceTests : TestBase
{
    private static DateTime TodayVn => (DateTime.UtcNow + VietnamTime.Offset).Date;

    private Payment SeedPaidPayment(int tableId, int userId, DateTime paidAtVn, decimal amount, string status = OrderStatuses.Paid)
    {
        var order = SeedOrder(tableId, userId, status: status, subtotal: amount);
        var payment = new Payment
        {
            OrderId = order.OrderId,
            Amount = amount,
            PaymentMethod = "Cash",
            Status = status,
            TransactionCode = string.Empty,
            PaidAt = paidAtVn - VietnamTime.Offset,
            CreatedBy = userId
        };
        Db.Payments.Add(payment);
        Db.SaveChanges();
        return payment;
    }

    [Fact]
    public async Task Revenue_Day_GroupsByHour_IgnoresUnpaidAndOtherDays()
    {
        var s = SeedOrderScenario();
        var today = TodayVn;

        SeedPaidPayment(s.Table.TableId, s.Staff.Id, today.AddHours(9), 100m);
        SeedPaidPayment(s.Table.TableId, s.Staff.Id, today.AddHours(9).AddMinutes(30), 50m);
        SeedPaidPayment(s.Table.TableId, s.Staff.Id, today.AddHours(10), 25m);
        SeedPaidPayment(s.Table.TableId, s.Staff.Id, today.AddHours(11), 999m, status: "Pending"); // chưa thu
        SeedPaidPayment(s.Table.TableId, s.Staff.Id, today.AddDays(-1).AddHours(9), 777m);          // ngoài kỳ

        var report = await Reports.GetRevenueAsync(new RevenueReportQuery { Period = "day", Date = today });

        Assert.Equal("day", report.Period);
        Assert.Equal(VietnamTime.TimeZoneId, report.TimeZone);
        Assert.Equal(175m, report.TotalRevenue);
        Assert.Equal(3, report.OrderCount);
        Assert.Equal(Math.Round(175m / 3m, 2), report.AveragePerOrder);

        Assert.Equal(24, report.Groups.Count); // đủ 24 giờ, kể cả giờ trống
        Assert.Equal(150m, report.Groups.Single(g => g.Label == "09:00").Revenue);
        Assert.Equal(2, report.Groups.Single(g => g.Label == "09:00").OrderCount);
        Assert.Equal(25m, report.Groups.Single(g => g.Label == "10:00").Revenue);
        Assert.Equal(0m, report.Groups.Single(g => g.Label == "00:00").Revenue);
    }

    [Fact]
    public async Task Revenue_Week_StartsMonday_WithSevenBuckets()
    {
        var s = SeedOrderScenario();
        var today = TodayVn;
        SeedPaidPayment(s.Table.TableId, s.Staff.Id, today.AddHours(9), 30m);

        var report = await Reports.GetRevenueAsync(new RevenueReportQuery { Period = "week", Date = today });

        Assert.Equal(7, report.Groups.Count);
        Assert.Equal(DayOfWeek.Monday, (report.From + VietnamTime.Offset).DayOfWeek);
        Assert.Equal(report.To + VietnamTime.Offset, report.From + VietnamTime.Offset + TimeSpan.FromDays(7));
        Assert.Equal(30m, report.TotalRevenue);
    }

    [Fact]
    public async Task Revenue_Month_HasOneBucketPerDay()
    {
        var today = TodayVn;

        var report = await Reports.GetRevenueAsync(new RevenueReportQuery { Period = "month", Date = today });

        Assert.Equal(DateTime.DaysInMonth(today.Year, today.Month), report.Groups.Count);
        Assert.Equal(1, (report.From + VietnamTime.Offset).Day);
    }

    [Fact]
    public async Task Revenue_InvalidPeriod_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Reports.GetRevenueAsync(new RevenueReportQuery { Period = "year" }));
    }

    [Fact]
    public async Task Dashboard_SummarizesRevenueOpenOrdersTablesKitchenAndLowStock()
    {
        var s = SeedOrderScenario();

        var openOrder = SeedOrder(s.Table.TableId, s.Staff.Id, status: OrderStatuses.Preparing, subtotal: 75m);
        Db.KitchenOrders.Add(new KitchenOrder
        {
            OrderId = openOrder.OrderId,
            Status = "Preparing",
            SentAt = DateTime.UtcNow,
            Priority = 0
        });

        var paidTable = SeedTable();
        SeedPaidPayment(paidTable.TableId, s.Staff.Id, TodayVn.AddHours(10), 120m);

        SeedTable(); // bàn trống đang hoạt động
        var inactive = SeedTable();
        inactive.IsActive = false;
        Db.SaveChanges();

        SeedIngredient(1m, minimumStock: 5m); // dưới mức tối thiểu

        var summary = await Reports.GetDashboardAsync(CancellationToken.None);

        Assert.Equal(VietnamTime.TimeZoneId, summary.TimeZone);
        Assert.Equal(TodayVn.ToString("yyyy-MM-dd"), summary.BusinessDate);
        Assert.Equal(120m, summary.RevenueToday);
        Assert.Equal(1, summary.PaidOrderCountToday);
        Assert.Equal(1, summary.OpenOrderCount);
        Assert.Equal(1, summary.LowStockCount);

        Assert.Equal(3, summary.Tables.Total);
        Assert.Equal(1, summary.Tables.Occupied);
        Assert.Equal(2, summary.Tables.Available);
        Assert.Equal(1, summary.Tables.Maintenance);

        Assert.Equal(1, summary.Kitchen.Open);
        Assert.Equal(0, summary.Kitchen.Pending);
        Assert.Equal(1, summary.Kitchen.Preparing);
        Assert.Equal(0, summary.Kitchen.Ready);

        var recent = Assert.Single(summary.RecentOrders);
        Assert.Equal(openOrder.OrderId, recent.OrderId);
        Assert.Equal(s.Table.TableNumber, recent.TableNumber);
        Assert.Equal(OrderStatuses.Preparing, recent.Status);
    }

    [Fact]
    public async Task Csv_HasBom_StableHeader_AndTotalRow()
    {
        var s = SeedOrderScenario();
        var today = TodayVn;
        SeedPaidPayment(s.Table.TableId, s.Staff.Id, today.AddHours(9), 150m);

        var bytes = await Reports.ExportRevenueCsvAsync(new RevenueReportQuery { Period = "day", Date = today });

        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());

        // GetString giữ nguyên BOM thành U+FEFF nên phải cắt trước khi so header.
        var text = Encoding.UTF8.GetString(bytes);
        if (text.StartsWith('\uFEFF')) text = text[1..];
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(ReportService.RevenueCsvHeader, lines[0]);
        Assert.Equal("day", lines[1].Split(',')[0]);
        var nine = lines.Select(l => l.Split(',')).Single(f => f[1].EndsWith(" 09:00"));
        Assert.Equal("150", nine[3]);
        Assert.Equal("1", nine[4]);
        Assert.StartsWith("TOTAL,", lines[^1]);
        Assert.EndsWith(",150,1", lines[^1]);
    }
}

public sealed class ReportRevenueHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public ReportRevenueHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetRevenue_ReturnsMonthlyReport()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/reports/revenue?period=month");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"period\":\"month\"", body);
        Assert.Contains("\"timeZone\":\"Asia/Ho_Chi_Minh\"", body);
        Assert.Contains("\"totalRevenue\":", body);
        Assert.Contains("\"groups\":", body);
    }

    [Fact]
    public async Task GetRevenue_InvalidPeriod_ReturnsBadRequest()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/reports/revenue?period=year");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("period", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetDashboard_ReturnsSummaryShape()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/reports/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"revenueToday\":", body);
        Assert.Contains("\"openOrderCount\":", body);
        Assert.Contains("\"tables\":", body);
        Assert.Contains("\"kitchen\":", body);
        Assert.Contains("\"recentOrders\":", body);
    }

    [Fact]
    public async Task ExportRevenueCsv_ReturnsCsvFile()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/reports/revenue/export.csv?period=week");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".csv", response.Content.Headers.ContentDisposition!.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray());
    }
}
