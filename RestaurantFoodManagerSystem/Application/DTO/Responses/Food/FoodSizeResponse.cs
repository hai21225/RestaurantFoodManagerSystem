public class FoodSizeResponseDto
{
    public int SizeId { get; set; }

    public int FoodId { get; set; }

    public string SizeName { get; set; } = string.Empty;

    public decimal SizePrice { get; set; }

    public bool IsActive { get; set; }

    public bool IsDefault { get; set; }
}