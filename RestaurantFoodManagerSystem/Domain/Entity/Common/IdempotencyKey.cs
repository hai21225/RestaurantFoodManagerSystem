public static class IdempotencyKeyStatuses
{
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
}

/// <summary>
/// Bản ghi lưu trạng thái &amp; kết quả Idempotency-Key trong DB (V2 Hybrid Idempotency).
/// </summary>
public class IdempotencyKey
{
    public long Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int? ActorId { get; set; }
    public string RequestFingerprint { get; set; } = string.Empty;
    public string Status { get; set; } = IdempotencyKeyStatuses.InProgress;
    public string? ResponseJson { get; set; }
    public int? StatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
