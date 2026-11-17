public sealed class ReservationResponse
{
    public int ReservationId { get; set; }
    public int TableId { get; set; }
    public string? TableNumber { get; set; }
    public int? CustomerUserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public int PartySize { get; set; }
    public DateTime ReservedFrom { get; set; }
    public DateTime ReservedTo { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal DepositAmount { get; set; }
    public string DepositStatus { get; set; } = string.Empty;
    public string? DepositTransactionCode { get; set; }
    public int? OrderId { get; set; }
    public string? Note { get; set; }
    public string? CancelReason { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? SeatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public static ReservationResponse From(TableReservation r, string? tableNumber = null) => new()
    {
        ReservationId = r.ReservationId,
        TableId = r.TableId,
        TableNumber = tableNumber,
        CustomerUserId = r.CustomerUserId,
        CustomerName = r.CustomerName,
        CustomerPhone = r.CustomerPhone,
        PartySize = r.PartySize,
        ReservedFrom = r.ReservedFrom,
        ReservedTo = r.ReservedTo,
        Status = r.Status,
        DepositAmount = r.DepositAmount,
        DepositStatus = r.DepositStatus,
        DepositTransactionCode = r.DepositTransactionCode,
        OrderId = r.OrderId,
        Note = r.Note,
        CancelReason = r.CancelReason,
        ConfirmedAt = r.ConfirmedAt,
        SeatedAt = r.SeatedAt,
        CompletedAt = r.CompletedAt,
        CancelledAt = r.CancelledAt,
        CreatedAt = r.CreatedAt
    };
}

public sealed class ReservationMomoDepositResponse
{
    public int ReservationId { get; set; }
    public string RequestId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string RedirectUrl { get; set; } = string.Empty;
    public string QrCodeUrl { get; set; } = string.Empty;
    public bool IsMock { get; set; }
}
