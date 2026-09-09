public class FoodSize
{
    public int SizeId { get; set; }
    public int FoodId { get; set; }
    public string SizeName { get; set; }
    public decimal SizePrice { get; set; }
    public bool IsActive { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}