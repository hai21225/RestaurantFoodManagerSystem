/// <summary>
/// Người dùng đang gọi request (GĐ6c).
///
/// Application chỉ biết abstraction này; implementation đọc claim từ JWT nằm ở tầng
/// Infrastructure (<c>HttpContextCurrentUser</c>) nên Application không phụ thuộc ASP.NET.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// <c>Users.Id</c> lấy từ claim <c>sub</c> của token.
    /// <c>null</c> khi chưa xác thực (ví dụ <c>Auth:Enforce=false</c> hoặc request không có token)
    /// ⇒ service sẽ dùng giá trị actor từ body như trước GĐ6.
    /// </summary>
    int? UserId { get; }

    /// <summary>
    /// Các role của người dùng hiện tại lấy từ claim (GĐ6). Rỗng khi chưa xác thực.
    /// Service dùng để áp luật nghiệp vụ chi tiết hơn policy ở controller (ví dụ bếp được
    /// huỷ món đang nấu nhưng không được huỷ món đã ra đĩa).
    /// </summary>
    IReadOnlyCollection<string> Roles { get; }
}
