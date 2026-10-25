public class Food
{
    public int FoodId { get; set; }
    public int CategoryId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string FoodDescription { get; set; } = string.Empty;
    public string FoodImageUrl { get; set; } = string.Empty;
    public decimal FoodPrice { get; set; }
    public  bool IsAvailable { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? InactiveReason { get; set; }
    public DateTime? InactiveAt { get; set; }
}