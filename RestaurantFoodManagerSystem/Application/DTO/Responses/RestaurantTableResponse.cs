public class RestaurantTableDto
{
    public int TableId { get; set; }
    public string TableNumber { get; set; } =string.Empty;
    public int Capacity { get; set; }
    public string Status { get; set; } = string.Empty;

    /// <summary>Khu vực phục vụ; null nếu bàn chưa được phân khu.</summary>
    public int? AreaId { get; set; }
    public string AreaName { get; set; } = string.Empty;

    /// <summary>
    /// false = bàn đã bị soft-delete. Bàn vẫn được trả về trong danh sách để frontend
    /// hiển thị trạng thái "Bảo trì" thay vì ẩn mất.
    /// </summary>
    public bool IsActive { get; set; }
}
