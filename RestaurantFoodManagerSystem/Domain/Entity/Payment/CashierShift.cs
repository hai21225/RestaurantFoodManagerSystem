public static class CashierShiftStatuses
{
    public const string Open = "Open";
    public const string Closed = "Closed";
}

/// <summary>
/// Ca làm việc và đối soát két tiền của thu ngân (V2).
/// </summary>
public class CashierShift
{
    public int ShiftId { get; set; }
    public int CashierUserId { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? ActualCash { get; set; }
    public decimal? Variance { get; set; }
    public string Status { get; set; } = CashierShiftStatuses.Open;
    public decimal CashTotal { get; set; }
    public decimal CardTotal { get; set; }
    public decimal MomoTotal { get; set; }
    public decimal OtherTotal { get; set; }
    public decimal RefundTotal { get; set; }
    public string? Note { get; set; }

    /// <summary>
    /// Concurrency token. Trên SQL Server là <c>rowversion</c>; trên SQLite do <c>AppDbContext</c> tự cập nhật.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
