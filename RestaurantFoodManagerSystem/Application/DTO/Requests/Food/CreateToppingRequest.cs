using System.ComponentModel.DataAnnotations;

public class CreateToppingRequest
{
    [Required]
    public string ToppingName { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal ToppingPrice { get; set; }

    public bool IsAvailable { get; set; } = true;
}