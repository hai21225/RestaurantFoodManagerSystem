using System.ComponentModel.DataAnnotations;

public class UpdateFoodSizeRequest
{
    [Required]
    public string SizeName { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal SizePrice { get; set; }

    public bool IsDefault { get; set; }
}