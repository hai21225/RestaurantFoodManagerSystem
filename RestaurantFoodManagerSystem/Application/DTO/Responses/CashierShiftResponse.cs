public sealed class CashierShiftResponse
{
    public int ShiftId { get; set; }
    public int CashierUserId { get; set; }
    public string? CashierName { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public decimal ExpectedCash { get; set; }
    public decimal? ActualCash { get; set; }
    public decimal? Variance { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal CashTotal { get; set; }
    public decimal CardTotal { get; set; }
    public decimal MomoTotal { get; set; }
    public decimal OtherTotal { get; set; }
    public decimal RefundTotal { get; set; }
    public string? Note { get; set; }

    public static CashierShiftResponse From(CashierShift s, string? cashierName = null) => new()
    {
        ShiftId = s.ShiftId,
        CashierUserId = s.CashierUserId,
        CashierName = cashierName,
        OpenedAt = s.OpenedAt,
        ClosedAt = s.ClosedAt,
        OpeningCash = s.OpeningCash,
        ExpectedCash = s.ExpectedCash,
        ActualCash = s.ActualCash,
        Variance = s.Variance,
        Status = s.Status,
        CashTotal = s.CashTotal,
        CardTotal = s.CardTotal,
        MomoTotal = s.MomoTotal,
        OtherTotal = s.OtherTotal,
        RefundTotal = s.RefundTotal,
        Note = s.Note
    };
}
