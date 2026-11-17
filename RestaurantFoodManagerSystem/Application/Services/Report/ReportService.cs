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

    /// <summary>Header CSV cố định của báo cáo món bán chạy / lợi nhuận.</summary>
    public const string DishProfitabilityCsvHeader =
        "foodId,foodName,sizeId,sizeName,categoryId,quantitySold,revenue,cogs,grossProfit,grossMarginPct,hasMissingRecipe,hasMissingCost";

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
            .ToHashSet();

        var occupiedTableIds = await openOrders
            .Select(x => x.TableId)
            .Distinct()
            .ToListAsync(ct);

        var occupied = occupiedTableIds.Count(id => operationalTableIds.Contains(id));
        var maintenance = tableRows.Count - operationalTableIds.Count;

        var kitchenCounts = await _db.KitchenOrders.AsNoTracking()
            .Where(x => x.Status != KitchenStatuses.Completed && x.Status != KitchenStatuses.Cancelled)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
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
                Open = kitchenCounts.Where(x => IsOpenKitchenStatus(x.Status)).Sum(x => x.Count),
                Pending = kitchenCounts.Where(x => IsStatus(x.Status, KitchenStatuses.Pending)).Sum(x => x.Count),
                Preparing = kitchenCounts.Where(x => IsStatus(x.Status, KitchenStatuses.Preparing) || IsStatus(x.Status, KitchenStatuses.Accepted)).Sum(x => x.Count),
                Ready = kitchenCounts.Where(x => IsStatus(x.Status, KitchenStatuses.Ready)).Sum(x => x.Count)
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

        return ToUtf8BomBytes(csv);
    }

    public async Task<List<TopDishItemResponse>> GetTopDishesAsync(TopDishesQuery query, CancellationToken ct = default)
    {
        var (_, _, _, items) = await BuildDishProfitabilityAsync(query.Period, query.Date, ct);
        var limit = query.Limit is > 0 ? Math.Min(query.Limit.Value, 200) : 20;
        return items
            .OrderByDescending(x => x.QuantitySold)
            .ThenByDescending(x => x.Revenue)
            .ThenBy(x => x.FoodName, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    public async Task<ProfitabilityReportResponse> GetProfitabilityAsync(RevenueReportQuery query, CancellationToken ct = default)
    {
        var (period, fromUtc, toUtc, items) = await BuildDishProfitabilityAsync(query.Period, query.Date, ct);
        var sorted = items
            .OrderByDescending(x => x.Revenue)
            .ThenByDescending(x => x.GrossProfit)
            .ToList();

        var totalRevenue = sorted.Sum(x => x.Revenue);
        var totalCogs = sorted.Sum(x => x.Cogs);
        var totalGrossProfit = totalRevenue - totalCogs;
        var grossMarginPct = totalRevenue > 0
            ? Math.Round(totalGrossProfit / totalRevenue * 100m, 2, MidpointRounding.AwayFromZero)
            : 0m;

        return new ProfitabilityReportResponse
        {
            Period = period,
            From = fromUtc,
            To = toUtc,
            TotalRevenue = totalRevenue,
            TotalCogs = totalCogs,
            TotalGrossProfit = totalGrossProfit,
            GrossMarginPct = grossMarginPct,
            MissingRecipeOrCostCount = sorted.Count(x => x.HasMissingRecipe || x.HasMissingCost),
            Items = sorted
        };
    }

    public async Task<byte[]> ExportTopDishesCsvAsync(TopDishesQuery query, CancellationToken ct = default)
    {
        var items = await GetTopDishesAsync(query, ct);
        var csv = new StringBuilder();
        csv.Append(DishProfitabilityCsvHeader).Append("\r\n");
        AppendDishCsvRows(csv, items);
        return ToUtf8BomBytes(csv);
    }

    public async Task<byte[]> ExportProfitabilityCsvAsync(RevenueReportQuery query, CancellationToken ct = default)
    {
        var report = await GetProfitabilityAsync(query, ct);
        var csv = new StringBuilder();
        csv.Append(DishProfitabilityCsvHeader).Append("\r\n");
        AppendDishCsvRows(csv, report.Items);

        csv.Append("TOTAL,,,,")
           .Append(',').Append(report.Items.Sum(x => x.QuantitySold))
           .Append(',').Append(EscapeCsvCell(report.TotalRevenue.ToString("0.##", CultureInfo.InvariantCulture)))
           .Append(',').Append(EscapeCsvCell(report.TotalCogs.ToString("0.##", CultureInfo.InvariantCulture)))
           .Append(',').Append(EscapeCsvCell(report.TotalGrossProfit.ToString("0.##", CultureInfo.InvariantCulture)))
           .Append(',').Append(EscapeCsvCell(report.GrossMarginPct.ToString("0.##", CultureInfo.InvariantCulture)))
           .Append(",,")
           .Append("\r\n");

        return ToUtf8BomBytes(csv);
    }

    private async Task<(string Period, DateTime FromUtc, DateTime ToUtc, List<TopDishItemResponse> Items)>
        BuildDishProfitabilityAsync(string? periodInput, DateTime? dateInput, CancellationToken ct)
    {
        var (period, fromUtc, toUtc, _, _) = VietnamTime.Resolve(periodInput, dateInput);

        var paidOrderIds = _db.Payments.AsNoTracking()
            .Where(x => x.Status == PaymentStatuses.Paid
                     && x.PaidAt != null
                     && x.PaidAt >= fromUtc && x.PaidAt < toUtc)
            .Select(x => x.OrderId)
            .Distinct();

        var soldRows = await (
            from oi in _db.OrderItems.AsNoTracking()
            join fs in _db.FoodSizes.AsNoTracking() on oi.SizeId equals fs.SizeId
            join f in _db.Foods.AsNoTracking() on fs.FoodId equals f.FoodId
            where paidOrderIds.Contains(oi.OrderId)
               && oi.Status != OrderStatuses.Cancelled
            group oi by new { f.FoodId, f.FoodName, fs.SizeId, fs.SizeName, f.CategoryId } into g
            select new
            {
                g.Key.FoodId,
                g.Key.FoodName,
                g.Key.SizeId,
                g.Key.SizeName,
                g.Key.CategoryId,
                QuantitySold = g.Sum(x => x.Quanlity),
                Revenue = g.Sum(x => x.Quanlity * x.UnitPrice)
            })
            .ToListAsync(ct);

        if (soldRows.Count == 0)
        {
            return (period, fromUtc, toUtc, []);
        }

        var sizeIds = soldRows.Select(x => x.SizeId).Distinct().ToList();
        var activeRecipes = await _db.Recipes.AsNoTracking()
            .Where(r => r.IsActive && sizeIds.Contains(r.SizeId))
            .Select(r => new { r.RecipeId, r.SizeId })
            .ToListAsync(ct);

        var recipeBySize = activeRecipes
            .GroupBy(r => r.SizeId)
            .ToDictionary(g => g.Key, g => g.First().RecipeId);

        var recipeIds = recipeBySize.Values.Distinct().ToList();
        var recipeIngredients = recipeIds.Count == 0
            ? []
            : await _db.RecipeIngredients.AsNoTracking()
                .Where(ri => recipeIds.Contains(ri.RecipeId))
                .Select(ri => new { ri.RecipeId, ri.IngredientId, ri.Quanlity })
                .ToListAsync(ct);

        var ingredientIds = recipeIngredients.Select(ri => ri.IngredientId).Distinct().ToList();
        var costRows = ingredientIds.Count == 0
            ? []
            : await (
                from poi in _db.PurchaseOrderItems.AsNoTracking()
                join po in _db.PurchaseOrders.AsNoTracking() on poi.PurchaseOrderId equals po.Id
                where po.ReceivedAt != null && ingredientIds.Contains(poi.IngredientId)
                group poi by poi.IngredientId into g
                select new
                {
                    IngredientId = g.Key,
                    TotalQty = g.Sum(x => x.Quanlity),
                    TotalCost = g.Sum(x => x.TotalPrice)
                })
                .ToListAsync(ct);

        var avgCostByIngredient = costRows
            .Where(x => x.TotalQty > 0)
            .ToDictionary(x => x.IngredientId, x => x.TotalCost / x.TotalQty);

        var ingredientsByRecipe = recipeIngredients
            .GroupBy(ri => ri.RecipeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = new List<TopDishItemResponse>(soldRows.Count);
        foreach (var row in soldRows)
        {
            var hasMissingRecipe = !recipeBySize.TryGetValue(row.SizeId, out var recipeId);
            var hasMissingCost = false;
            var unitCogs = 0m;

            if (!hasMissingRecipe && ingredientsByRecipe.TryGetValue(recipeId, out var lines))
            {
                foreach (var line in lines)
                {
                    if (avgCostByIngredient.TryGetValue(line.IngredientId, out var avgCost))
                    {
                        unitCogs += line.Quanlity * avgCost;
                    }
                    else
                    {
                        hasMissingCost = true;
                    }
                }
            }

            var cogs = Math.Round(unitCogs * row.QuantitySold, 2, MidpointRounding.AwayFromZero);
            var grossProfit = row.Revenue - cogs;
            var grossMarginPct = row.Revenue > 0
                ? Math.Round(grossProfit / row.Revenue * 100m, 2, MidpointRounding.AwayFromZero)
                : 0m;

            items.Add(new TopDishItemResponse
            {
                FoodId = row.FoodId,
                FoodName = row.FoodName,
                SizeId = row.SizeId,
                SizeName = row.SizeName,
                CategoryId = row.CategoryId,
                QuantitySold = row.QuantitySold,
                Revenue = row.Revenue,
                Cogs = cogs,
                GrossProfit = grossProfit,
                GrossMarginPct = grossMarginPct,
                HasMissingRecipe = hasMissingRecipe,
                HasMissingCost = hasMissingCost
            });
        }

        return (period, fromUtc, toUtc, items);
    }

    private static void AppendDishCsvRows(StringBuilder csv, IEnumerable<TopDishItemResponse> items)
    {
        foreach (var item in items)
        {
            csv.Append(item.FoodId).Append(',')
               .Append(EscapeCsvCell(item.FoodName)).Append(',')
               .Append(item.SizeId).Append(',')
               .Append(EscapeCsvCell(item.SizeName)).Append(',')
               .Append(item.CategoryId).Append(',')
               .Append(item.QuantitySold).Append(',')
               .Append(EscapeCsvCell(item.Revenue.ToString("0.##", CultureInfo.InvariantCulture))).Append(',')
               .Append(EscapeCsvCell(item.Cogs.ToString("0.##", CultureInfo.InvariantCulture))).Append(',')
               .Append(EscapeCsvCell(item.GrossProfit.ToString("0.##", CultureInfo.InvariantCulture))).Append(',')
               .Append(EscapeCsvCell(item.GrossMarginPct.ToString("0.##", CultureInfo.InvariantCulture))).Append(',')
               .Append(item.HasMissingRecipe ? "true" : "false").Append(',')
               .Append(item.HasMissingCost ? "true" : "false")
               .Append("\r\n");
        }
    }

    private static byte[] ToUtf8BomBytes(StringBuilder csv)
    {
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
