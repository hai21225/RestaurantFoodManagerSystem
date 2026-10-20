using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Hợp nhất nguồn actor giữa claim token và body (GĐ6c, bước 1).
///
/// Quy tắc:
/// - Có actor trong token ⇒ **luôn dùng token**, bỏ qua giá trị body (log cảnh báo nếu lệch).
/// - Chưa có token ⇒ giữ giá trị body để không phá vỡ luồng dev/frontend hiện tại.
/// </summary>
public static class CurrentUserExtensions
{
    public static int ResolveActorId(
        this ICurrentUser currentUser,
        int bodyValue,
        ILogger logger,
        string fieldName)
    {
        var claimedUserId = currentUser.UserId;

        if (claimedUserId is null)
        {
            return bodyValue;
        }

        if (bodyValue != 0 && bodyValue != claimedUserId.Value)
        {
            logger.LogWarning(
                "{Field} trong body ({BodyValue}) khác actor trong token ({TokenValue}); dùng actor trong token.",
                fieldName,
                bodyValue,
                claimedUserId.Value);
        }

        return claimedUserId.Value;
    }

    /// <summary>
    /// Resolve actor rồi kiểm user có thật không.
    ///
    /// <para>
    /// Khi chưa xác thực, actor lấy từ body ⇒ client có thể gửi id không tồn tại (hoặc 0).
    /// Các cột <c>CreatedBy</c>/<c>ChangedBy</c> là FK sang <c>users</c> ⇒ lưu sẽ ném
    /// <c>DbUpdateException</c> và thành lỗi 500. Kiểm ở đây để trả 400 rõ ràng.
    /// </para>
    ///
    /// <para>
    /// <paramref name="fallbackUserId"/> dùng khi actor không hợp lệ (ví dụ đơn hàng
    /// không có token và body không gửi id) ⇒ lùi về người tạo đơn thay vì ghi id 0.
    /// </para>
    /// </summary>
    public static async Task<int> ResolveExistingActorAsync(
        this ICurrentUser currentUser,
        AppDbContext db,
        int bodyValue,
        ILogger logger,
        string fieldName,
        CancellationToken ct = default,
        int fallbackUserId = 0)
    {
        var actorId = currentUser.ResolveActorId(bodyValue, logger, fieldName);

        if (actorId > 0 && await db.Users.AnyAsync(u => u.Id == actorId, ct))
        {
            return actorId;
        }

        if (fallbackUserId > 0 && await db.Users.AnyAsync(u => u.Id == fallbackUserId, ct))
        {
            return fallbackUserId;
        }

        throw new ArgumentException($"{fieldName} does not reference an existing user");
    }
}
