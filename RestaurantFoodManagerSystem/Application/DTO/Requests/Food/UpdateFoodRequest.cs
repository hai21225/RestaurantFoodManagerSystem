using System.ComponentModel.DataAnnotations;

public class UpdateFoodRequest
{
    [Required]
    public int CategoryId { get; set; }

    [Required]
    public string FoodName { get; set; } = string.Empty;

    public string FoodDescription { get; set; } = string.Empty;

    public string FoodImageUrl { get; set; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal FoodPrice { get; set; }

    public bool IsAvailable { get; set; }
}