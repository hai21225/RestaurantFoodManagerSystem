using System.ComponentModel.DataAnnotations;

public sealed class KitchenItemStatusRequest
{
    [Required] public string Status { get; set; } = string.Empty;
}
