using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantFoodManagerSystem.Infrastructure.Auth;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// V2 QA Concurrency Simulation &amp; Integration Tests.
/// Uses <see cref="Task.WhenAll(IEnumerable{Task})"/> + <see cref="TaskCompletionSource"/>
/// and a temporary SQLite WAL file database for true multi-connection concurrency.
/// </summary>
public sealed class V2ConcurrencyAndIntegrationTests
{
    private sealed record IdempotencySampleResponse(int OrderId, string Status);

    /// <summary>
    /// Ensures every opened SQLite connection sets <c>PRAGMA busy_timeout=5000</c>
    /// so concurrent <c>Serializable</c> write transactions wait for lock release
    /// instead of immediately failing with SQLITE_BUSY.
    /// </summary>
    private sealed class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            ApplyBusyTimeout(connection);
        }

        public override Task ConnectionOpenedAsync(
            DbConnection connection,
            ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            ApplyBusyTimeout(connection);
            return Task.CompletedTask;
        }

        private static void ApplyBusyTimeout(DbConnection connection)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Isolated SQLite WAL file-backed <see cref="WebApplicationFactory{TEntryPoint}"/> with
    /// <c>Auth:Enforce=true</c> so concurrent HTTP requests open separate DB connections
    /// and exercise real transaction isolation.
    /// </summary>
    private sealed class WalEnforcedRestaurantApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath;
        private readonly SqliteBusyTimeoutInterceptor _busyTimeout = new();

        public WalEnforcedRestaurantApiFactory()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"rfms-v2-wal-{Guid.NewGuid():N}.db");

            using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
            {
                conn.Open();
                using var walCmd = conn.CreateCommand();
                walCmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
                walCmd.ExecuteNonQuery();
            }

            using var context = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite($"Data Source={_dbPath}")
                    .AddInterceptors(_busyTimeout)
                    .Options);
            context.Database.EnsureCreated();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting($"{DatabaseInitializationOptions.SectionName}:MigrateOnStartup", "false");
            builder.UseSetting($"{DatabaseInitializationOptions.SectionName}:SeedOnStartup", "true");
            builder.UseSetting("Auth:Enforce", "true");
            builder.UseSetting("Authentication:Jwt:Key", RestaurantApiFactory.TestJwtSigningKey);

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite($"Data Source={_dbPath}")
                           .AddInterceptors(_busyTimeout));
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                SqliteConnection.ClearAllPools();
                TryDelete(_dbPath);
                TryDelete(_dbPath + "-wal");
                TryDelete(_dbPath + "-shm");
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup of temp SQLite files.
            }
        }
    }

    private sealed class TempSqliteScopeFactory : IDisposable
    {
        private readonly string _dbPath;
        private readonly ServiceProvider _provider;

        public TempSqliteScopeFactory()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"rfms-v2-idemp-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            var interceptor = new SqliteBusyTimeoutInterceptor();
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite($"Data Source={_dbPath}")
                       .AddInterceptors(interceptor));

            _provider = services.BuildServiceProvider();

            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();

            using var scope = _provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        public IServiceScopeFactory ScopeFactory => _provider.GetRequiredService<IServiceScopeFactory>();

        public void Dispose()
        {
            _provider.Dispose();
            SqliteConnection.ClearAllPools();
            foreach (var p in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
        }
    }

    // =========================================================================
    // Test 1: SameKey_DoubleClickStorm_20Concurrent_ExecutesOnce
    // =========================================================================
    [Fact]
    public async Task SameKey_DoubleClickStorm_20Concurrent_ExecutesOnce()
    {
        var store = new InMemoryOrderIdempotencyStore();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;

        var tasks = Enumerable.Range(0, 20).Select(async _ =>
        {
            await startGate.Task;
            return await store.ExecuteAsync<IdempotencySampleResponse>(
                "1:orders:double-click-storm",
                "fp-same-payload",
                async ct =>
                {
                    Interlocked.Increment(ref executions);
                    await Task.Delay(25, ct);
                    return new IdempotencySampleResponse(777, "Pending");
                },
                CancellationToken.None);
        }).ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, executions);
        Assert.All(results, r => Assert.Equal(new IdempotencySampleResponse(777, "Pending"), r));
    }

    // =========================================================================
    // Test 2: DifferentKeys_MultiTableThroughput_10Concurrent_NoGlobalLock
    // =========================================================================
    [Fact]
    public async Task DifferentKeys_MultiTableThroughput_10Concurrent_NoGlobalLock()
    {
        const int concurrency = 10;
        var store = new InMemoryOrderIdempotencyStore();
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredCount = 0;

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            await startGate.Task;
            return await store.ExecuteAsync<int>(
                $"1:orders:table-{i}",
                $"fp-{i}",
                async ct =>
                {
                    if (Interlocked.Increment(ref enteredCount) == concurrency)
                    {
                        allEntered.TrySetResult();
                    }

                    // If a global lock existed, this would deadlock and time out after 2s.
                    await allEntered.Task.WaitAsync(TimeSpan.FromSeconds(2), ct);
                    return i;
                },
                CancellationToken.None);
        }).ToArray();

        startGate.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Equal(concurrency, enteredCount);
        Assert.Equal(Enumerable.Range(0, concurrency), results);
    }

    // =========================================================================
    // Test 3: HybridDbIdempotency_SurvivesRestart_And_RecoversExpiredLease
    // =========================================================================
    [Fact]
    public async Task HybridDbIdempotency_SurvivesRestart_And_RecoversExpiredLease()
    {
        using var dbHost = new TempSqliteScopeFactory();

        // 1. Completed row in DB survives fresh InMemoryOrderIdempotencyStore (simulated restart)
        var store1 = new InMemoryOrderIdempotencyStore(dbHost.ScopeFactory);
        var firstExec = 0;
        var initial = await store1.ExecuteAsync<IdempotencySampleResponse>(
            "5:orders:restart-key",
            "fp-restart",
            _ =>
            {
                Interlocked.Increment(ref firstExec);
                return Task.FromResult(new IdempotencySampleResponse(101, "Pending"));
            },
            CancellationToken.None);

        Assert.Equal(1, firstExec);
        Assert.Equal(101, initial.OrderId);

        var store2 = new InMemoryOrderIdempotencyStore(dbHost.ScopeFactory);
        var secondExec = 0;
        var replayed = await store2.ExecuteAsync<IdempotencySampleResponse>(
            "5:orders:restart-key",
            "fp-restart",
            _ =>
            {
                Interlocked.Increment(ref secondExec);
                return Task.FromResult(new IdempotencySampleResponse(999, "ShouldNotRun"));
            },
            CancellationToken.None);

        Assert.Equal(0, secondExec);
        Assert.Equal(initial, replayed);

        // 2. Active InProgress lease (< 30s) -> throws InvalidOperationException (409)
        //    Expired InProgress lease (> 30s) -> taken over and executed cleanly
        await using (var scope = dbHost.ScopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            db.IdempotencyKeys.AddRange(
                new IdempotencyKey
                {
                    Scope = "orders",
                    Key = "5:orders:active-lease",
                    ActorId = 5,
                    RequestFingerprint = "fp-active",
                    Status = IdempotencyKeyStatuses.InProgress,
                    CreatedAt = now.AddSeconds(-5),
                    ExpiresAt = now.AddHours(24)
                },
                new IdempotencyKey
                {
                    Scope = "orders",
                    Key = "5:orders:expired-lease",
                    ActorId = 5,
                    RequestFingerprint = "fp-expired",
                    Status = IdempotencyKeyStatuses.InProgress,
                    CreatedAt = now.AddSeconds(-45),
                    ExpiresAt = now.AddHours(24)
                });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store2.ExecuteAsync<string>(
                "5:orders:active-lease",
                "fp-active",
                _ => Task.FromResult("should-not-run"),
                CancellationToken.None));

        var recoveredExec = 0;
        var recovered = await store2.ExecuteAsync<string>(
            "5:orders:expired-lease",
            "fp-expired",
            _ =>
            {
                Interlocked.Increment(ref recoveredExec);
                return Task.FromResult("recovered-ok");
            },
            CancellationToken.None);

        Assert.Equal(1, recoveredExec);
        Assert.Equal("recovered-ok", recovered);

        await using (var verifyScope = dbHost.ScopeFactory.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.IdempotencyKeys.AsNoTracking()
                .SingleAsync(x => x.Scope == "orders" && x.Key == "5:orders:expired-lease");
            Assert.Equal(IdempotencyKeyStatuses.Completed, row.Status);
        }
    }

    // =========================================================================
    // Test 4: SameKey_DifferentPayload_ThrowsConflict
    // =========================================================================
    [Fact]
    public async Task SameKey_DifferentPayload_ThrowsConflict()
    {
        var store = new InMemoryOrderIdempotencyStore();

        // Sequential mismatch
        var ok = await store.ExecuteAsync<string>(
            "1:orders:payload-mismatch-seq",
            "fp-A",
            _ => Task.FromResult("ok-A"),
            CancellationToken.None);
        Assert.Equal("ok-A", ok);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.ExecuteAsync<string>(
                "1:orders:payload-mismatch-seq",
                "fp-B",
                _ => Task.FromResult("should-conflict"),
                CancellationToken.None));

        // Concurrent mismatch
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var t1 = Task.Run(async () =>
        {
            await startGate.Task;
            return await store.ExecuteAsync<string>(
                "1:orders:payload-mismatch-conc",
                "fp-1",
                async ct =>
                {
                    await Task.Delay(20, ct);
                    return "ok-1";
                },
                CancellationToken.None);
        });

        var t2 = Task.Run(async () =>
        {
            await startGate.Task;
            return await store.ExecuteAsync<string>(
                "1:orders:payload-mismatch-conc",
                "fp-2",
                async ct =>
                {
                    await Task.Delay(20, ct);
                    return "ok-2";
                },
                CancellationToken.None);
        });

        startGate.SetResult();

        var results = await Task.WhenAll(
            t1.ContinueWith(t => (t.IsFaulted, t.Exception?.InnerException)),
            t2.ContinueWith(t => (t.IsFaulted, t.Exception?.InnerException)));

        Assert.Equal(1, results.Count(r => !r.IsFaulted));
        Assert.Equal(1, results.Count(r => r.IsFaulted && r.InnerException is InvalidOperationException));
    }

    // =========================================================================
    // Test 5: ReservationOverlapRace_Concurrent_OnlyOneSucceeds & AdjacentSlots_BothSucceed
    // =========================================================================
    [Fact]
    public async Task ReservationOverlapRace_Concurrent_OnlyOneSucceeds_And_AdjacentSlots_BothSucceed()
    {
        using var factory = new WalEnforcedRestaurantApiFactory();
        var (customer1Id, customer1Name) = SeedUserWithRole(factory, AuthPolicies.Roles.Customer);
        var (customer2Id, customer2Name) = SeedUserWithRole(factory, AuthPolicies.Roles.Customer);
        var tableId = SeedActiveTable(factory, "T-WAL-RES", capacity: 4);

        using var client1 = CreateAuthenticatedClient(factory, customer1Id, customer1Name, AuthPolicies.Roles.Customer);
        using var client2 = CreateAuthenticatedClient(factory, customer2Id, customer2Name, AuthPolicies.Roles.Customer);

        var day = DateTime.UtcNow.Date.AddDays(2);
        var slot18To20From = day.AddHours(18);
        var slot18To20To = day.AddHours(20);

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var r1Task = Task.Run(async () =>
        {
            await startGate.Task;
            return await client1.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
            {
                TableId = tableId,
                CustomerName = "Guest One",
                CustomerPhone = "0901111111",
                PartySize = 2,
                ReservedFrom = slot18To20From,
                ReservedTo = slot18To20To
            });
        });

        var r2Task = Task.Run(async () =>
        {
            await startGate.Task;
            return await client2.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
            {
                TableId = tableId,
                CustomerName = "Guest Two",
                CustomerPhone = "0902222222",
                PartySize = 2,
                ReservedFrom = slot18To20From.AddMinutes(30),
                ReservedTo = slot18To20To.AddMinutes(30)
            });
        });

        startGate.SetResult();
        var responses = await Task.WhenAll(r1Task, r2Task);

        var okCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var conflictCount = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, okCount);
        Assert.Equal(1, conflictCount);

        // Adjacent slots (18:00-19:00 and 19:00-20:00) on another day must both succeed
        var adjDay = DateTime.UtcNow.Date.AddDays(3);
        var adj1 = await client1.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TableId = tableId,
            CustomerName = "Adj One",
            CustomerPhone = "0903333333",
            PartySize = 2,
            ReservedFrom = adjDay.AddHours(18),
            ReservedTo = adjDay.AddHours(19)
        });
        var adj2 = await client2.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TableId = tableId,
            CustomerName = "Adj Two",
            CustomerPhone = "0904444444",
            PartySize = 2,
            ReservedFrom = adjDay.AddHours(19),
            ReservedTo = adjDay.AddHours(20)
        });

        Assert.Equal(HttpStatusCode.OK, adj1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, adj2.StatusCode);
    }

    // =========================================================================
    // Test 6: ShiftOpenRace_Concurrent_OnlyOneSucceeds
    // =========================================================================
    [Fact]
    public async Task ShiftOpenRace_Concurrent_OnlyOneSucceeds()
    {
        using var factory = new WalEnforcedRestaurantApiFactory();
        var (cashierId, cashierName) = SeedUserWithRole(factory, AuthPolicies.Roles.Cashier);

        using var client1 = CreateAuthenticatedClient(factory, cashierId, cashierName, AuthPolicies.Roles.Cashier);
        using var client2 = CreateAuthenticatedClient(factory, cashierId, cashierName, AuthPolicies.Roles.Cashier);

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var t1 = Task.Run(async () =>
        {
            await startGate.Task;
            return await client1.PostAsJsonAsync("/api/cashier-shifts/open", new OpenCashierShiftRequest
            {
                OpeningCash = 500_000m,
                Note = "Race 1"
            });
        });

        var t2 = Task.Run(async () =>
        {
            await startGate.Task;
            return await client2.PostAsJsonAsync("/api/cashier-shifts/open", new OpenCashierShiftRequest
            {
                OpeningCash = 500_000m,
                Note = "Race 2"
            });
        });

        startGate.SetResult();
        var responses = await Task.WhenAll(t1, t2);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    // =========================================================================
    // Test 7: JwtRevocation_LockUser_Or_ChangeRole_ImmediatelyReturns401
    // =========================================================================
    [Fact]
    public async Task JwtRevocation_LockUser_Or_ChangeRole_ImmediatelyReturns401()
    {
        using var factory = new EnforcedRestaurantApiFactory();
        var (adminId, adminName) = SeedUserWithRole(factory, AuthPolicies.Roles.Admin);
        var (waiterId, waiterName) = SeedUserWithRole(factory, AuthPolicies.Roles.Waiter, password: "Password123!");
        var (staffId, staffName) = SeedUserWithRole(factory, AuthPolicies.Roles.Waiter, password: "Password123!");

        using var adminClient = CreateAuthenticatedClient(factory, adminId, adminName, AuthPolicies.Roles.Admin);

        // 1. Waiter logs in -> 200 on /api/users/me -> Admin locks waiter -> old JWT immediately 401
        var waiterToken = await LoginAndGetTokenAsync(factory, waiterName, "Password123!");
        using var waiterClient = factory.CreateClient();
        waiterClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", waiterToken);

        var beforeLock = await waiterClient.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, beforeLock.StatusCode);

        var lockResp = await adminClient.PatchAsync($"/api/users/{waiterId}/active?active=false", null);
        Assert.Equal(HttpStatusCode.OK, lockResp.StatusCode);

        var afterLock = await waiterClient.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLock.StatusCode);

        // 2. Staff logs in -> 200 on /api/users/me -> Admin changes role -> old JWT immediately 401
        var staffToken = await LoginAndGetTokenAsync(factory, staffName, "Password123!");
        using var staffClient = factory.CreateClient();
        staffClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", staffToken);

        var beforeRoleChange = await staffClient.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.OK, beforeRoleChange.StatusCode);

        var assignResp = await adminClient.PostAsJsonAsync($"/api/users/{staffId}/roles", new AssignRoleRequest
        {
            RoleName = AuthPolicies.Roles.Cashier
        });
        Assert.Equal(HttpStatusCode.OK, assignResp.StatusCode);

        var afterRoleChange = await staffClient.GetAsync("/api/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterRoleChange.StatusCode);
    }

    // =========================================================================
    // Test 8: PublicApis_AllowAnonymous_And_CustomerRemoteOrderBlocked_QrOrderAllowed
    // =========================================================================
    [Fact]
    public async Task PublicApis_AllowAnonymous_And_CustomerRemoteOrderBlocked_QrOrderAllowed()
    {
        using var factory = new EnforcedRestaurantApiFactory();
        var (tableId, qrToken, sizeId) = SeedOrderableTableAndMenu(factory);

        using var anonClient = factory.CreateClient();

        // Public read endpoints return 200 OK anonymously
        foreach (var path in new[]
        {
            "/api/menu",
            "/api/foods",
            "/api/categories",
            "/api/public/tables",
            "/api/public/table-areas",
            "/api/public/promotions"
        })
        {
            var resp = await anonClient.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        // Protected write endpoints require authentication
        var anonOrder = await anonClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            TableId = tableId,
            Items = [new CreateOrderItemRequest { SizeId = sizeId, Quanlity = 1 }]
        });
        Assert.Equal(HttpStatusCode.Unauthorized, anonOrder.StatusCode);

        var anonReservation = await anonClient.PostAsJsonAsync("/api/reservations", new CreateReservationRequest
        {
            TableId = tableId,
            CustomerName = "Anon",
            CustomerPhone = "0900000000",
            PartySize = 2,
            ReservedFrom = DateTime.UtcNow.AddDays(1),
            ReservedTo = DateTime.UtcNow.AddDays(1).AddHours(1)
        });
        Assert.Equal(HttpStatusCode.Unauthorized, anonReservation.StatusCode);

        // Public customer registration returns 200 OK with Customer role token
        var username = $"cust_{Guid.NewGuid():N}";
        var regResp = await anonClient.PostAsJsonAsync("/api/auth/customer/register", new CustomerRegisterRequest
        {
            UserName = username,
            Email = $"{username}@example.test",
            Password = "Password123!",
            ConfirmPassword = "Password123!",
            FullName = "Customer Test",
            Phone = "0901234567"
        });
        Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);

        var regJson = await regResp.Content.ReadFromJsonAsync<JsonElement>();
        var data = regJson.GetProperty("data");
        var customerToken = data.GetProperty("token").GetString();
        var roles = data.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.False(string.IsNullOrWhiteSpace(customerToken));
        Assert.Contains(AuthPolicies.Roles.Customer, roles);

        using var customerClient = factory.CreateClient();
        customerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customerToken);

        // Customer without TableQrToken -> 403 Forbidden
        var remoteOrderNoQr = await customerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            TableId = tableId,
            Items = [new CreateOrderItemRequest { SizeId = sizeId, Quanlity = 1 }]
        });
        Assert.Equal(HttpStatusCode.Forbidden, remoteOrderNoQr.StatusCode);

        // Customer with wrong TableQrToken -> 403 Forbidden
        var remoteOrderWrongQr = await customerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            TableId = tableId,
            TableQrToken = "INVALID-QR-TOKEN",
            Items = [new CreateOrderItemRequest { SizeId = sizeId, Quanlity = 1 }]
        });
        Assert.Equal(HttpStatusCode.Forbidden, remoteOrderWrongQr.StatusCode);

        // Customer with valid TableQrToken -> 200 OK
        var qrOrder = await customerClient.PostAsJsonAsync("/api/orders", new CreateOrderRequest
        {
            TableId = tableId,
            TableQrToken = qrToken,
            Items = [new CreateOrderItemRequest { SizeId = sizeId, Quanlity = 1 }]
        });
        Assert.Equal(HttpStatusCode.OK, qrOrder.StatusCode);
    }

    // =========================================================================
    // Test 9: TopDishes_And_ProfitabilityReports_ReturnExpectedCogsAndCsv
    // =========================================================================
    [Fact]
    public async Task TopDishes_And_ProfitabilityReports_ReturnExpectedCogsAndCsv()
    {
        using var factory = new EnforcedRestaurantApiFactory();
        var (adminId, adminName) = SeedUserWithRole(factory, AuthPolicies.Roles.Admin);
        var dishName = $"PhoBo-{Guid.NewGuid():N}";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            var cat = new Category
            {
                CategoryName = $"Cat-{Guid.NewGuid():N}",
                CategoryDescription = "desc",
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Categories.Add(cat);
            db.SaveChanges();

            var food = new Food
            {
                CategoryId = cat.CategoryId,
                FoodName = dishName,
                FoodDescription = "desc",
                FoodImageUrl = "pho.png",
                FoodPrice = 100m,
                IsAvailable = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Foods.Add(food);
            db.SaveChanges();

            var size = new FoodSize
            {
                FoodId = food.FoodId,
                SizeName = "L",
                SizePrice = 100m,
                IsDefault = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.FoodSizes.Add(size);
            db.SaveChanges();

            var ingredient = new Ingredient
            {
                Name = $"Beef-{Guid.NewGuid():N}",
                Unit = "kg",
                MinimumStock = 1m,
                IsActive = true
            };
            db.Ingredients.Add(ingredient);
            db.SaveChanges();

            var supplier = new Supplier
            {
                Name = $"Sup-{Guid.NewGuid():N}",
                Phone = "0900000000",
                Email = "sup@example.test",
                Address = "HN",
                IsActive = true
            };
            db.Suppliers.Add(supplier);
            db.SaveChanges();

            var po = new PurchaseOrder
            {
                SupplierId = supplier.SupplierId,
                CreatedBy = adminId,
                Status = "Received",
                TotalAmount = 150m,
                OrderedAt = now,
                ReceivedAt = now
            };
            db.PurchaseOrders.Add(po);
            db.SaveChanges();

            // 10 units for 150 -> avg cost = 15 per unit
            db.PurchaseOrderItems.Add(new PurchaseOrderItem
            {
                PurchaseOrderId = po.Id,
                IngredientId = ingredient.IngredientId,
                Quanlity = 10m,
                UnitPrice = 15m,
                TotalPrice = 150m
            });

            var recipe = new Recipe
            {
                SizeId = size.SizeId,
                Name = "Pho Recipe",
                Version = "1",
                IsActive = true
            };
            db.Recipes.Add(recipe);
            db.SaveChanges();

            // 2 units per dish -> unit COGS = 2 * 15 = 30
            db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = recipe.RecipeId,
                IngredientId = ingredient.IngredientId,
                Quanlity = 2m
            });

            var table = new RestaurantTable
            {
                TableNumber = $"T-RPT-{Guid.NewGuid():N}"[..16],
                Capacity = 4,
                Status = RestaurantTableStatuses.Available,
                IsActive = true
            };
            db.RestaurantTables.Add(table);
            db.SaveChanges();

            var order = new Order
            {
                TableId = table.TableId,
                CreatedBy = adminId,
                Status = OrderStatuses.Paid,
                Subtotal = 300m,
                TotalAmount = 300m,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.Orders.Add(order);
            db.SaveChanges();

            // 3 dishes sold -> Revenue = 300, COGS = 90, GrossProfit = 210, Margin = 70%
            db.OrderItems.Add(new OrderItem
            {
                OrderId = order.OrderId,
                SizeId = size.SizeId,
                Quanlity = 3,
                UnitPrice = 100m,
                Status = OrderStatuses.Completed
            });

            db.Payments.Add(new Payment
            {
                OrderId = order.OrderId,
                PaymentMethod = "Cash",
                Provider = PaymentProviders.Mock,
                Amount = 300m,
                Status = PaymentStatuses.Paid,
                PaidAt = now,
                CreatedAt = now,
                CreatedBy = adminId
            });
            db.SaveChanges();
        }

        using var adminClient = CreateAuthenticatedClient(factory, adminId, adminName, AuthPolicies.Roles.Admin);

        var topResp = await adminClient.GetAsync("/api/reports/top-dishes?period=day");
        Assert.Equal(HttpStatusCode.OK, topResp.StatusCode);
        var topItems = await topResp.Content.ReadFromJsonAsync<List<TopDishItemResponse>>();
        Assert.NotNull(topItems);
        var dish = Assert.Single(topItems!, x => x.FoodName == dishName);
        Assert.Equal(3, dish.QuantitySold);
        Assert.Equal(300m, dish.Revenue);
        Assert.Equal(90m, dish.Cogs);
        Assert.Equal(210m, dish.GrossProfit);
        Assert.False(dish.HasMissingRecipe);
        Assert.False(dish.HasMissingCost);

        var profResp = await adminClient.GetAsync("/api/reports/profitability?period=day");
        Assert.Equal(HttpStatusCode.OK, profResp.StatusCode);
        var prof = await profResp.Content.ReadFromJsonAsync<ProfitabilityReportResponse>();
        Assert.NotNull(prof);
        Assert.Equal(300m, prof!.TotalRevenue);
        Assert.Equal(90m, prof.TotalCogs);
        Assert.Equal(210m, prof.TotalGrossProfit);
        Assert.Equal(70m, prof.GrossMarginPct);

        var topCsvResp = await adminClient.GetAsync("/api/reports/top-dishes/export.csv?period=day");
        Assert.Equal(HttpStatusCode.OK, topCsvResp.StatusCode);
        Assert.Equal("text/csv", topCsvResp.Content.Headers.ContentType?.MediaType);
        var topCsvBytes = await topCsvResp.Content.ReadAsByteArrayAsync();
        Assert.True(topCsvBytes.Length >= 3 && topCsvBytes[0] == 0xEF && topCsvBytes[1] == 0xBB && topCsvBytes[2] == 0xBF);
        var topCsvText = Encoding.UTF8.GetString(topCsvBytes[3..]);
        Assert.Contains(dishName, topCsvText);

        var profCsvResp = await adminClient.GetAsync("/api/reports/profitability/export.csv?period=day");
        Assert.Equal(HttpStatusCode.OK, profCsvResp.StatusCode);
        Assert.Equal("text/csv", profCsvResp.Content.Headers.ContentType?.MediaType);
        var profCsvBytes = await profCsvResp.Content.ReadAsByteArrayAsync();
        Assert.True(profCsvBytes.Length >= 3 && profCsvBytes[0] == 0xEF && profCsvBytes[1] == 0xBB && profCsvBytes[2] == 0xBF);
        var profCsvText = Encoding.UTF8.GetString(profCsvBytes[3..]);
        Assert.Contains(dishName, profCsvText);
        Assert.Contains("TOTAL", profCsvText);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static (int UserId, string UserName) SeedUserWithRole(
        WebApplicationFactory<Program> factory,
        string roleName,
        string password = "init-password")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var userName = $"u_{Guid.NewGuid():N}";
        var user = new Users
        {
            UserName = userName,
            UserPassword = BCrypt.Net.BCrypt.HashPassword(password),
            UserFullName = $"Test {roleName}",
            UserEmail = $"{userName}@example.test",
            UserPhone = "0900000000",
            IsActive = true,
            TokenVersion = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.SaveChanges();

        var role = db.Roles.Single(r => r.RoleName == roleName);
        db.UserRoles.Add(new UserRoles { UserId = user.Id, RoleId = role.RoleId });
        db.SaveChanges();

        return (user.Id, userName);
    }

    private static HttpClient CreateAuthenticatedClient(
        WebApplicationFactory<Program> factory,
        int userId,
        string userName,
        params string[] roles)
    {
        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
        var token = tokenService.CreateToken(new UserDto
        {
            UserId = userId,
            UserName = userName,
            Email = $"{userName}@example.test",
            FullName = userName,
            TokenVersion = 1
        }, roles);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> LoginAndGetTokenAsync(
        WebApplicationFactory<Program> factory,
        string userName,
        string password)
    {
        using var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            UserName = userName,
            Password = password
        });
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("data").GetProperty("token").GetString()!;
    }

    private static int SeedActiveTable(WebApplicationFactory<Program> factory, string prefix, int capacity)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var table = new RestaurantTable
        {
            TableNumber = $"{prefix}-{Guid.NewGuid():N}"[..18],
            Capacity = capacity,
            Status = RestaurantTableStatuses.Available,
            IsActive = true,
            QrToken = $"QR-{Guid.NewGuid():N}"
        };
        db.RestaurantTables.Add(table);
        db.SaveChanges();
        return table.TableId;
    }

    private static (int TableId, string QrToken, int SizeId) SeedOrderableTableAndMenu(
        WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var cat = new Category
        {
            CategoryName = $"Cat-{Guid.NewGuid():N}",
            CategoryDescription = "desc",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Categories.Add(cat);
        db.SaveChanges();

        var food = new Food
        {
            CategoryId = cat.CategoryId,
            FoodName = $"Food-{Guid.NewGuid():N}",
            FoodDescription = "desc",
            FoodImageUrl = "img.png",
            FoodPrice = 50m,
            IsAvailable = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Foods.Add(food);
        db.SaveChanges();

        var size = new FoodSize
        {
            FoodId = food.FoodId,
            SizeName = "M",
            SizePrice = 50m,
            IsDefault = true,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.FoodSizes.Add(size);
        db.SaveChanges();

        var ingredient = new Ingredient
        {
            Name = $"Ing-{Guid.NewGuid():N}",
            Unit = "kg",
            MinimumStock = 0m,
            IsActive = true
        };
        db.Ingredients.Add(ingredient);
        db.SaveChanges();

        db.Inventories.Add(new Inventory
        {
            IngredientId = ingredient.IngredientId,
            Quanlity = 100m,
            UpdatedAt = now
        });

        var recipe = new Recipe
        {
            SizeId = size.SizeId,
            Name = "R1",
            Version = "1",
            IsActive = true
        };
        db.Recipes.Add(recipe);
        db.SaveChanges();

        db.RecipeIngredients.Add(new RecipeIngredient
        {
            RecipeId = recipe.RecipeId,
            IngredientId = ingredient.IngredientId,
            Quanlity = 1m
        });

        var qr = $"QR-{Guid.NewGuid():N}";
        var table = new RestaurantTable
        {
            TableNumber = $"T-QR-{Guid.NewGuid():N}"[..16],
            Capacity = 4,
            Status = RestaurantTableStatuses.Available,
            IsActive = true,
            QrToken = qr
        };
        db.RestaurantTables.Add(table);
        db.SaveChanges();

        return (table.TableId, qr, size.SizeId);
    }
}
