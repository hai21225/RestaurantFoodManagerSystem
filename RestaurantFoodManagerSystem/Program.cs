using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Domain.AppDbContext;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<FoodService>();
builder.Services.AddScoped<CategoryService>();
builder.Services.AddScoped<FoodSizeService>();
builder.Services.AddScoped<ToppingService>();
builder.Services.AddScoped<FoodToppingService>();
builder.Services.AddScoped<RestaurantTableService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<UserRoleService>();
builder.Services.AddScoped<RoleService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<ManagementService>();
builder.Services.AddScoped<IngredientService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddScoped<PromotionService>();
builder.Services.AddScoped<RecipeService>();
builder.Services.AddScoped<KitchenService>();





var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();



using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<AppDbContext>();
    context.Database.Migrate();


    if(!context.Roles.Any())
    {
        context.Roles.AddRange(
            new Roles { RoleName = "Admin",RoleDescription ="Người quản lí cấp cao" },
            new Roles { RoleName = "WarehouseManager", RoleDescription="Nhân viên quản lí kho" },
            new Roles { RoleName = "Chef", RoleDescription="Nhân viên bếp" },
            new Roles { RoleName = "Cashier", RoleDescription="Nhân viên thu ngân" },
            new Roles { RoleName = "Waiter", RoleDescription="Nhân viên phục vụ" }
                                         );
        context.SaveChanges();
    }

}

app.Run();
