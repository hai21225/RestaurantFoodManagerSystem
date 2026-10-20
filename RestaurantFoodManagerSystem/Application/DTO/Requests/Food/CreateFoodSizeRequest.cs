using System.ComponentModel.DataAnnotations;

public class CreateFoodSizeRequest
{
    [Required, MaxLength(50)]
    public string SizeName { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal SizePrice { get; set; }

    public bool IsDefault { get; set; }
}