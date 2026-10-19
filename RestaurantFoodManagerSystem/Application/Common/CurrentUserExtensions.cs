using Microsoft.Extensions.Logging;

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
}
