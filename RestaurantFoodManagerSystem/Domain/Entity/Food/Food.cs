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

    /// <summary>
    /// Lý do món bị vô hiệu hoá (ví dụ bếp báo lô nguyên liệu hư/hết hạn nên ngừng bán món này).
    /// Giữ lại để làm báo cáo; không xoá khi bật lại món.
    /// </summary>
    public string? InactiveReason { get; set; }

    /// <summary>Mốc thời gian món bị vô hiệu hoá (UTC).</summary>
    public DateTime? InactiveAt { get; set; }
}