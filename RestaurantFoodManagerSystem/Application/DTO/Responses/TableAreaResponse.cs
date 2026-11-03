/// <summary>Kết quả của <c>GET/POST/PUT /api/table-areas</c>.</summary>
public sealed class TableAreaResponse
{
    public int AreaId { get; set; }
    public string AreaName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }

    public static TableAreaResponse From(TableArea x) => new()
    {
        AreaId = x.AreaId,
        AreaName = x.AreaName,
        Description = x.Description,
        DisplayOrder = x.DisplayOrder,
        IsActive = x.IsActive
    };
}
