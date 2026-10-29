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
            // Đợt 3: PaidAt nullable (giao dịch MoMo còn Pending thì chưa có thời điểm thu)
            // ⇒ chỉ lấy dòng đã thu thật sự, và bỏ null rõ ràng trước khi nhóm theo bucket.
            .Where(x => x.Status == PaymentStatuses.Paid
                     && x.PaidAt != null
                     && x.PaidAt >= fromUtc && x.PaidAt < toUtc)
            .Select(x => new { PaidAt = x.PaidAt!.Value, x.Amount, x.OrderId })
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
                From = VietnamTime.ToUtc(bucket),
                To = VietnamTime.ToUtc(next),
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
            .Where(x => x.Status == PaymentStatuses.Paid
                     && x.PaidAt != null
                     && x.PaidAt >= fromUtc && x.PaidAt < toUtc)
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

        // Tất cả bàn vẫn được hiển thị trên frontend. Bàn đã soft-delete hoặc đang bảo trì
        // được gom vào Maintenance; chỉ bàn active + không bảo trì mới là Available/Occupied.
        var tableRows = await _db.RestaurantTables.AsNoTracking()
            .Select(x => new { x.TableId, x.IsActive, x.Status })
            .ToListAsync(ct);

        var operationalTableIds = tableRows
            .Where(x => x.IsActive &&
                        !string.Equals(
                            x.Status,
                            RestaurantTableStatuses.Maintenance,
                            StringComparison.OrdinalIgnoreCase))
            .Select(x => x.TableId)
            .ToList();

        var occupiedTableIds = await openOrders
            .Select(x => x.TableId)
            .Distinct()
            .ToListAsync(ct);

        var occupied = occupiedTableIds.Count(id => operationalTableIds.Contains(id));
        var maintenance = tableRows.Count - operationalTableIds.Count;

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
                Total = tableRows.Count,
                Occupied = occupied,
                Available = operationalTableIds.Count - occupied,
                Maintenance = maintenance
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
            csv.Append(EscapeCsvCell(report.Period)).Append(',')
               .Append(EscapeCsvCell((group.From + VietnamTime.Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append(',')
               .Append(EscapeCsvCell((group.To + VietnamTime.Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append(',')
               .Append(EscapeCsvCell(group.Revenue.ToString("0.##", CultureInfo.InvariantCulture))).Append(',')
               .Append(group.OrderCount)
               .Append("\r\n");
        }

        csv.Append("TOTAL").Append(',')
           .Append(EscapeCsvCell(((report.From + VietnamTime.Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))).Append(',')
           .Append(EscapeCsvCell(((report.To + VietnamTime.Offset).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))).Append(',')
           .Append(EscapeCsvCell(report.TotalRevenue.ToString("0.##", CultureInfo.InvariantCulture))).Append(',')
           .Append(report.OrderCount)
           .Append("\r\n");

        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return encoding.GetPreamble().Concat(encoding.GetBytes(csv.ToString())).ToArray();
    }

    /// <summary>
    /// Escape 1 ô CSV. Ngoài escape dấu nháy/dấu phẩy, còn **chặn CSV formula injection**
    /// (OWASP): ô bắt đầu bằng <c>=</c> <c>+</c> <c>-</c> <c>@</c> sẽ bị Excel/LibreOffice
    /// hiểu là **công thức** khi mở file (ví dụ tên món do người dùng nhập) ⇒ tiền tên
    /// dấu nháy đơn để buộc hiển thị dạng text.
    ///
    /// <para>
    /// Số âm (ví dụ <c>-5</c>) là dữ liệu số hợp lệ nên **không** bị chặn — chỉ chuỗi
    /// không phải số mới bị tiền tên dấu nháy.
    /// </para>
    ///
    /// <para>Public để các báo cáo mới (top-dishes, by-staff…) dùng lại đúng hàm này.</para>
    /// </summary>
    public static string EscapeCsvCell(string value)
    {
        if (NeedsFormulaGuard(value))
        {
            value = "'" + value;
        }

        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
            return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    /// <summary>
    /// Ô có bắt đầu bằng ký tự công thức và **không** phải số ⇒ cần chặn formula injection.
    /// </summary>
    private static bool NeedsFormulaGuard(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        var trimmed = value.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var first = trimmed[0];
        if (first is not ('=' or '+' or '-' or '@'))
        {
            return false;
        }

        // Số âm (-5) / số khoa học là dữ liệu số hợp lệ ⇒ không chặn.
        return !decimal.TryParse(
            trimmed, NumberStyles.Any, CultureInfo.InvariantCulture, out _);
    }
}
