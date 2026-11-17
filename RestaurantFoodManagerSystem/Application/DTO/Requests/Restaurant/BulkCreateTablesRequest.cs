public sealed class BulkCreateTablesRequest
{
    public string Prefix { get; set; } = string.Empty;
    public int StartNumber { get; set; }
    public int EndNumber { get; set; }
    public int? AreaId { get; set; }
    public int Capacity { get; set; }
}
