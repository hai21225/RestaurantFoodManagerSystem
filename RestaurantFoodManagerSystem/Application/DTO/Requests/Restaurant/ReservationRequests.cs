using System.ComponentModel.DataAnnotations;

public sealed class CreateReservationRequest
{
    [Range(1, int.MaxValue)] public int TableId { get; set; }
    [Required, MaxLength(150)] public string CustomerName { get; set; } = string.Empty;
    [Required, MaxLength(20)] public string CustomerPhone { get; set; } = string.Empty;
    [Range(1, int.MaxValue)] public int PartySize { get; set; }
    public DateTime ReservedFrom { get; set; }
    public DateTime ReservedTo { get; set; }
    [Range(0, double.MaxValue)] public decimal DepositAmount { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public int? CustomerUserId { get; set; }
}

public sealed class ReservationListQuery
{
    public DateTime? Date { get; set; }
    public string? Status { get; set; }
    public int? TableId { get; set; }
}

public sealed class CancelReservationRequest
{
    [MaxLength(500)] public string? Reason { get; set; }
}

public sealed class ConfirmDepositRequest
{
    [MaxLength(100)] public string? TransactionCode { get; set; }
}
