public class RestaurantTable
{
    public int TableId { get; set; }
    public string TableNumer { get; set; } = string.Empty;
    public int Capacity { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }

}