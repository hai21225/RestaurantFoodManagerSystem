using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Báo cáo doanh thu (P3). Doanh thu = tổng <c>Payment.Amount</c> của các payment <c>Paid</c>;
/// KHÔNG tính giá trị đơn chưa thu. Kỳ báo cáo tính theo giờ Việt Nam (xem <see cref="VietnamTime"/>).
/// </summary>
public sealed class ReportService
{
    /// <summary>Header CSV cố định của báo cáo doanh thu.</summary>
    public const string RevenueCsvHeader = "period,groupStartVn,groupEndVn,revenue,orderCount";

    private readonly AppDbContext _db;

    public ReportService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<RevenueReportResponse> GetRevenueAsync(RevenueReportQuery query, CancellationToken ct = default)
    {
        var (period, fromUtc, toUtc, startVn, endVn) = VietnamTime.Resolve(query.Period, query.Date);

        var rows = await _db.Payments.AsNoTracking()
            .Where(x => x.Status == OrderStatuses.Paid && x.PaidAt >= fromUtc && x.PaidAt < toUtc)
            .Select(x => new { x.PaidAt, x.Amount, x.OrderId })
            .ToListAsync(ct);

        var byBucket = rows
            .GroupBy(r => VietnamTime.BucketStartVn(r.PaidAt, period))
            .ToDictionary(
                g => g.Key,
                g => (Revenue: g.Sum(x => x.Amount), Orders: g.Select(x => x.OrderId).Distinct().Count()));

        var groups = new List<RevenueGroupResponse>();
        for (var bucket = startVn; bucket < endVn; bucket = VietnamTime.NextBucketVn(bucket, period))
        {
            var next = VietnamTime.NextBucketVn(bucket, period);
            var has = byBucket.TryGetValue(bucket, out var bucketTotals);
            groups.Add(new RevenueGroupResponse
            {
                Label = VietnamTime.BucketLabel(bucket, period),
                From = bucket - VietnamTime.Offset,
                To = next - VietnamTime.Offset,
                Revenue = has ? bucketTotals.Revenue : 0m,
                OrderCount = has ? bucketTotals.Orders : 0
            });
        }

        var totalRevenue = rows.Sum(x => x.Amount);
        var orderCount = rows.Select(x => x.OrderId).Distinct().Count();

        return new RevenueReportResponse
        {
            Period = period,
            From = fromUtc,
            To = toUtc,
            TotalRevenue = totalRevenue,
            OrderCount = orderCount,
            AveragePerOrder = orderCount == 0 ? 0m : Math.Round(totalRevenue / orderCount, 2),
            Groups = groups
        };
    }

