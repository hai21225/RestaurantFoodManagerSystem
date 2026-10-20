using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RestaurantFoodManagerSystem.Infrastructure.Auth;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// GĐ6/A0: frontend tĩnh chạy bằng `python -m http.server 5500 --bind localhost`
// ⇒ origin http://localhost:5500. Thêm 5501/3000 cho các cổng dev thay thế.
// Policy này trước đây đăng ký nhưng KHÔNG được áp vào pipeline (thiếu UseCors).
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5500",
                "http://localhost:5501",
                "http://localhost:3000")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// GĐ6 (lát 6a): cấu hình + service sinh JWT. Khóa ký nạp ngoài repo
// (Authentication__Jwt__Key hoặc user-secrets), không commit.
var jwtOptions = builder.Configuration.GetSection("Authentication:Jwt").Get<JwtOptions>() ?? new JwtOptions();
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();

if (authOptions.Enforce && string.IsNullOrWhiteSpace(jwtOptions.Key))
{
    throw new InvalidOperationException(
        "Auth:Enforce=true nhưng chưa cấu hình khóa ký JWT (Authentication:Jwt:Key). " +
        "Đặt qua biến môi trường Authentication__Jwt__Key hoặc user-secrets.");
}

// HS256 cần khoá tối thiểu 32 byte; khoá quá ngắn sẽ ném lỗi ngay lúc ký token (500 ở login).
if (!string.IsNullOrWhiteSpace(jwtOptions.Key) &&
    Encoding.UTF8.GetByteCount(jwtOptions.Key) < 32)
{
    throw new InvalidOperationException(
        "Authentication:Jwt:Key phải dài tối thiểu 32 byte (chuẩn HS256).");
}

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddScoped<TokenService>();

// GĐ6 (lát 6b): JWT authentication + policy phân quyền. Enforcement bật/tắt bằng Auth:Enforce
// (dev mặc định false ⇒ [Authorize] chưa chặn cho tới khi frontend gửi Bearer token).
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = !string.IsNullOrWhiteSpace(jwtOptions.Issuer),
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = !string.IsNullOrWhiteSpace(jwtOptions.Audience),
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = !string.IsNullOrWhiteSpace(jwtOptions.Key),
            IssuerSigningKey = string.IsNullOrWhiteSpace(jwtOptions.Key)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ValidateLifetime = true,
            // Khi chưa cấu hình khóa (dev), đừng đòi token phải ký để tránh lỗi "no keys".
            RequireSignedTokens = !string.IsNullOrWhiteSpace(jwtOptions.Key),
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

builder.Services.AddRestaurantAuthorization(authOptions);

// GĐ6c: actor (Users.Id) lấy từ claim "sub" của token; khi chưa xác thực thì fallback về body.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

builder.Services.AddScoped<FoodService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<FoodSizeService>();
builder.Services.AddScoped<ToppingService>();
builder.Services.AddScoped<FoodToppingService>();
builder.Services.AddScoped<TableAreaService>();
builder.Services.AddScoped<RestaurantTableService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<UserRoleService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SetupService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddSingleton<IOrderIdempotencyStore, InMemoryOrderIdempotencyStore>();
builder.Services.AddScoped<OrderLifecycleCoordinator>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<IngredientService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<KitchenService>();
builder.Services.AddScoped<ReportService>();





var app = builder.Build();

if (!authOptions.Enforce)
{
    // Cảnh báo: khi tắt enforce, mọi policy [Authorize] đều cho qua ⇒ API mở cho
    // mọi caller. Chỉ chấp nhận khi frontend chưa gửi Bearer token.
    app.Logger.LogWarning(
        "Auth:Enforce=false — mọi endpoint [Authorize] đang cho qua. Chỉ dùng cho môi trường dev.");
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Phải đứng trước UseAuthentication/UseAuthorization để preflight và header
// Authorization của request cross-origin được xử lý.
app.UseCors("FrontendPolicy");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();



// GĐ8: migrate + seed tách khỏi Program.cs (Infrastructure/Persistence/DatabaseInitializer.cs)
// để host test (WebApplicationFactory) dựng được mà không chạm DB SQL Server thật.
// Mặc định cả hai cờ bật ⇒ hành vi khởi động không đổi.
var databaseInitOptions = builder.Configuration
    .GetSection(DatabaseInitializationOptions.SectionName)
    .Get<DatabaseInitializationOptions>() ?? new DatabaseInitializationOptions();

await DatabaseInitializer.InitializeAsync(app.Services, databaseInitOptions);

app.Run();

// GĐ8: cần cho WebApplicationFactory<Program> (test HTTP-level, mở đường cho GĐ6e).
public partial class Program { }
