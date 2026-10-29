using System.Collections.Concurrent;

/// <summary>
/// Lưu kết quả của các lệnh ghi theo <c>Idempotency-Key</c> để client retry
/// không tạo thêm đơn/trừ kho/thu tiền lần hai (Đợt 2 / A5).
///
/// <para>
/// Áp dụng cho các lệnh tiền–số lượng: <c>POST /api/orders</c>, <c>POST /api/orders/{id}/items</c>,
/// <c>PATCH /api/orders/{id}/items/{itemId}/quantity</c>, <c>POST /api/payments</c>.
/// </para>
/// </summary>
public interface IOrderIdempotencyStore
{
    /// <summary>
    /// Chạy <paramref name="create"/> đúng một lần cho mỗi <paramref name="key"/>.
    /// Cùng key + cùng payload ⇒ trả lại kết quả cũ; cùng key + payload khác ⇒ ném lỗi.
    /// </summary>
    Task<TResponse> ExecuteAsync<TResponse>(
        string key,
        string requestFingerprint,
        Func<CancellationToken, Task<TResponse>> create,
        CancellationToken ct);
}

/// <summary>
/// Bản lưu in-memory, TTL 24 giờ. Phù hợp cho một instance backend nội bộ.
///
/// <para>
/// Giới hạn: restart process hoặc chạy nhiều instance sẽ làm mất cache. Nếu cần
/// idempotency bền vững/multi-instance thì phải thêm bảng hoặc cột idempotency
/// vào DB (schema change) và thay implementation này.
/// </para>
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

    public async Task<TResponse> ExecuteAsync<TResponse>(
        string key,
        string requestFingerprint,
        Func<CancellationToken, Task<TResponse>> create,
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
                            "Idempotency key was already used with a different request payload");
                    }

                    if (existing.Response is TResponse cached)
                    {
                        return cached;
                    }

                    return default!;
                }

                _entries.TryRemove(key, out _);
            }

            var response = await create(ct);
            _entries[key] = new Entry(requestFingerprint, response!, DateTimeOffset.UtcNow.Add(Ttl));

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
        object Response,
        DateTimeOffset ExpiresAt);
}
