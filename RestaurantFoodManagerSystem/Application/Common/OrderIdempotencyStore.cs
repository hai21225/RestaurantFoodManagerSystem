using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

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

public static class OrderIdempotencyStore
{
    /// <summary>
    /// Fingerprint của payload: SHA-256 của <c>scope + JSON payload</c>. Cùng key nhưng
    /// payload khác ⇒ báo lỗi (409) thay vì âm thầm trả kết quả cũ.
    /// </summary>
    public static string BuildFingerprint(string scope, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes($"{scope}|{json}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static Task<TResponse> RunIfKeyedAsync<TResponse>(
        this IOrderIdempotencyStore store,
        string? idempotencyKey,
        int? actorId,
        string fingerprintScope,
        string cacheKeyPrefix,
        object payload,
        Func<CancellationToken, Task<TResponse>> action,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return action(ct);
        }

        var key = idempotencyKey.Trim();
        if (key.Length > 128)
        {
            throw new ArgumentException("Idempotency-Key must be at most 128 characters");
        }

        var fingerprint = BuildFingerprint(fingerprintScope, payload);
        return store.ExecuteAsync($"{actorId}:{cacheKeyPrefix}{key}", fingerprint, action, ct);
    }
}

/// <summary>
/// Hybrid 2-tier Idempotency store (TTL 24h):
/// Tier 1: In-memory per-key singleflight (<see cref="ConcurrentDictionary{TKey, TValue}"/> + <see cref="Lazy{T}"/>)
/// để các key khác nhau chạy song song hoàn toàn.
/// Tier 2: Bảng <c>idempotency_keys</c> trong DB (khi có <see cref="IServiceScopeFactory"/>)
/// để bền vững qua restart / multi-instance.
/// </summary>
public sealed class InMemoryOrderIdempotencyStore : IOrderIdempotencyStore
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly TimeSpan InProgressLease = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ConcurrentDictionary<string, Lazy<Task<Entry>>> _entries = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ILogger<InMemoryOrderIdempotencyStore>? _logger;

    public InMemoryOrderIdempotencyStore(
        IServiceScopeFactory? scopeFactory = null,
        ILogger<InMemoryOrderIdempotencyStore>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<TResponse> ExecuteAsync<TResponse>(
        string key,
        string requestFingerprint,
        Func<CancellationToken, Task<TResponse>> create,
        CancellationToken ct)
    {
        while (true)
        {
            var lazy = _entries.GetOrAdd(
                key,
                k => new Lazy<Task<Entry>>(
                    () => ExecuteCoreAsync(k, requestFingerprint, create, ct),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            Entry entry;
            try
            {
                entry = await lazy.Value;
            }
            catch
            {
                // Evict faulted/cancelled Lazy so subsequent retries are not poisoned.
                _entries.TryRemove(KeyValuePair.Create(key, lazy));
                throw;
            }

            if (entry.ExpiresAt <= DateTimeOffset.UtcNow || entry.Response is null)
            {
                _entries.TryRemove(KeyValuePair.Create(key, lazy));
                if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    continue;
                }
            }

            if (!string.Equals(entry.Fingerprint, requestFingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Idempotency key was already used with a different request payload");
            }

            if (entry.Response is not null)
            {
                EvictExpiredIfNeeded();
            }

            return entry.Response is TResponse cached ? cached : default!;
        }
    }

    private async Task<Entry> ExecuteCoreAsync<TResponse>(
        string key,
        string requestFingerprint,
        Func<CancellationToken, Task<TResponse>> create,
        CancellationToken ct)
    {
        if (_scopeFactory is null)
        {
            var response = await create(ct);
            return new Entry(requestFingerprint, response, DateTimeOffset.UtcNow.Add(Ttl));
        }

        var (keyScope, dbKey, actorId) = ParseKey(key);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var row = await db.IdempotencyKeys
            .FirstOrDefaultAsync(x => x.Scope == keyScope && x.Key == dbKey, ct);

        if (row is not null)
        {
            if (string.Equals(row.Status, IdempotencyKeyStatuses.Completed, StringComparison.Ordinal) &&
                row.ExpiresAt > now)
            {
                return BuildEntryFromCompleted<TResponse>(row, requestFingerprint);
            }

            if (string.Equals(row.Status, IdempotencyKeyStatuses.InProgress, StringComparison.Ordinal) &&
                row.ExpiresAt > now &&
                row.CreatedAt.Add(InProgressLease) > now)
            {
                throw new InvalidOperationException("Idempotency key is currently being processed");
            }

            // Expired Completed or expired InProgress lease: take over the row.
            row.ActorId = actorId;
            row.RequestFingerprint = requestFingerprint;
            row.Status = IdempotencyKeyStatuses.InProgress;
            row.ResponseJson = null;
            row.StatusCode = null;
            row.CreatedAt = now;
            row.ExpiresAt = now.Add(Ttl);
            await db.SaveChangesAsync(ct);
        }
        else
        {
            row = new IdempotencyKey
            {
                Scope = keyScope,
                Key = dbKey,
                ActorId = actorId,
                RequestFingerprint = requestFingerprint,
                Status = IdempotencyKeyStatuses.InProgress,
                CreatedAt = now,
                ExpiresAt = now.Add(Ttl)
            };
            db.IdempotencyKeys.Add(row);

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two instances/threads raced past the read on unique index (Scope, Key).
                db.Entry(row).State = EntityState.Detached;
                var raced = await db.IdempotencyKeys
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Scope == keyScope && x.Key == dbKey, ct);

                if (raced is not null &&
                    string.Equals(raced.Status, IdempotencyKeyStatuses.Completed, StringComparison.Ordinal) &&
                    raced.ExpiresAt > now)
                {
                    return BuildEntryFromCompleted<TResponse>(raced, requestFingerprint);
                }

                throw new InvalidOperationException("Idempotency key is currently being processed");
            }
        }

        TResponse result;
        try
        {
            result = await create(ct);
        }
        catch
        {
            try
            {
                db.IdempotencyKeys.Remove(row);
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception cleanupEx)
            {
                _logger?.LogWarning(cleanupEx, "Failed to clean up InProgress idempotency key {Scope}/{Key}", keyScope, dbKey);
            }
            throw;
        }

        var completedAt = DateTime.UtcNow;
        row.Status = IdempotencyKeyStatuses.Completed;
        row.ResponseJson = result is null ? null : JsonSerializer.Serialize(result, JsonOptions);
        row.StatusCode = 200;
        row.ExpiresAt = completedAt.Add(Ttl);
        await db.SaveChangesAsync(CancellationToken.None);

        return new Entry(requestFingerprint, result, new DateTimeOffset(DateTime.SpecifyKind(row.ExpiresAt, DateTimeKind.Utc)));
    }

    private static Entry BuildEntryFromCompleted<TResponse>(IdempotencyKey row, string requestFingerprint)
    {
        if (!string.Equals(row.RequestFingerprint, requestFingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Idempotency key was already used with a different request payload");
        }

        object? cached = string.IsNullOrEmpty(row.ResponseJson)
            ? default(TResponse)
            : JsonSerializer.Deserialize<TResponse>(row.ResponseJson, JsonOptions);

        return new Entry(
            requestFingerprint,
            cached,
            new DateTimeOffset(DateTime.SpecifyKind(row.ExpiresAt, DateTimeKind.Utc)));
    }

    private static (string Scope, string Key, int? ActorId) ParseKey(string rawKey)
    {
        int? actorId = null;
        var rest = rawKey;
        var firstColon = rawKey.IndexOf(':');
        if (firstColon >= 0)
        {
            if (int.TryParse(rawKey[..firstColon], out var parsed))
            {
                actorId = parsed;
            }
            rest = rawKey[(firstColon + 1)..];
        }

        var scope = rest.StartsWith("payments:", StringComparison.Ordinal) ? "payments" : "orders";
        var dbKey = rawKey.Length <= 200
            ? rawKey
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));
        return (scope, dbKey, actorId);
    }

    private void EvictExpiredIfNeeded()
    {
        if (_entries.Count <= 2048) return;

        var cutoff = DateTimeOffset.UtcNow;
        foreach (var kv in _entries)
        {
            if (kv.Value.IsValueCreated &&
                kv.Value.Value.IsCompletedSuccessfully &&
                kv.Value.Value.Result.ExpiresAt <= cutoff)
            {
                _entries.TryRemove(KeyValuePair.Create(kv.Key, kv.Value));
            }
        }
    }

    private sealed record Entry(
        string Fingerprint,
        object? Response,
        DateTimeOffset ExpiresAt);
}
