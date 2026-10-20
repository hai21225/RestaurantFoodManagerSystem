using System.ComponentModel.DataAnnotations;

public class CategoryDTO
{
    public int Id { get; set; }
    [Required, MaxLength(100)]
    public string? Name { get; set; }
    [MaxLength(500)]
    public string? Description { get; set; }
    public bool DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}