/// <summary>
/// Khoảng thời gian báo cáo tính theo giờ Việt Nam (UTC+7, không có DST).
///
/// <para>
/// DB lưu UTC; người dùng nghĩ theo "ngày/tuần/tháng" giờ VN. Helper này quy đổi hai chiều
/// và cắt bucket cho báo cáo (giờ cho <c>day</c>, ngày cho <c>week</c>/<c>month</c>).
/// </para>
/// </summary>
public static class VietnamTime
{
    public const string TimeZoneId = "Asia/Ho_Chi_Minh";
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    /// <summary>Chuẩn hoá chuỗi kỳ báo cáo; ném <see cref="ArgumentException"/> nếu không hợp lệ.</summary>
    public static string NormalizePeriod(string? period)
    {
        var value = string.IsNullOrWhiteSpace(period) ? "day" : period.Trim().ToLowerInvariant();
        if (value is not ("day" or "week" or "month"))
            throw new ArgumentException("period must be one of: day, week, month");
        return value;
    }

    /// <summary>
    /// Cắt kỳ báo cáo chứa <paramref name="date"/> (ngày giờ VN; mặc định hôm nay theo giờ VN).
    /// <c>FromUtc</c> bao gồm, <c>ToUtc</c> loại trừ.
    ///
    /// <para>
    /// Mốc trả về luôn có <c>Kind</c> rõ ràng: <c>FromUtc</c>/<c>ToUtc</c> là <c>Utc</c>
    /// (JSON có đuôi <c>Z</c>), còn <c>StartVn</c>/<c>EndVn</c> là giờ VN
    /// (<c>Unspecified</c>). Trước đây <c>Kind</c> lúc có lúc không tuỳy kỳ/tham số
    /// ⇒ cùng một ý nghĩa lại trả về hai định dạng JSON khác nhau.
    /// </para>
    /// </summary>
    public static (string Period, DateTime FromUtc, DateTime ToUtc, DateTime StartVn, DateTime EndVn) Resolve(string? period, DateTime? date)
    {
        var normalized = NormalizePeriod(period);
        var dateVn = ToVnDate(date);

        var (startVn, endVn) = normalized switch
        {
            "day" => (dateVn, dateVn.AddDays(1)),
            "week" => (dateVn.AddDays(-(((int)dateVn.DayOfWeek + 6) % 7)), dateVn.AddDays(-(((int)dateVn.DayOfWeek + 6) % 7)).AddDays(7)),
            _ => (new DateTime(dateVn.Year, dateVn.Month, 1), new DateTime(dateVn.Year, dateVn.Month, 1).AddMonths(1))
        };

        return (normalized, ToUtc(startVn), ToUtc(endVn), startVn, endVn);
    }

    /// <summary>
    /// Ngày theo giờ VN từ tham số <c>date</c>. Giá trị không kèm múi giờ được hiểu là
    /// giờ VN (đúng như tài liệu API); giá trị UTC/Local được quy đổi sang VN.
    /// </summary>
    private static DateTime ToVnDate(DateTime? date)
    {
        var vn = date switch
        {
            null => DateTime.UtcNow + Offset,
            { Kind: DateTimeKind.Utc } d => d + Offset,
            { Kind: DateTimeKind.Local } d => d.ToUniversalTime() + Offset,
            _ => date.Value
        };

        return DateTime.SpecifyKind(vn.Date, DateTimeKind.Unspecified);
    }

    /// <summary>Chuyển một mốc thời gian giờ VN sang UTC (kèm <c>Kind = Utc</c>).</summary>
    public static DateTime ToUtc(DateTime vn) =>
        DateTime.SpecifyKind(vn - Offset, DateTimeKind.Utc);

    /// <summary>Bắt đầu bucket chứa <paramref name="utc"/> (UTC), trả về theo giờ VN.</summary>
    public static DateTime BucketStartVn(DateTime utc, string normalizedPeriod)
    {
        var vn = utc + Offset;
        return normalizedPeriod == "day"
            ? new DateTime(vn.Year, vn.Month, vn.Day, vn.Hour, 0, 0)
            : vn.Date;
    }

    /// <summary>Bucket kế tiếp theo kỳ báo cáo.</summary>
    public static DateTime NextBucketVn(DateTime bucketVn, string normalizedPeriod) =>
        normalizedPeriod == "day" ? bucketVn.AddHours(1) : bucketVn.AddDays(1);

    /// <summary>Nhãn hiển thị của bucket (giờ VN).</summary>
    public static string BucketLabel(DateTime bucketVn, string normalizedPeriod) =>
        normalizedPeriod == "day" ? bucketVn.ToString("HH:00") : bucketVn.ToString("dd/MM");
}
