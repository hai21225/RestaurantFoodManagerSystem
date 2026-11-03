/// <summary>
/// Khu vực phục vụ của bàn (Sảnh chính, Lầu 2, Ngoài trời...).
/// Bàn thuộc tối đa một khu vực; khu vực bị soft-delete vẫn giữ lại để báo cáo/lịch sử.
/// </summary>
public class TableArea
{
    public int AreaId { get; set; }
    public string AreaName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}
