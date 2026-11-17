using System.ComponentModel.DataAnnotations;

public sealed class OpenCashierShiftRequest
{
    [Range(0, double.MaxValue)] public decimal OpeningCash { get; set; }
    public int CashierUserId { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}

public sealed class CloseCashierShiftRequest
{
    [Range(0, double.MaxValue)] public decimal ActualCash { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}
