using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

public sealed class CreateOrderRequest
{
    [Range(1, int.MaxValue)] public int TableId { get; set; }
    [Range(1, int.MaxValue)] public int CreatedBy { get; set; }
    [MinLength(1)] public List<CreateOrderItemRequest> Items { get; set; } = [];
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)] public int SizeId { get; set; }
    // C# dùng tên Quanlity theo quy ước của project/DB; JSON vẫn là "quantity" để không phá frontend.
    [Range(1, int.MaxValue)] [JsonPropertyName("quantity")] public int Quanlity { get; set; }
    public string? Note { get; set; }
    public List<CreateOrderToppingRequest> Toppings { get; set; } = [];
}

public sealed class CreateOrderToppingRequest
{
    [Range(1, int.MaxValue)] public int ToppingId { get; set; }
    [Range(1, int.MaxValue)] [JsonPropertyName("quantity")] public int Quanlity { get; set; } = 1;
}

public sealed class UpdateOrderStatusRequest
{
    [Required] public string Status { get; set; } = string.Empty;
    public int ChangedBy { get; set; }
    public string? Note { get; set; }
}

public sealed class CreatePaymentRequest
{
    [Range(1, int.MaxValue)] public int OrderId { get; set; }
    [Required] public string PaymentMethod { get; set; } = string.Empty;
    public string? TransactionCode { get; set; }
    [Range(1, int.MaxValue)] public int CreatedBy { get; set; }
}
