public sealed class CustomerSummaryResponse
{
    public int UserId { get; set; }
    public int OrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public int ReservationCount { get; set; }
}
