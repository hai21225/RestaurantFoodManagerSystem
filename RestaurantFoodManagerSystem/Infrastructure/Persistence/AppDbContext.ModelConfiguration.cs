using Microsoft.EntityFrameworkCore;

namespace RestaurantFoodManagerSystem.Infrastructure.Persistence
{
    public partial class AppDbContext
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var isSqlServer = Database.IsSqlServer();
            var activeFilter = isSqlServer ? "[IsActive] = 1" : "\"IsActive\" = 1";

            modelBuilder.Entity<Users>(entity =>
            {
                entity.ToTable("users");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).ValueGeneratedOnAdd();
                entity.Property(x => x.UserName).IsRequired().HasMaxLength(100);
                entity.Property(x => x.UserPassword).IsRequired().HasMaxLength(255);
                entity.Property(x => x.UserFullName).IsRequired().HasMaxLength(150);
                entity.Property(x => x.UserEmail).IsRequired().HasMaxLength(150);
                entity.Property(x => x.UserPhone).IsRequired().HasMaxLength(20);
                entity.HasIndex(x => x.UserName).IsUnique();
                entity.HasIndex(x => x.UserEmail).IsUnique();
            });

            modelBuilder.Entity<Roles>(entity =>
            {
                entity.ToTable("roles");
                entity.HasKey(x => x.RoleId);
                entity.Property(x => x.RoleName).IsRequired().HasMaxLength(100);
                entity.Property(x => x.RoleDescription).IsRequired().HasMaxLength(255);
                entity.HasIndex(x => x.RoleName).IsUnique();
            });

            modelBuilder.Entity<UserRoles>(entity =>
            {
                entity.ToTable("user_roles");
                entity.HasKey(x => new { x.UserId, x.RoleId });
                entity.HasOne<Users>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Roles>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("categories");
                entity.HasKey(x => x.CategoryId);
                entity.Property(x => x.CategoryName).IsRequired().HasMaxLength(100);
                entity.Property(x => x.CategoryDescription).IsRequired().HasMaxLength(500);
                entity.Property(x => x.DisplayOrder).IsRequired();
                entity.Property(x => x.IsActive).IsRequired();
                entity.HasIndex(x => x.CategoryName).IsUnique().HasFilter(activeFilter);
            });

            modelBuilder.Entity<Food>(entity =>
            {
                entity.ToTable("foods");
                entity.HasKey(x => x.FoodId);
                entity.Property(x => x.FoodName).IsRequired().HasMaxLength(150);
                entity.Property(x => x.FoodDescription).IsRequired().HasMaxLength(1000);
                entity.Property(x => x.FoodImageUrl).IsRequired().HasMaxLength(500);
                entity.Property(x => x.FoodPrice).HasPrecision(18, 2);
                entity.Property(x => x.IsAvailable).IsRequired();
                entity.Property(x => x.IsActive).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired();
                entity.Property(x => x.UpdatedAt).IsRequired();
                entity.Property(x => x.InactiveReason).HasMaxLength(500);
                entity.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.CategoryId);
            });

            modelBuilder.Entity<FoodSize>(entity =>
            {
                entity.ToTable("food_sizes");
                entity.HasKey(x => x.SizeId);
                entity.Property(x => x.SizeName).IsRequired().HasMaxLength(50);
                entity.Property(x => x.SizePrice).HasPrecision(18, 2);
                entity.Property(x => x.IsActive).IsRequired();
                entity.Property(x => x.IsDefault).IsRequired();
                entity.Property(x => x.CreatedAt).IsRequired();
                entity.Property(x => x.UpdatedAt).IsRequired();
                entity.HasOne<Food>().WithMany().HasForeignKey(x => x.FoodId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => new { x.FoodId, x.SizeName }).IsUnique().HasFilter(activeFilter);
            });

            modelBuilder.Entity<Topping>(entity =>
            {
                entity.ToTable("toppings");
                entity.HasKey(x => x.ToppingId);
                entity.Property(x => x.ToppingName).IsRequired().HasMaxLength(100);
                entity.Property(x => x.ToppingPrice).HasPrecision(18, 2);
                entity.Property(x => x.IsActive).IsRequired();
                entity.Property(x => x.IsAvailable).IsRequired();
                entity.HasIndex(x => x.ToppingName).IsUnique().HasFilter(activeFilter);
            });

            modelBuilder.Entity<FoodTopping>(entity =>
            {
                entity.ToTable("food_toppings");
                entity.HasKey(x => new { x.FoodId, x.ToppingId });
                entity.HasOne<Food>().WithMany().HasForeignKey(x => x.FoodId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Topping>().WithMany().HasForeignKey(x => x.ToppingId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TableArea>(entity =>
            {
                entity.ToTable("table_areas");
                entity.HasKey(x => x.AreaId);
                entity.Property(x => x.AreaName).IsRequired().HasMaxLength(100);
                entity.Property(x => x.Description).HasMaxLength(500);
                entity.Property(x => x.DisplayOrder).IsRequired();
                entity.Property(x => x.IsActive).IsRequired();
                entity.HasIndex(x => x.AreaName).IsUnique().HasFilter(activeFilter);
            });

            modelBuilder.Entity<RestaurantTable>(entity =>
            {
                entity.ToTable("restaurant_tables");
                entity.HasKey(x => x.TableId);
                entity.Property(x => x.TableNumber).IsRequired().HasMaxLength(20);
                entity.Property(x => x.Capacity).IsRequired();
                entity.Property(x => x.Status).IsRequired().HasMaxLength(50);
                entity.Property(x => x.IsActive).IsRequired();
                entity.HasIndex(x => x.TableNumber).IsUnique().HasFilter(activeFilter);
                entity.HasIndex(x => x.AreaId);
                entity.HasOne(x => x.Area).WithMany().HasForeignKey(x => x.AreaId).OnDelete(DeleteBehavior.SetNull);
                ConfigureRowVersion(entity);
            });

            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("orders", table =>
                {
                    if (isSqlServer)
                    {
                        table.HasCheckConstraint(
                            "CK_orders_Status",
                            "[Status] IN ('Pending','SentToKitchen','Preparing','Ready','Completed','Paid','Cancelled')");
                    }
                });

                entity.HasKey(x => x.OrderId);
                entity.Property(x => x.Status).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Subtotal).HasPrecision(18, 2);
                entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
                entity.Property(x => x.TaxAmount).HasPrecision(18, 2);
                entity.Property(x => x.ServiceChargeAmount).HasPrecision(18, 2);
                entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
                entity.Property(x => x.Note).HasMaxLength(500);
                entity.Property(x => x.PromotionDiscountType).HasMaxLength(50);
                entity.Property(x => x.PromotionDiscountValue).HasPrecision(18, 2);
                entity.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.SetNull);
                entity.Property(x => x.CreatedAt).IsRequired();
                entity.Property(x => x.UpdatedAt).IsRequired();
                entity.HasOne<RestaurantTable>().WithMany().HasForeignKey(x => x.TableId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Users>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.TableId);
                entity.HasIndex(x => x.CreatedBy);
                entity.HasIndex(x => x.Status);
                entity.HasIndex(x => x.PromotionId);
                ConfigureRowVersion(entity);
            });

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.ToTable("order_items", table =>
                {
                    if (isSqlServer)
                    {
                        table.HasCheckConstraint(
                            "CK_order_items_Status",
                            "[Status] IN ('Pending','Preparing','Ready','Completed','Cancelled')");
                    }
                });

                entity.HasKey(x => x.Id);
                entity.Property(x => x.Quanlity).IsRequired();
                entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
                entity.Property(x => x.Note).HasMaxLength(500);
                entity.Property(x => x.Status).IsRequired().HasMaxLength(50);
                entity.Property(x => x.CreatedAt).IsRequired();
                entity.Property(x => x.CancelReason).HasMaxLength(500);
                entity.Property(x => x.OrderItemGroupId);
                entity.HasOne<Users>().WithMany().HasForeignKey(x => x.CancelledBy).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<FoodSize>().WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.OrderId);
                entity.HasIndex(x => x.SizeId);
                entity.HasIndex(x => x.OrderItemGroupId);
            });

            modelBuilder.Entity<OrderItemTopping>(entity =>
            {
                entity.ToTable("order_item_toppings");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Quanlity).IsRequired();
                entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
                entity.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Topping>().WithMany().HasForeignKey(x => x.ToppingId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.OrderItemId);
                entity.HasIndex(x => x.ToppingId);
            });

            modelBuilder.Entity<OrderStatusHistory>(entity =>
            {
                entity.ToTable("order_status_histories");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.OldStatus).IsRequired().HasMaxLength(50);
                entity.Property(x => x.NewStatus).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Note).HasMaxLength(500);
                entity.Property(x => x.ReasonCode).HasMaxLength(50);
                entity.Property(x => x.CreatedAt).IsRequired();
                entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Users>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.OrderId);
            });

            modelBuilder.Entity<RestaurantSettings>(entity =>
            {
                entity.ToTable("restaurant_settings");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Id).ValueGeneratedNever();
                entity.Property(x => x.RestaurantName).IsRequired().HasMaxLength(200);
                entity.Property(x => x.Address).IsRequired().HasMaxLength(500);
                entity.Property(x => x.Phone).IsRequired().HasMaxLength(50);
                entity.Property(x => x.TaxCode).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Currency).IsRequired().HasMaxLength(10);
                entity.Property(x => x.TaxRate).HasPrecision(5, 2);
                entity.Property(x => x.TaxIncluded).IsRequired();
                entity.Property(x => x.ServiceChargeRate).HasPrecision(5, 2);
                entity.Property(x => x.UpdatedAt).IsRequired();
            });

            modelBuilder.Entity<OrderItemStatusHistory>(entity =>
            {
                entity.ToTable("order_item_status_histories");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.OldStatus).IsRequired().HasMaxLength(50);
                entity.Property(x => x.NewStatus).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Reason).HasMaxLength(500);
                entity.Property(x => x.ReasonCode).HasMaxLength(50);
                entity.Property(x => x.CreatedAt).IsRequired();
                entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Users>().WithMany().HasForeignKey(x => x.ChangedBy).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.OrderId);
                entity.HasIndex(x => x.OrderItemId);
                entity.HasIndex(x => x.KitchenOrderItemId);
                entity.HasIndex(x => x.CreatedAt);
            });

            modelBuilder.Entity<KitchenOrder>(entity =>
            {
                entity.ToTable("kitchen_orders", table =>
                {
                    if (isSqlServer)
                    {
                        table.HasCheckConstraint(
                            "CK_kitchen_orders_Status",
                            "[Status] IN ('Pending','Accepted','Preparing','Ready','Completed','Cancelled')");
                    }
                });

                entity.HasKey(x => x.KitchenOrderId);
                entity.Property(x => x.Status).IsRequired().HasMaxLength(50);
                entity.Property(x => x.SentAt).IsRequired();
                entity.Property(x => x.Priority).IsRequired();
                entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => x.OrderId);
                entity.HasIndex(x => x.Status);
            });

            modelBuilder.Entity<KitchenOrderItem>(entity =>
            {
                entity.ToTable("kitchen_order_items", table =>
                {
                    if (isSqlServer)
                    {
                        table.HasCheckConstraint(
                            "CK_kitchen_order_items_Status",
                            "[Status] IN ('Pending','Preparing','Ready','Completed','Cancelled')");
                    }
                });

                entity.HasKey(x => x.Id);
                entity.Property(x => x.Quanlity).IsRequired();
                entity.Property(x => x.Status).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Note).HasMaxLength(500);
                entity.HasOne<KitchenOrder>().WithMany().HasForeignKey(x => x.KitchenOrderId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.KitchenOrderId);
                entity.HasIndex(x => x.OrderItemId);
            });

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("payments", table =>
                {
                    if (isSqlServer)
                    {
                        table.HasCheckConstraint(
                            "CK_payments_Status",
                            "[Status] IN ('Pending','Paid','Failed','Refunded','Cancelled')");
                    }
                });

                entity.HasKey(x => x.PaymentId);
                entity.Property(x => x.Amount).HasPrecision(18, 2);
                entity.Property(x => x.PaymentMethod).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Status).IsRequired().HasMaxLength(50);
                entity.Property(x => x.TransactionCode).IsRequired().HasMaxLength(100);
                entity.Property(x => x.PaidAt);
                entity.Property(x => x.Provider).IsRequired().HasMaxLength(30);
                entity.Property(x => x.RequestId).HasMaxLength(50);
                entity.Property(x => x.ProviderTransactionId).HasMaxLength(100);
                entity.Property(x => x.Note).HasMaxLength(500);
                entity.Property(x => x.ProviderResponse);
                entity.Property(x => x.CreatedAt).IsRequired();
                entity.Property(x => x.RefundedAt);
                entity.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Users>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.OrderId).IsUnique().HasFilter(isSqlServer ? "[Status] = 'Paid'" : "\"Status\" = 'Paid'");
                entity.HasIndex(x => x.TransactionCode).IsUnique().HasFilter(isSqlServer ? "[TransactionCode] <> ''" : "\"TransactionCode\" <> ''");
                entity.HasIndex(x => x.RequestId);
            });

            modelBuilder.Entity<Promotion>(entity =>
            {
                entity.ToTable("promotions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Description).IsRequired().HasMaxLength(1000);
                entity.Property(x => x.DiscountType).IsRequired().HasMaxLength(50);
                entity.Property(x => x.DiscountValue).HasPrecision(18, 2);
                entity.Property(x => x.MinimumOrderAmount).HasPrecision(18, 2);
                entity.Property(x => x.MaxDiscountAmount).HasPrecision(18, 2);
                entity.Property(x => x.StartAt).IsRequired();
                entity.Property(x => x.EndAt).IsRequired();
                entity.Property(x => x.IsActive).IsRequired();
            });

            modelBuilder.Entity<PromotionCategory>(entity =>
            {
                entity.ToTable("promotion_categories");
                entity.HasKey(x => x.Id);
                entity.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(x => new { x.PromotionId, x.CategoryId }).IsUnique();
            });

            modelBuilder.Entity<PromotionFood>(entity =>
            {
                entity.ToTable("promotion_foods");
                entity.HasKey(x => x.Id);
                entity.HasOne<Promotion>().WithMany().HasForeignKey(x => x.PromotionId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<FoodSize>().WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => new { x.PromotionId, x.SizeId }).IsUnique();
            });

            modelBuilder.Entity<Ingredient>(entity =>
            {
                entity.ToTable("ingredients");
                entity.HasKey(x => x.IngredientId);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Unit).IsRequired().HasMaxLength(50);
                entity.Property(x => x.MinimumStock).HasPrecision(18, 3);
                entity.Property(x => x.IsActive).IsRequired();
                entity.Property(x => x.StockStatus).IsRequired().HasMaxLength(30).HasDefaultValue(IngredientStockStatuses.Available);
                entity.HasIndex(x => x.Name).IsUnique().HasFilter(activeFilter);
            });

            modelBuilder.Entity<Inventory>(entity =>
            {
                entity.ToTable("inventory");
                entity.HasKey(x => x.InventoryId);
                entity.Property(x => x.Quanlity).HasPrecision(18, 3);
                entity.Property(x => x.UpdatedAt).IsRequired();
                entity.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.IngredientId).IsUnique();
                ConfigureRowVersion(entity);
            });

            modelBuilder.Entity<InventoryTransaction>(entity =>
            {
                entity.ToTable("inventory_transactions");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Type).IsRequired().HasMaxLength(50);
                entity.Property(x => x.Quanlity).HasPrecision(18, 3);
                entity.Property(x => x.QuanlityBefore).HasPrecision(18, 3);
                entity.Property(x => x.QuanlityAfter).HasPrecision(18, 3);
                entity.Property(x => x.ReferenceType).HasMaxLength(100);
                entity.Property(x => x.Unit).HasMaxLength(50);
                entity.Property(x => x.Note).HasMaxLength(500);
                entity.Property(x => x.CreatedAt).IsRequired();
                entity.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Users>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.SetNull);
                entity.HasIndex(x => x.IngredientId);
                entity.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
                entity.HasIndex(x => x.OrderItemId);
            });

            modelBuilder.Entity<Supplier>(entity =>
            {
                entity.ToTable("suppliers");
                entity.HasKey(x => x.SupplierId);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Phone).IsRequired().HasMaxLength(20);
                entity.Property(x => x.Email).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Address).IsRequired().HasMaxLength(500);
                entity.Property(x => x.IsActive).IsRequired();
                entity.HasIndex(x => x.Name);
            });

            modelBuilder.Entity<PurchaseOrder>(entity =>
            {
                entity.ToTable("purchase_orders");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Status).IsRequired().HasMaxLength(50);
                entity.Property(x => x.TotalAmount).HasPrecision(18, 2);
                entity.Property(x => x.OrderedAt).IsRequired();
                entity.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
                entity.HasOne<Users>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.SupplierId);
                entity.HasIndex(x => x.Status);
            });

            modelBuilder.Entity<PurchaseOrderItem>(entity =>
            {
                entity.ToTable("purchase_order_items");
                entity.HasKey(x => x.Id);
                entity.Property(x => x.Quanlity).HasPrecision(18, 3);
                entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
                entity.Property(x => x.TotalPrice).HasPrecision(18, 2);
                entity.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.PurchaseOrderId);
                entity.HasIndex(x => x.IngredientId);
            });

            modelBuilder.Entity<Recipe>(entity =>
            {
                entity.ToTable("recipes");
                entity.HasKey(x => x.RecipeId);
                entity.Property(x => x.Name).IsRequired().HasMaxLength(150);
                entity.Property(x => x.Version).IsRequired().HasMaxLength(50);
                entity.Property(x => x.IsActive).IsRequired();
                entity.HasOne<FoodSize>().WithMany().HasForeignKey(x => x.SizeId).OnDelete(DeleteBehavior.Restrict);
                entity.HasIndex(x => x.SizeId).IsUnique().HasFilter(activeFilter);
            });

            modelBuilder.Entity<RecipeIngredient>(entity =>
            {
                entity.ToTable("recipe_ingredients");
                entity.HasKey(x => new { x.RecipeId, x.IngredientId });
                entity.Property(x => x.Quanlity).HasPrecision(18, 3);
                entity.HasOne<Recipe>().WithMany().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne<Ingredient>().WithMany().HasForeignKey(x => x.IngredientId).OnDelete(DeleteBehavior.Restrict);
            });
        }

        private void ConfigureRowVersion<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
            where TEntity : class
        {
            var property = entity.Property<byte[]>(nameof(Order.RowVersion));

            if (Database.IsSqlServer())
                property.IsRowVersion();
            else
                property.IsConcurrencyToken();
        }
    }
}
