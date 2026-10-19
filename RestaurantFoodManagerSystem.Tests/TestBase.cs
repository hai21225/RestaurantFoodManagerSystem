using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Domain.AppDbContext;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Phase 0 safety net.
///
/// Provides an isolated in-memory SQLite database + manually wired services, so the
/// current behaviour of the backend services can be pinned BEFORE any refactoring.
///
/// Important:
/// - The real SQL Server database is never touched.
/// - The schema is created from the EF model (EnsureCreated), so migrations/schema are untouched.
/// - SQLite is used instead of the InMemory provider because several services
///   (OrderService, ManagementService) open explicit transactions, which InMemory does not support.
/// </summary>
public abstract class TestBase : IDisposable
{
    private readonly SqliteConnection _connection;

    protected AppDbContext Db { get; }

    protected TestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();
    }

    /// <summary>A second context on the same database, for tracker-independent assertions.</summary>
    protected AppDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    // ==========================================================
    // Wiring (mirrors the real DI registrations one-to-one)
    // ==========================================================

    protected IUnitOfWork Uow => new UnitOfWork(Db);
    protected IRepository<T> Repo<T>() where T : class => new Repository<T>(Db);

    protected AuthService Auth => new(new UserService(Uow, Repo<Users>()),
                                      new UserRoleService(Uow, Repo<UserRoles>()),
                                      new RoleService(Repo<Roles>()));

    protected FoodService Foods => new(Uow, Repo<Food>(), Repo<Category>());
    protected CategoryService Categories => new(Repo<Category>(), Uow);
    protected FoodSizeService FoodSizes => new(Repo<FoodSize>(), Repo<Food>(), Uow);
    protected ToppingService Toppings => new(Repo<Topping>(), Uow);
    protected FoodToppingService FoodToppings => new(Repo<FoodTopping>(), Repo<Food>(), Repo<Topping>(), Uow);
    protected RestaurantTableService Tables => new(Uow, Repo<RestaurantTable>(), Db);

    protected OrderService Orders => new(Db, Tables);
    protected PaymentService Payments => new(Db, Tables);
    protected KitchenService Kitchen => new(Db);
    protected MenuService Menu => new(Db);

    protected ManagementService Management => new(Db);
    protected IngredientService Ingredients => new(Management);
    protected SupplierService Suppliers => new(Management);
    protected PurchaseOrderService Purchases => new(Management);
    protected PromotionService Promotions => new(Management);
    protected RecipeService Recipes => new(Management);
    protected InventoryService InventorySvc => new(Management);

    // ==========================================================
    // Seeding helpers
    // ==========================================================

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    protected Users SeedUser(bool isActive = true)
    {
        var username = Unique("user");
        var user = new Users
        {
            UserName = username,
            UserPassword = "unused-hash-placeholder",
            UserFullName = "Test User",
            UserEmail = $"{username}@example.test",
            UserPhone = "0000000000",
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    protected Roles SeedRole(string name)
    {
        var role = new Roles { RoleName = name, RoleDescription = name };
        Db.Roles.Add(role);
        Db.SaveChanges();
        return role;
    }

    protected Category SeedCategory(string? name = null)
    {
        var category = new Category
        {
            CategoryName = name ?? Unique("cat"),
            CategoryDescription = "desc",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Db.Categories.Add(category);
        Db.SaveChanges();
        return category;
    }

    protected Food SeedFood(int categoryId, decimal price = 100m, bool isActive = true, bool isAvailable = true)
    {
        var food = new Food
        {
            CategoryId = categoryId,
            FoodName = Unique("food"),
            FoodDescription = "desc",
            FoodImageUrl = "img.png",
            FoodPrice = price,
            IsAvailable = isAvailable,
            IsActive = isActive,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Db.Foods.Add(food);
        Db.SaveChanges();
        return food;
    }

    protected FoodSize SeedSize(int foodId, decimal price, bool isDefault = true, string name = "M")
    {
        var size = new FoodSize
        {
            FoodId = foodId,
            SizeName = name,
            SizePrice = price,
            IsDefault = isDefault,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Db.FoodSizes.Add(size);
        Db.SaveChanges();
        return size;
    }

    protected Topping SeedTopping(decimal price = 10m, bool isActive = true, bool isAvailable = true)
    {
        var topping = new Topping
        {
            ToppingName = Unique("topping"),
            ToppingPrice = price,
            IsActive = isActive,
            IsAvailable = isAvailable
        };
        Db.Toppings.Add(topping);
        Db.SaveChanges();
        return topping;
    }

    protected Ingredient SeedIngredient(decimal stock, decimal minimumStock = 0m)
    {
        var ingredient = new Ingredient
        {
            Name = Unique("ingredient"),
            Unit = "kg",
            MinimumStock = minimumStock,
            IsActive = true
        };
        Db.Ingredients.Add(ingredient);
        Db.SaveChanges();

        Db.Inventories.Add(new Inventory
        {
            IngredientId = ingredient.IngredientId,
            Quanlity = stock,
            UpdatedAt = DateTime.UtcNow
        });
        Db.SaveChanges();

        return ingredient;
    }

    protected Recipe SeedRecipe(int sizeId, params (int IngredientId, decimal Quantity)[] items)
    {
        var recipe = new Recipe
        {
            SizeId = sizeId,
            Name = Unique("recipe"),
            Version = "1",
            IsActive = true
        };
        Db.Recipes.Add(recipe);
        Db.SaveChanges();

        foreach (var (ingredientId, quantity) in items)
        {
            Db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = recipe.RecipeId,
                IngredientId = ingredientId,
                Quanlity = quantity
            });
        }
        Db.SaveChanges();
        return recipe;
    }

    protected RestaurantTable SeedTable(string? number = null, int capacity = 4)
    {
        var table = new RestaurantTable
        {
            TableNumber = number ?? Unique("T"),
            Capacity = capacity,
            Status = RestaurantTableStatuses.Available,
            IsActive = true
        };
        Db.RestaurantTables.Add(table);
        Db.SaveChanges();
        return table;
    }

    protected Order SeedOrder(int tableId, int createdBy, string status = "Pending", decimal subtotal = 100m)
    {
        var order = new Order
        {
            TableId = tableId,
            CreatedBy = createdBy,
            Status = status,
            Subtotal = subtotal,
            TotalAmount = subtotal,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        Db.Orders.Add(order);
        Db.SaveChanges();
        return order;
    }

    /// <summary>A fully orderable scenario: table + staff user + sized food with recipe/stock + assignable topping.</summary>
    protected sealed record OrderScenario(
        RestaurantTable Table,
        Users Staff,
        Category Category,
        Food Food,
        FoodSize Size,
        Topping Topping,
        Ingredient Ingredient,
        decimal StockQuantity);

    protected OrderScenario SeedOrderScenario(decimal sizePrice = 50m, decimal toppingPrice = 5m, decimal stock = 100m)
    {
        var category = SeedCategory();
        var food = SeedFood(category.CategoryId);
        var size = SeedSize(food.FoodId, sizePrice);
        var ingredient = SeedIngredient(stock);
        SeedRecipe(size.SizeId, (ingredient.IngredientId, 2m));
        var topping = SeedTopping(toppingPrice);

        Db.FoodToppings.Add(new FoodTopping { FoodId = food.FoodId, ToppingId = topping.ToppingId });
        Db.SaveChanges();

        return new OrderScenario(SeedTable(), SeedUser(), category, food, size, topping, ingredient, stock);
    }
}
