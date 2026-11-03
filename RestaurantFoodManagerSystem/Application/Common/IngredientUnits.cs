using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// C-17: đọc đơn vị tính của nguyên liệu để ghi kèm vào dòng ledger kho
/// (<c>inventory_transactions.unit</c>).
///
/// <para>
/// Ledger là lịch sử bất biến: mỗi dòng phải cho biết lượng đó tính theo đơn vị nào
/// tại thời điểm ghi. Nhờ đó một báo cáo cũ vẫn đọc đúng sau này, kể cả khi nguyên liệu
/// bị ẩn hay đơn vị của nó bị sửa (hiện đã chặn — xem
/// <c>IngredientService.UpdateAsync</c>).
/// </para>
///
/// <para>
/// Không tự mở transaction/SaveChanges. Caller đang giữ transaction chịu trách nhiệm lưu.
/// </para>
/// </summary>
internal static class IngredientUnits
{
    /// <summary>
    /// Lấy đơn vị của các nguyên liệu theo id. Nguyên liệu không tồn tại (hoặc đã bị
    /// xoá mềm) thì không có entry trong kết quả ⇒ caller ghi <c>null</c> cho dòng ledger
    /// tương ứng, thay vì làm hỏng cả lô giao dịch.
    /// </summary>
    public static async Task<Dictionary<int, string>> LoadAsync(
        AppDbContext db,
        IEnumerable<int> ingredientIds,
        CancellationToken ct)
    {
        var ids = ingredientIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Ingredients
            .AsNoTracking()
            .Where(x => ids.Contains(x.IngredientId))
            .ToDictionaryAsync(x => x.IngredientId, x => x.Unit, ct);
    }
}
