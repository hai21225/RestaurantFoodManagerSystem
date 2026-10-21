public class Category
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } =string.Empty;
    public string CategoryDescription { get; set; } = string.Empty;
    /// <summary>
    /// Thứ tự hiển thị của danh mục (Đợt 2 / C1). Số nhỏ hiển thị trước.
    /// Trước đây là <c>bool</c> nên chỉ sắp được 2 vị trí, không sắp xếp được menu.
    /// </summary>
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}