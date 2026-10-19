using System.Collections.Concurrent;

/// <summary>
/// Lưu kết quả <c>POST /api/orders</c> theo <c>Idempotency-Key</c> để client retry
/// không tạo thêm đơn/trừ kho lần hai.
/// </summary>
public interface IOrderIdempotencyStore
{
    Task<OrderCreatedResponse> ExecuteAsync(
        string key,
        string requestFingerprint,
        Func<CancellationToken, Task<OrderCreatedResponse>> create,
        CancellationToken ct);
}

/// <summary>
/// Bản lưu in-memory, TTL 24 giờ. Phù hợp cho một instance backend nội bộ.
///
/// Giới hạn: restart process hoặc chạy nhiều instance sẽ làm mất cache. Nếu cần
/// idempotency bền vững/multi-instance thì phải thêm bảng hoặc cột idempotency
/// vào DB (schema change) và thay implementation này.
/// </summary>
public sealed class InMemoryOrderIdempotencyStore : IOrderIdempotencyStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private const int LockStripeCount = 64;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim[] _locks =
        Enumerable.Range(0, LockStripeCount)
            .Select(_ => new SemaphoreSlim(1, 1))
            .ToArray();

    public async Task<OrderCreatedResponse> ExecuteAsync(
        string key,
        string requestFingerprint,
        Func<CancellationToken, Task<OrderCreatedResponse>> create,
        CancellationToken ct)
    {
        var gate = _locks[(key.GetHashCode() & int.MaxValue) % _locks.Length];
        await gate.WaitAsync(ct);

        try
        {
            if (_entries.TryGetValue(key, out var existing))
            {
                if (existing.ExpiresAt > DateTimeOffset.UtcNow)
                {
                    if (!string.Equals(existing.Fingerprint, requestFingerprint, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Idempotency key was already used with a different order payload");
                    }

                    return existing.Response;
                }

                _entries.TryRemove(key, out _);
            }

            var response = await create(ct);
            _entries[key] = new Entry(requestFingerprint, response, DateTimeOffset.UtcNow.Add(Ttl));

            if (_entries.Count > 2048)
            {
                RemoveExpired();
            }

            return response;
        }
        finally
        {
            gate.Release();
        }
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
            {
                _entries.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record Entry(
        string Fingerprint,
        OrderCreatedResponse Response,
        DateTimeOffset ExpiresAt);
}
