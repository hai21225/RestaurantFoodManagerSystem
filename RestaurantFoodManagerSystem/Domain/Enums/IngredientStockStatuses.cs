/// <summary>
/// Trạng thái đáp ứng của một nguyên liệu trong kho (C-13, chốt 2026-10-21).
///
/// <para>
/// Bếp báo "thiếu" hoặc "hư" thì phải có 1 trạng thái gắn vào nguyên liệu để kho quản lý
/// và báo cáo — không chỉ ghi 1 dòng ledger rồi bỏ đi. Nguyên liệu vẫn được giữ nguyên
/// (không xoá) làm minh chứng cho báo cáo hao hụt.
/// </para>
///
/// <para>
/// Phạm vi: ở cấp <b>nguyên liệu</b>, không phải cấp lô hàng. Hệ thống chưa có khái niệm
/// lô/batch nên một lô hàng hỏng sẽ đánh dấu cả nguyên liệu.
/// </para>
///
/// <para>
/// Đặt ở <c>Domain/Enums</c> vì <see cref="Ingredient"/> (entity) dùng hằng này làm giá trị
/// mặc định — Domain không được phụ thuộc vào Application.
/// </para>
/// </summary>
public static class IngredientStockStatuses
{
    /// <summary>Bình thường.</summary>
    public const string Available = "Available";

    /// <summary>Hết hàng — bếp báo thiếu, kho cần mua bổ sung.</summary>
    public const string OutOfStock = "OutOfStock";

    /// <summary>Hư hỏng / hết hạn — lô hàng không còn đảm bảo đáp ứng được món.</summary>
    public const string Spoiled = "Spoiled";

    public static readonly string[] All = [Available, OutOfStock, Spoiled];

    public static bool IsValid(string? status) =>
        status is not null && All.Contains(status, StringComparer.OrdinalIgnoreCase);

    /// <summary>Chuẩn hoá về tên hằng; sai thì ném lỗi để API trả 400.</summary>
    public static string Normalize(string? status)
    {
        var normalized = status?.Trim() ?? string.Empty;
        var match = All.FirstOrDefault(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));

        return match
            ?? throw new ArgumentException(
                $"Stock status must be one of {string.Join(", ", All)}");
    }
}
