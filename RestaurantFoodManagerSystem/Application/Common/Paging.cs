/// <summary>
/// Quy ước phân trang dùng chung cho các endpoint danh sách mới (P1).
/// <c>page</c> tính từ 1; <c>pageSize</c> trong khoảng 1..<see cref="MaxPageSize"/>.
/// </summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Số bản ghi cần bỏ qua (<c>Skip</c>), hoặc ném <see cref="ArgumentException"/>
    /// để controller map thành HTTP 400.
    /// </summary>
    public static int ResolveSkip(int page, int pageSize)
    {
        if (page < 1)
            throw new ArgumentException("page must be greater than or equal to 1");
        if (pageSize < 1 || pageSize > MaxPageSize)
            throw new ArgumentException($"pageSize must be between 1 and {MaxPageSize}");

        return (page - 1) * pageSize;
    }
}
