public static class TableReservationStatuses
{
    public const string Pending = "Pending";
    public const string Confirmed = "Confirmed";
    public const string Seated = "Seated";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
    public const string Completed = "Completed";
}

public static class DepositStatuses
{
    public const string None = "None";
    public const string Pending = "Pending";
    public const string Paid = "Paid";
    public const string AppliedToOrder = "AppliedToOrder";
    public const string Refunded = "Refunded";
    public const string Forfeited = "Forfeited";
}

/// <summary>
/// Đặt bàn trước kèm thông tin đặt cọc (V2).
/// </summary>
public class TableReservation
{
    public int ReservationId { get; set; }
    public int TableId { get; set; }
    public int? CustomerUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int PartySize { get; set; }
    public DateTime ReservedFrom { get; set; }
    public DateTime ReservedTo { get; set; }
    public string Status { get; set; } = TableReservationStatuses.Pending;
    public decimal DepositAmount { get; set; }
    public string DepositStatus { get; set; } = DepositStatuses.None;
    public string? DepositTransactionCode { get; set; }
    public int? OrderId { get; set; }
    public string? Note { get; set; }
    public string? CancelReason { get; set; }
    public int? ConfirmedBy { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? SeatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int? CancelledBy { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Concurrency token. Trên SQL Server là <c>rowversion</c>; trên SQLite do <c>AppDbContext</c> tự cập nhật.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
