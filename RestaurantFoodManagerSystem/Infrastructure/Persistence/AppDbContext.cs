using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantFoodManagerSystem.Infrastructure.Persistence
{
    public class AppDbContext : DbContext
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

        public DbSet<RestaurantTable> RestaurantTables { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =====================================================
            // NHÓM B6 — UNIQUE INDEX + SOFT DELETE
            // =====================================================
            // Các bảng dùng soft delete (IsActive=false) và có unique index trên tên. Index cũ
            // không lọc nên bản ghi đã "xoá" vẫn chiếm tên ⇒ tạo lại cùng tên bị 500. Filter dưới
            // đây chỉ áp unique cho bản ghi đang hoạt động.
            //
            // Ngoại lệ: users.UserName/UserEmail vẫn unique toàn cục (xem khối ACCOUNT).
            //
            // Cú pháp filter khác nhau giữa SQL Server và SQLite (test dùng SQLite in-memory),
            // nên chọn theo provider đang build model. Mỗi tiến trình chỉ dùng một provider
            // (app = SQL Server, test = SQLite) nên model cache không bị trộn.
            var isSqlServer = Database.IsSqlServer();
            var activeFilter = isSqlServer ? "[IsActive] = 1" : "\"IsActive\" = 1";


            // =====================================================
            // ACCOUNT
            // =====================================================

            modelBuilder.Entity<Users>(entity =>
            {
                entity.ToTable("users");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Id)
                    .ValueGeneratedOnAdd();

                entity.Property(x => x.UserName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(x => x.UserPassword)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.Property(x => x.UserFullName)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(x => x.UserEmail)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(x => x.UserPhone)
                    .IsRequired()
                    .HasMaxLength(20);

                // Users: giữ unique TOÀN CỤC (không lọc IsActive) vì UserName/UserEmail là định danh
                // đăng nhập; tài khoản bị vô hiệu hoá vẫn phải chiếm tên để tránh trùng danh tính.
                entity.HasIndex(x => x.UserName)
                    .IsUnique();

                entity.HasIndex(x => x.UserEmail)
                    .IsUnique();
            });


            modelBuilder.Entity<Roles>(entity =>
            {
                entity.ToTable("roles");

                entity.HasKey(x => x.RoleId);

                entity.Property(x => x.RoleName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(x => x.RoleDescription)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.HasIndex(x => x.RoleName)
                    .IsUnique();
            });


            modelBuilder.Entity<UserRoles>(entity =>
            {
                entity.ToTable("user_roles");

                // Composite Primary Key
                entity.HasKey(x => new
                {
                    x.UserId,
                    x.RoleId
                });

                entity.HasOne<Users>()
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Roles>()
                    .WithMany()
                    .HasForeignKey(x => x.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });


            // =====================================================
            // CATEGORY
            // =====================================================

            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("categories");

                entity.HasKey(x => x.CategoryId);

                entity.Property(x => x.CategoryName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(x => x.CategoryDescription)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(x => x.DisplayOrder)
                    .IsRequired();

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.HasIndex(x => x.CategoryName)
                    .IsUnique()
                    .HasFilter(activeFilter);
            });


            // =====================================================
            // FOOD
            // =====================================================

            modelBuilder.Entity<Food>(entity =>
            {
                entity.ToTable("foods");

                entity.HasKey(x => x.FoodId);

                entity.Property(x => x.FoodName)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(x => x.FoodDescription)
                    .IsRequired()
                    .HasMaxLength(1000);

                entity.Property(x => x.FoodImageUrl)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(x => x.FoodPrice)
                    .HasPrecision(18, 2);

                entity.Property(x => x.IsAvailable)
                    .IsRequired();

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.Property(x => x.UpdatedAt)
                    .IsRequired();

                // Food -> Category
                entity.HasOne<Category>()
                    .WithMany()
                    .HasForeignKey(x => x.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.CategoryId);
            });


            // =====================================================
            // FOOD SIZE
            // =====================================================

            modelBuilder.Entity<FoodSize>(entity =>
            {
                entity.ToTable("food_sizes");

                entity.HasKey(x => x.SizeId);

                entity.Property(x => x.SizeName)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.SizePrice)
                    .HasPrecision(18, 2);

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.Property(x => x.IsDefault)
                    .IsRequired();

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.Property(x => x.UpdatedAt)
                    .IsRequired();

                // Food -> FoodSize
                entity.HasOne<Food>()
                    .WithMany()
                    .HasForeignKey(x => x.FoodId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new
                {
                    x.FoodId,
                    x.SizeName
                })
                .IsUnique()
                .HasFilter(activeFilter);
            });


            // =====================================================
            // TOPPING
            // =====================================================

            modelBuilder.Entity<Topping>(entity =>
            {
                entity.ToTable("toppings");

                entity.HasKey(x => x.ToppingId);

                entity.Property(x => x.ToppingName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(x => x.ToppingPrice)
                    .HasPrecision(18, 2);

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.Property(x => x.IsAvailable)
                    .IsRequired();

                entity.HasIndex(x => x.ToppingName)
                    .IsUnique()
                    .HasFilter(activeFilter);
            });


            // =====================================================
            // FOOD TOPPING
            // =====================================================

            modelBuilder.Entity<FoodTopping>(entity =>
            {
                entity.ToTable("food_toppings");

                // Composite Primary Key
                entity.HasKey(x => new
                {
                    x.FoodId,
                    x.ToppingId
                });

                entity.HasOne<Food>()
                    .WithMany()
                    .HasForeignKey(x => x.FoodId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Topping>()
                    .WithMany()
                    .HasForeignKey(x => x.ToppingId)
                    .OnDelete(DeleteBehavior.Cascade);
            });


            // =====================================================
            // RESTAURANT TABLE
            // =====================================================

            modelBuilder.Entity<RestaurantTable>(entity =>
            {
                entity.ToTable("restaurant_tables");

                entity.HasKey(x => x.TableId);

                entity.Property(x => x.TableNumber)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(x => x.Capacity)
                    .IsRequired();

                entity.Property(x => x.Status)
                    .IsRequired()
                    .HasMaxLength(50);


                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.HasIndex(x => x.TableNumber)
                    .IsUnique()
                    .HasFilter(activeFilter);

                ConfigureRowVersion(entity);
            });


            // =====================================================
            // ORDER
            // =====================================================

            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("orders");

                entity.HasKey(x => x.OrderId);

                entity.Property(x => x.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.Subtotal)
                    .HasPrecision(18, 2);

                entity.Property(x => x.DiscountAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.TaxAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.TotalAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.Property(x => x.UpdatedAt)
                    .IsRequired();

                // Order -> RestaurantTable
                entity.HasOne<RestaurantTable>()
                    .WithMany()
                    .HasForeignKey(x => x.TableId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Order -> User
                entity.HasOne<Users>()
                    .WithMany()
                    .HasForeignKey(x => x.CreatedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.TableId);
                entity.HasIndex(x => x.CreatedBy);
                entity.HasIndex(x => x.Status);

                ConfigureRowVersion(entity);

                // IMPORTANT:
                // KHÔNG unique TableId.
                //
                // Một bàn có thể:
                // Order #1 -> gọi món lần đầu
                // Order #2 -> gọi thêm
                // Order #3 -> gọi thêm tiếp
                //
                // Vì vậy không được:
                // entity.HasIndex(x => x.TableId).IsUnique();
            });


            // =====================================================
            // ORDER ITEM
            // =====================================================

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.ToTable("order_items");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Quanlity)
                    .IsRequired();

                entity.Property(x => x.UnitPrice)
                    .HasPrecision(18, 2);

                entity.Property(x => x.Note)
                    .HasMaxLength(500);

                entity.Property(x => x.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                // Order -> OrderItem
                entity.HasOne<Order>()
                    .WithMany()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                // FoodSize -> OrderItem
                entity.HasOne<FoodSize>()
                    .WithMany()
                    .HasForeignKey(x => x.SizeId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.OrderId);
                entity.HasIndex(x => x.SizeId);
            });


            // =====================================================
            // ORDER ITEM TOPPING
            // =====================================================

            modelBuilder.Entity<OrderItemTopping>(entity =>
            {
                entity.ToTable("order_item_toppings");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Quanlity)
                    .IsRequired();

                entity.Property(x => x.UnitPrice)
                    .HasPrecision(18, 2);

                entity.HasOne<OrderItem>()
                    .WithMany()
                    .HasForeignKey(x => x.OrderItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Topping>()
                    .WithMany()
                    .HasForeignKey(x => x.ToppingId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.OrderItemId);
                entity.HasIndex(x => x.ToppingId);
            });


            // =====================================================
            // ORDER STATUS HISTORY
            // =====================================================

            modelBuilder.Entity<OrderStatusHistory>(entity =>
            {
                entity.ToTable("order_status_histories");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.OldStatus)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.NewStatus)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.Note)
                    .HasMaxLength(500);

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.HasOne<Order>()
                    .WithMany()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Users>()
                    .WithMany()
                    .HasForeignKey(x => x.ChangedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.OrderId);
            });


            // =====================================================
            // KITCHEN ORDER
            // =====================================================

            modelBuilder.Entity<KitchenOrder>(entity =>
            {
                entity.ToTable("kitchen_orders");

                entity.HasKey(x => x.KitchenOrderId);

                entity.Property(x => x.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.SentAt)
                    .IsRequired();

                entity.Property(x => x.Priority)
                    .IsRequired();

                entity.HasOne<Order>()
                    .WithMany()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => x.OrderId);
                entity.HasIndex(x => x.Status);
            });


            // =====================================================
            // KITCHEN ORDER ITEM
            // =====================================================

            modelBuilder.Entity<KitchenOrderItem>(entity =>
            {
                entity.ToTable("kitchen_order_items");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Quanlity)
                    .IsRequired();

                entity.Property(x => x.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.Note)
                    .HasMaxLength(500);

                entity.HasOne<KitchenOrder>()
                    .WithMany()
                    .HasForeignKey(x => x.KitchenOrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<OrderItem>()
                    .WithMany()
                    .HasForeignKey(x => x.OrderItemId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.KitchenOrderId);
                entity.HasIndex(x => x.OrderItemId);
            });


            // =====================================================
            // PAYMENT
            // =====================================================

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("payments");

                entity.HasKey(x => x.PaymentId);

                entity.Property(x => x.Amount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.PaymentMethod)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.TransactionCode)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(x => x.PaidAt)
                    .IsRequired();

                entity.HasOne<Order>()
                    .WithMany()
                    .HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<Users>()
                    .WithMany()
                    .HasForeignKey(x => x.CreatedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.OrderId)
                    .IsUnique()
                    .HasFilter(isSqlServer ? "[Status] = 'Paid'" : "\"Status\" = 'Paid'");
                entity.HasIndex(x => x.TransactionCode);
            });


            // =====================================================
            // PROMOTION
            // =====================================================

            modelBuilder.Entity<Promotion>(entity =>
            {
                entity.ToTable("promotions");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(x => x.Description)
                    .IsRequired()
                    .HasMaxLength(1000);

                entity.Property(x => x.DiscountType)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.DiscountValue)
                    .HasPrecision(18, 2);

                entity.Property(x => x.MinimumOrderAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.MaxDiscountAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.StartAt)
                    .IsRequired();

                entity.Property(x => x.EndAt)
                    .IsRequired();

                entity.Property(x => x.IsActive)
                    .IsRequired();
            });


            // =====================================================
            // PROMOTION CATEGORY
            // =====================================================

            modelBuilder.Entity<PromotionCategory>(entity =>
            {
                entity.ToTable("promotion_categories");

                entity.HasKey(x => x.Id);

                entity.HasOne<Promotion>()
                    .WithMany()
                    .HasForeignKey(x => x.PromotionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Category>()
                    .WithMany()
                    .HasForeignKey(x => x.CategoryId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(x => new
                {
                    x.PromotionId,
                    x.CategoryId
                })
                .IsUnique();
            });


            // =====================================================
            // PROMOTION FOOD
            // =====================================================

            modelBuilder.Entity<PromotionFood>(entity =>
            {
                entity.ToTable("promotion_foods");

                entity.HasKey(x => x.Id);

                entity.HasOne<Promotion>()
                    .WithMany()
                    .HasForeignKey(x => x.PromotionId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<FoodSize>()
                    .WithMany()
                    .HasForeignKey(x => x.SizeId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new
                {
                    x.PromotionId,
                    x.SizeId
                })
                .IsUnique();
            });


            // =====================================================
            // INGREDIENT
            // =====================================================

            modelBuilder.Entity<Ingredient>(entity =>
            {
                entity.ToTable("ingredients");

                entity.HasKey(x => x.IngredientId);

                entity.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(x => x.Unit)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.MinimumStock)
                    .HasPrecision(18, 3);

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.HasIndex(x => x.Name)
                    .IsUnique()
                    .HasFilter(activeFilter);
            });


            // =====================================================
            // INVENTORY
            // =====================================================

            modelBuilder.Entity<Inventory>(entity =>
            {
                entity.ToTable("inventory");

                entity.HasKey(x => x.InventoryId);

                entity.Property(x => x.Quanlity)
                    .HasPrecision(18, 3);

                entity.Property(x => x.UpdatedAt)
                    .IsRequired();

                entity.HasOne<Ingredient>()
                    .WithMany()
                    .HasForeignKey(x => x.IngredientId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Một ingredient chỉ có một inventory record
                entity.HasIndex(x => x.IngredientId)
                    .IsUnique();

                ConfigureRowVersion(entity);
            });


            // =====================================================
            // INVENTORY TRANSACTION
            // =====================================================

            modelBuilder.Entity<InventoryTransaction>(entity =>
            {
                entity.ToTable("inventory_transactions");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Type)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.Quanlity)
                    .HasPrecision(18, 3);

                entity.Property(x => x.QuanlityBefore)
                    .HasPrecision(18, 3);

                entity.Property(x => x.QuanlityAfter)
                    .HasPrecision(18, 3);

                entity.Property(x => x.ReferenceType)
                    .HasMaxLength(100);

                entity.Property(x => x.Note)
                    .HasMaxLength(500);

                entity.Property(x => x.CreatedAt)
                    .IsRequired();

                entity.HasOne<Ingredient>()
                    .WithMany()
                    .HasForeignKey(x => x.IngredientId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<Users>()
                    .WithMany()
                    .HasForeignKey(x => x.CreatedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.IngredientId);
                entity.HasIndex(x => new
                {
                    x.ReferenceType,
                    x.ReferenceId
                });
            });


            // =====================================================
            // SUPPLIER
            // =====================================================

            modelBuilder.Entity<Supplier>(entity =>
            {
                entity.ToTable("suppliers");

                entity.HasKey(x => x.SupplierId);

                entity.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(x => x.Phone)
                    .IsRequired()
                    .HasMaxLength(20);

                entity.Property(x => x.Email)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(x => x.Address)
                    .IsRequired()
                    .HasMaxLength(500);

                entity.Property(x => x.IsActive)
                    .IsRequired();

                entity.HasIndex(x => x.Name);
            });


            // =====================================================
            // PURCHASE ORDER
            // =====================================================

            modelBuilder.Entity<PurchaseOrder>(entity =>
            {
                entity.ToTable("purchase_orders");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Status)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.TotalAmount)
                    .HasPrecision(18, 2);

                entity.Property(x => x.OrderedAt)
                    .IsRequired();

                entity.HasOne<Supplier>()
                    .WithMany()
                    .HasForeignKey(x => x.SupplierId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne<Users>()
                    .WithMany()
                    .HasForeignKey(x => x.CreatedBy)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.SupplierId);
                entity.HasIndex(x => x.Status);
            });


            // =====================================================
            // PURCHASE ORDER ITEM
            // =====================================================

            modelBuilder.Entity<PurchaseOrderItem>(entity =>
            {
                entity.ToTable("purchase_order_items");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Quanlity)
                    .HasPrecision(18, 3);

                entity.Property(x => x.UnitPrice)
                    .HasPrecision(18, 2);

                entity.Property(x => x.TotalPrice)
                    .HasPrecision(18, 2);

                entity.HasOne<PurchaseOrder>()
                    .WithMany()
                    .HasForeignKey(x => x.PurchaseOrderId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Ingredient>()
                    .WithMany()
                    .HasForeignKey(x => x.IngredientId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => x.PurchaseOrderId);
                entity.HasIndex(x => x.IngredientId);
            });


            // =====================================================
            // RECIPE
            // =====================================================

            modelBuilder.Entity<Recipe>(entity =>
            {
                entity.ToTable("recipes");

                entity.HasKey(x => x.RecipeId);

                entity.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(150);

                entity.Property(x => x.Version)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(x => x.IsActive)
                    .IsRequired();

                // Một recipe thuộc một size
                entity.HasOne<FoodSize>()
                    .WithMany()
                    .HasForeignKey(x => x.SizeId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Nhóm B5: mỗi size chỉ được có tối đa MỘT công thức đang hoạt động.
                // Index lọc theo IsActive để vẫn giữ được lịch sử công thức đã ngừng dùng.
                entity.HasIndex(x => x.SizeId)
                    .IsUnique()
                    .HasFilter(activeFilter);
            });


            // =====================================================
            // RECIPE INGREDIENT
            // =====================================================

            modelBuilder.Entity<RecipeIngredient>(entity =>
            {
                entity.ToTable("recipe_ingredients");

                // Composite Primary Key
                entity.HasKey(x => new
                {
                    x.RecipeId,
                    x.IngredientId
                });

                entity.Property(x => x.Quanlity)
                    .HasPrecision(18, 3);

                entity.HasOne<Recipe>()
                    .WithMany()
                    .HasForeignKey(x => x.RecipeId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne<Ingredient>()
                    .WithMany()
                    .HasForeignKey(x => x.IngredientId)
                    .OnDelete(DeleteBehavior.Restrict);
            });
        }

        /// <summary>
        /// Nhóm B7 — concurrency token cho <see cref="Order"/>/<see cref="Inventory"/>/
        /// <see cref="RestaurantTable"/>.
        ///
        /// Trên SQL Server dùng cột <c>rowversion</c> thật (<c>IsRowVersion</c>): DB tự tăng sau mỗi
        /// lần UPDATE nên EF phát hiện được lost update. SQLite (test in-memory) không có rowversion,
        /// nên dùng <c>IsConcurrencyToken</c> + giá trị mới sinh trong <see cref="SaveChangesAsync"/>
        /// để vẫn kiểm chứng được cơ chế chặn ghi đè.
        /// </summary>
        private void ConfigureRowVersion<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
            where TEntity : class
        {
            var property = entity.Property<byte[]>(nameof(Order.RowVersion));

            if (Database.IsSqlServer())
                property.IsRowVersion();
            else
                property.IsConcurrencyToken();
        }

        /// <summary>
        /// Sinh giá trị mới cho concurrency token ở provider không có rowversion (SQLite trong test).
        /// Trên SQL Server, DB tự làm việc này nên bỏ qua.
        /// Đồng thời dịch <see cref="DbUpdateConcurrencyException"/> thành
        /// <see cref="ConcurrencyConflictException"/> để tầng API trả 409 thay vì 500.
        /// </summary>
        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            if (!Database.IsSqlServer())
            {
                foreach (var entry in ChangeTracker.Entries()
                             .Where(e => e.State == EntityState.Modified &&
                                         e.Metadata.FindProperty(nameof(Order.RowVersion)) is not null))
                {
                    entry.Property(nameof(Order.RowVersion)).CurrentValue = Guid.NewGuid().ToByteArray();
                }
            }

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
            if (!Database.IsSqlServer())
            {
                foreach (var entry in ChangeTracker.Entries()
                             .Where(e => e.State == EntityState.Modified &&
                                         e.Metadata.FindProperty(nameof(Order.RowVersion)) is not null))
                {
                    entry.Property(nameof(Order.RowVersion)).CurrentValue = Guid.NewGuid().ToByteArray();
                }
            }

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
    }
}
