public class Food
{
    public int FoodId { get; set; }
    public int CategoryId { get; set; }
    public string FoodName { get; set; }
    public string FoodDescription { get; set; }
    public string FoodImageUrl { get; set; }
    public decimal FoodPrice { get; set; }
    public  bool IsAvailable { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
        
}