    /// <summary>
    /// Số liệu tổng hợp cho trang Tổng quan (Batch A): doanh thu hôm nay (giờ VN),
    /// đơn còn mở, tình trạng bàn, phiếu bếp và cảnh báo tồn kho thấp.
    /// </summary>
    public async Task<DashboardSummaryResponse> GetDashboardAsync(CancellationToken ct = default)
    {
        var (_, fromUtc, toUtc, startVn, endVn) = VietnamTime.Resolve("day", null);

        var paidRows = await _db.Payments.AsNoTracking()
            .Where(x => x.Status == OrderStatuses.Paid && x.PaidAt >= fromUtc && x.PaidAt < toUtc)
            .Select(x => new { x.Amount, x.OrderId })
            .ToListAsync(ct);

        var openOrders = _db.Orders.AsNoTracking()
            .Where(x => x.Status != OrderStatuses.Paid && x.Status != OrderStatuses.Cancelled);

        var openOrderCount = await openOrders.CountAsync(ct);

        var recentOrders = await (
            from o in openOrders
            join t in _db.RestaurantTables.AsNoTracking() on o.TableId equals t.TableId into tables
            from t in tables.DefaultIfEmpty()
            orderby o.CreatedAt descending, o.OrderId descending
            select new DashboardOrderItemResponse
            {
                OrderId = o.OrderId,
                TableId = o.TableId,
                TableNumber = t != null ? t.TableNumber : string.Empty,
                Status = o.Status,
                TotalAmount = o.TotalAmount,
                CreatedAt = o.CreatedAt
            }).Take(10).ToListAsync(ct);

        var activeTableIds = await _db.RestaurantTables.AsNoTracking()
            .Where(x => x.IsActive)
            .Select(x => x.TableId)
            .ToListAsync(ct);

        var occupiedTableIds = await openOrders
            .Select(x => x.TableId)
            .Distinct()
            .ToListAsync(ct);

        var occupied = occupiedTableIds.Count(id => activeTableIds.Contains(id));

        var kitchenRows = await _db.KitchenOrders.AsNoTracking()
            .Select(x => x.Status)
            .ToListAsync(ct);

        var lowStockCount = await (
            from inventory in _db.Inventories.AsNoTracking()
            join ingredient in _db.Ingredients.AsNoTracking() on inventory.IngredientId equals ingredient.IngredientId
            where ingredient.IsActive && inventory.Quanlity <= ingredient.MinimumStock
            select inventory.InventoryId).CountAsync(ct);

        return new DashboardSummaryResponse
        {
            GeneratedAt = DateTime.UtcNow,
            TimeZone = VietnamTime.TimeZoneId,
            BusinessDate = startVn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            From = fromUtc,
            To = toUtc,
            RevenueToday = paidRows.Sum(x => x.Amount),
            PaidOrderCountToday = paidRows.Select(x => x.OrderId).Distinct().Count(),
            OpenOrderCount = openOrderCount,
            LowStockCount = lowStockCount,
            Tables = new DashboardTablesSummary
            {
                Total = activeTableIds.Count,
                Occupied = occupied,
                Available = activeTableIds.Count - occupied,
                Maintenance = await _db.RestaurantTables.AsNoTracking().CountAsync(x => !x.IsActive, ct)
            },
            Kitchen = new DashboardKitchenSummary
            {
                Open = kitchenRows.Count(IsOpenKitchenStatus),
                Pending = kitchenRows.Count(x => IsStatus(x, "Pending")),
                Preparing = kitchenRows.Count(x => IsStatus(x, "Preparing") || IsStatus(x, "Accepted")),
                Ready = kitchenRows.Count(x => IsStatus(x, "Ready"))
            },
            RecentOrders = recentOrders
        };
    }

    private static bool IsStatus(string status, string expected) =>
        string.Equals(status, expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsOpenKitchenStatus(string status) =>
        !IsStatus(status, "Completed") && !IsStatus(status, "Cancelled");

    /// <summary>
    /// CSV UTF-8 (có BOM để Excel nhận tiếng Việt) của báo cáo doanh thu.
    /// Số dùng <see cref="CultureInfo.InvariantCulture"/> (dấu chấm thập phân) để nhất quán;
    /// thời gian trong CSV là giờ Việt Nam; dòng cuối là <c>TOTAL</c>.
    /// </summary>
    public async Task<byte[]> ExportRevenueCsvAsync(RevenueReportQuery query, CancellationToken ct = default)
    {
        var report = await GetRevenueAsync(query, ct);

        var csv = new StringBuilder();
        csv.Append(RevenueCsvHeader).Append("\r\n");

        foreach (var group in report.Groups)
        {
            csv.Append(Csv(report.Period)).Append(',')
               .Append(Csv((group.From + VietnamTime.Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append(',')
               .Append(Csv((group.To + VietnamTime.Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append(',')
               .Append(Csv(group.Revenue.ToString("0.##", CultureInfo.InvariantCulture))).Append(',')
               .Append(group.OrderCount)
               .Append("\r\n");
        }

        csv.Append("TOTAL").Append(',')
           .Append(Csv(((report.From + VietnamTime.Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))).Append(',')
           .Append(Csv(((report.To + VietnamTime.Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))).Append(',')
           .Append(Csv(report.TotalRevenue.ToString("0.##", CultureInfo.InvariantCulture))).Append(',')
           .Append(report.OrderCount)
           .Append("\r\n");

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return encoding.GetPreamble().Concat(encoding.GetBytes(csv.ToString())).ToArray();
    }

    private static string Csv(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
