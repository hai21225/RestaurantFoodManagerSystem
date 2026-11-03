using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// GĐ8: host API dựng được bằng <see cref="WebApplicationFactory{TEntryPoint}"/> mà KHÔNG cần
/// SQL Server thật. GĐ6e dùng lại factory này để test 401/403 ở mức HTTP.
///
/// <para>
/// Cách làm: thay <see cref="AppDbContext"/> bằng SQLite in-memory, tắt
/// <c>Database:MigrateOnStartup</c> (migration viết cho SQL Server) nhưng vẫn bật
/// <c>Database:SeedOnStartup</c> để chạy đúng đường khởi động của app.
/// </para>
///
/// <para>
/// <paramref name="enforce"/> = <c>true</c> bật <c>Auth:Enforce</c> + khóa ký JWT test (xem
/// <see cref="EnforcedRestaurantApiFactory"/>); mặc định <c>false</c> giữ baseline dev (anonymous).
/// </para>
/// </summary>
public class RestaurantApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Khóa ký HS256 dùng riêng trong test (không phải khóa thật, không dùng ngoài test).</summary>
    public const string TestJwtSigningKey = "gd6e-http-test-signing-key-0123456789abcdef";

    private readonly SqliteConnection _connection;
    private readonly bool _enforce;

    public RestaurantApiFactory() : this(enforce: false)
    {
    }

    protected RestaurantApiFactory(bool enforce)
    {
        _enforce = enforce;

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        // ConfigureWebHost chạy trước khối initializer sau builder.Build() trong Program.cs,
        // nên schema phải sẵn sàng ngay tại đây để seed lúc khởi động không lỗi "no such table".
        using var context = new AppDbContext(CreateOptions());
        context.Database.EnsureCreated();
    }

    /// <summary><c>Auth:Enforce</c> của host test.</summary>
    public bool Enforce => _enforce;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting($"{DatabaseInitializationOptions.SectionName}:MigrateOnStartup", "false");
        builder.UseSetting($"{DatabaseInitializationOptions.SectionName}:SeedOnStartup", "true");

        // GĐ6e: bật/tắt enforcement + nạp khóa ký cho host test. UseSetting được áp trước khi
        // Program.cs đọc cấu hình nên cặp Auth:Enforce / Jwt:Key có hiệu lực ngay từ đầu.
        builder.UseSetting("Auth:Enforce", _enforce ? "true" : "false");
        builder.UseSetting("Authentication:Jwt:Key", TestJwtSigningKey);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            // EF Core 9: cấu hình provider cũ (UseSqlServer) cũng phải bỏ, nếu không sẽ báo
            // "Only a single database provider can be registered".
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));
        });
    }

    private DbContextOptions<AppDbContext> CreateOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}

public sealed class EnforcedRestaurantApiFactory : RestaurantApiFactory
{
    /// <summary>Host test có <c>Auth:Enforce=true</c> (401/403 thật) và khóa ký JWT test.</summary>
    public EnforcedRestaurantApiFactory() : base(enforce: true)
    {
    }
}

public sealed class ApiHostSmokeTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public ApiHostSmokeTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnonymousSetupStatus_ReturnsOk_OnBootedHost()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/setup/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<SetupStatusResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.RequiresSetup);
    }

    [Fact]
    public void StartupSeed_CreatesDefaultRoles_OnBootedHost()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var roleNames = db.Roles.Select(role => role.RoleName).ToList();

        Assert.Equal(DatabaseInitializer.DefaultRoles.Count, roleNames.Count);
        foreach (var (name, _) in DatabaseInitializer.DefaultRoles)
        {
            Assert.Contains(name, roleNames);
        }
    }
}
