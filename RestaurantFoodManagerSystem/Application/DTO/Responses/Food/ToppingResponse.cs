public class ToppingResponseDto
{
    public int ToppingId { get; set; }

    public string ToppingName { get; set; } = string.Empty;

    public decimal ToppingPrice { get; set; }

    public bool IsActive { get; set; }

    public bool IsAvailable { get; set; }
}