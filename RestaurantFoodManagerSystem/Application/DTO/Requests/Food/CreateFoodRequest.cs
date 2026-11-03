using System.ComponentModel.DataAnnotations;

public class CreateFoodRequest
{
    [Required]
    public int CategoryId { get; set; }

    [Required, MaxLength(150)]
    public string FoodName { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string FoodDescription { get; set; } = string.Empty;

    [MaxLength(500)]
    public string FoodImageUrl { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal FoodPrice { get; set; }

    public bool IsAvailable { get; set; } = true;
}