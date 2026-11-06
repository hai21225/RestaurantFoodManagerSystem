using Microsoft.EntityFrameworkCore;

namespace RestaurantFoodManagerSystem.Infrastructure.Persistence
{
    public partial class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        // =========================================================
        // ACCOUNT
        // =========================================================

        public DbSet<Users> Users { get; set; }
        public DbSet<Roles> Roles { get; set; }
        public DbSet<UserRoles> UserRoles { get; set; }

        // =========================================================
        // FOOD
        // =========================================================

        public DbSet<Category> Categories { get; set; }
        public DbSet<Food> Foods { get; set; }
        public DbSet<FoodSize> FoodSizes { get; set; }
        public DbSet<Topping> Toppings { get; set; }
        public DbSet<FoodTopping> FoodToppings { get; set; }

        // =========================================================
        // ORDER
        // =========================================================

        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<OrderItemTopping> OrderItemToppings { get; set; }
        public DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }
        public DbSet<OrderItemStatusHistory> OrderItemStatusHistories { get; set; }

        // =========================================================
        // KITCHEN
        // =========================================================

        public DbSet<KitchenOrder> KitchenOrders { get; set; }
        public DbSet<KitchenOrderItem> KitchenOrderItems { get; set; }

        // =========================================================
        // PAYMENT
        // =========================================================

        public DbSet<Payment> Payments { get; set; }

        // =========================================================
        // PROMOTION
        // =========================================================

        public DbSet<Promotion> Promotions { get; set; }
        public DbSet<PromotionCategory> PromotionCategories { get; set; }
        public DbSet<PromotionFood> PromotionFoods { get; set; }

        // =========================================================
        // INVENTORY
        // =========================================================

        public DbSet<Ingredient> Ingredients { get; set; }
        public DbSet<Inventory> Inventories { get; set; }
        public DbSet<InventoryTransaction> InventoryTransactions { get; set; }

        // =========================================================
        // PURCHASE
        // =========================================================

        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<PurchaseOrderItem> PurchaseOrderItems { get; set; }

        // =========================================================
        // RECIPE
        // =========================================================

        public DbSet<Recipe> Recipes { get; set; }
        public DbSet<RecipeIngredient> RecipeIngredients { get; set; }

        // =========================================================
        // RESTAURANT TABLE
        // =========================================================

        public DbSet<TableArea> TableAreas { get; set; }
        public DbSet<RestaurantTable> RestaurantTables { get; set; }

        // =========================================================
        // SETTINGS (Đợt 2 / A1)
        // =========================================================

        /// <summary>Cấu hình quán — bảng chỉ có một bản ghi duy nhất (Id = 1).</summary>
        public DbSet<RestaurantSettings> RestaurantSettings { get; set; }

        /// <summary>
        /// Sinh giá trị mới cho concurrency token ở provider không có rowversion (SQLite trong test).
        /// Trên SQL Server, DB tự làm việc này nên bỏ qua.
        /// Đồng thời dịch <see cref="DbUpdateConcurrencyException"/> thành
        /// <see cref="ConcurrencyConflictException"/> để tầng API trả 409 thay vì 500.
        /// </summary>
        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            StampRowVersionsForNonSqlServer();

            try
            {
                return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ConcurrencyConflictException(
                    "Dữ liệu vừa bị thay đổi bởi thao tác khác. Vui lòng tải lại và thử lại.", ex);
            }
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            StampRowVersionsForNonSqlServer();

            try
            {
                return base.SaveChanges(acceptAllChangesOnSuccess);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new ConcurrencyConflictException(
                    "Dữ liệu vừa bị thay đổi bởi thao tác khác. Vui lòng tải lại và thử lại.", ex);
            }
        }

        private void StampRowVersionsForNonSqlServer()
        {
            if (Database.IsSqlServer()) return;

            foreach (var entry in ChangeTracker.Entries()
                         .Where(e => e.State == EntityState.Modified &&
                                     e.Metadata.FindProperty(nameof(Order.RowVersion)) is not null))
            {
                entry.Property(nameof(Order.RowVersion)).CurrentValue = Guid.NewGuid().ToByteArray();
            }
        }
    }
}
