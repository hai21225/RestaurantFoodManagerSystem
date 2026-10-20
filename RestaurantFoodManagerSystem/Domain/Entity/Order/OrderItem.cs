public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int SizeId { get; set; }
    public int Quanlity { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Nhóm các dòng được tách từ cùng một món gọi ban đầu (Cách A).
    /// Null với dữ liệu cũ; khi tách dòng, cả dòng gốc và dòng mới dùng chung giá trị này.
    /// </summary>
    public Guid? OrderItemGroupId { get; set; }

    /// <summary>Lý do huỷ dòng món (bắt buộc khi huỷ món). Null với dòng còn hoạt động.</summary>
    public string? CancelReason { get; set; }

    /// <summary>Thời điểm huỷ dòng món (UTC).</summary>
    public DateTime? CancelledAt { get; set; }

    /// <summary>Người huỷ dòng món (<c>Users.Id</c>).</summary>
    public int? CancelledBy { get; set; }
}