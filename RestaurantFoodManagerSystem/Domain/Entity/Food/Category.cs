public class Category
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } =string.Empty;
    public string CategoryDescription { get; set; } = string.Empty;
    public bool DisplayOrder { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}