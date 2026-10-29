using System.ComponentModel.DataAnnotations;

/// <summary>Body của <c>POST/PUT /api/table-areas</c>.</summary>
public sealed class TableAreaRequest
{
    [Required, MaxLength(100)]
    public string AreaName { get; set; } = string.Empty;
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}
