using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// Rà soát hồi quy (2026-10-20) — khoá lại các lỗi vừa sửa:
/// tiền (khuyến mãi, phiếu nhập), mật khẩu/role, actor FK, và các lỗi HTTP.
/// </summary>
public sealed class RegressionFixTests : TestBase
{
    // ==========================================================
    // 1. Khuyến mãi không được tính tiền của món đã huỷ
    // ==========================================================

    [Fact]
    public async Task ApplyPromotion_IgnoresCancelledItems()
    {
        var scenario = SeedOrderScenario(sizePrice: 100m, stock: 1000m);
        var otherFood = SeedFood(scenario.Category.CategoryId, 200m);
        var otherSize = SeedSize(otherFood.FoodId, 200m);
        var otherIngredient = SeedIngredient(1000m);
        SeedRecipe(otherSize.SizeId, (otherIngredient.IngredientId, 1m));

        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id,
            Items =
            {
                new CreateOrderItemRequest { SizeId = scenario.Size.SizeId, Quanlity = 1 },
                new CreateOrderItemRequest { SizeId = otherSize.SizeId, Quanlity = 1 }
            }
        }, CancellationToken.None);

        // Huỷ món đầu (giá 100) ⇒ Subtotal còn 200.
        var firstItem = Db.OrderItems.Single(x => x.OrderId == order.OrderId && x.SizeId == scenario.Size.SizeId);
        await Orders.CancelItemAsync(
            order.OrderId,
            firstItem.Id,
            new CancelOrderItemRequest { Reason = "Khách đổi món", ChangedBy = scenario.Staff.Id },
            CancellationToken.None);

        var promotion = await Promotions.CreateAsync(new PromotionRequest
        {
            Name = "giảm 50% món B",
            Description = "",
            DiscountType = "Percent",
            DiscountValue = 50m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            MinimumOrderAmount = 0m,
            MaxDiscountAmount = 0m
        }, CancellationToken.None);

        Db.PromotionFoods.Add(new PromotionFood { PromotionId = promotion.Id, SizeId = otherSize.SizeId });
        Db.SaveChanges();

        Assert.True(await Orders.ApplyPromotionAsync(order.OrderId, promotion.Id, CancellationToken.None));

        // Chỉ món B (200) được giảm 50% ⇒ 100. Nếu tính cả món đã huỷ thì base = 300 ⇒ giảm 150.
        Assert.Equal(200m, order.Subtotal);
        Assert.Equal(100m, order.DiscountAmount);
    }

    // ==========================================================
    // 2. Phiếu nhập: tổng phiếu = tổng dòng hàng (đã làm tròn 2 chữ số)
    // ==========================================================

    [Fact]
    public async Task CreatePurchase_TotalMatchesSumOfRoundedLines()
    {
        var supplier = await Suppliers.CreateAsync(
            new SupplierRequest { Name = "S", Phone = "1", Email = "a@b.test", Address = "A" },
            CancellationToken.None);
        var first = SeedIngredient(0m);
        var second = SeedIngredient(0m);
        var staff = SeedUser();

        var purchase = await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items =
            {
                new PurchaseOrderItemRequest { IngredientId = first.IngredientId, Quanlity = 1.5m, UnitPrice = 3.33m },
                new PurchaseOrderItemRequest { IngredientId = second.IngredientId, Quanlity = 1.5m, UnitPrice = 3.33m }
            }
        }, CancellationToken.None);

        var items = Db.PurchaseOrderItems.Where(x => x.PurchaseOrderId == purchase.Id).ToList();

        // 1.5 * 3.33 = 4.995 ⇒ làm tròn 5.00 trước khi ghi (cột DB là decimal(18,2)).
        Assert.All(items, x => Assert.Equal(5.00m, x.TotalPrice));
        Assert.Equal(items.Sum(x => x.TotalPrice), purchase.TotalAmount);
        Assert.Equal(10.00m, purchase.TotalAmount);
    }

    // ==========================================================
    // 3. Khuyến mãi: giá trị lưu/áp dụng phải khớp với giá trị API trả về
    // ==========================================================

    [Fact]
    public async Task CreatePromotion_RoundsDiscountValues()
    {
        var promotion = await Promotions.CreateAsync(new PromotionRequest
        {
            Name = "p",
            Description = "",
            DiscountType = "Percent",
            DiscountValue = 10.555m,
            StartAt = DateTime.UtcNow.AddDays(-1),
            EndAt = DateTime.UtcNow.AddDays(1),
            MinimumOrderAmount = 49.999m,
            MaxDiscountAmount = 19.994m
        }, CancellationToken.None);

        Assert.Equal(10.56m, promotion.DiscountValue);
        Assert.Equal(50.00m, promotion.MinimumOrderAmount);
        Assert.Equal(19.99m, promotion.MaxDiscountAmount);
    }

    // ==========================================================
    // 4. Mật khẩu: BCrypt chỉ dùng 72 byte đầu
    // ==========================================================

    [Fact]
    public async Task Register_RejectsPasswordLongerThan72Bytes()
    {
        // 40 ký tự 'á' = 80 byte UTF-8 nhưng chỉ 40 ký tự ⇒ check theo ký tự sẽ bỏ sót.
        var password = new string('á', 40);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Auth.Register(new UserDto
        {
            UserName = $"reg-{Guid.NewGuid():N}",
            Email = $"reg-{Guid.NewGuid():N}@example.test",
            Password = password,
            ConfirmPassword = password
        }));

        Assert.Contains("6 and 72", ex.Message);
    }

    [Fact]
    public async Task Register_RejectsWhitespaceOnlyPassword()
    {
        var password = "      ";

        await Assert.ThrowsAsync<ArgumentException>(() => Auth.Register(new UserDto
        {
            UserName = $"reg-{Guid.NewGuid():N}",
            Email = $"reg-{Guid.NewGuid():N}@example.test",
            Password = password,
            ConfirmPassword = password
        }));
    }

    // ==========================================================
    // 5. Chặn gỡ role Admin cuối cùng: chỉ tính Admin ĐANG HOẠT ĐỘNG
    // ==========================================================

    [Fact]
    public async Task RemoveAdminRole_Blocked_WhenOtherAdminIsInactive()
    {
        SeedRole(AuthPolicies.Roles.Admin);

        var activeAdmin = SeedUser();
        var inactiveAdmin = SeedUser(isActive: false);
        var role = Db.Roles.Single(r => r.RoleName == AuthPolicies.Roles.Admin);

        Db.UserRoles.AddRange(
            new UserRoles { UserId = activeAdmin.Id, RoleId = role.RoleId },
            new UserRoles { UserId = inactiveAdmin.Id, RoleId = role.RoleId });
        Db.SaveChanges();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Auth.RemoveRoleFromUserAsync(activeAdmin.Id, AuthPolicies.Roles.Admin, CancellationToken.None));

        Assert.Equal("Cannot remove the last Admin role", ex.Message);
    }

    // ==========================================================
    // 6. Gán role: user/role không tồn tại, gán trùng
    // ==========================================================

    [Fact]
    public async Task AssignRoleToUser_Throws_WhenUserDoesNotExist()
    {
        SeedRole("Chef");

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => Auth.AssignRoleToUser(999999, "Chef", CancellationToken.None));
    }

    [Fact]
    public async Task AssignRoleToUser_Throws_WhenRoleAlreadyAssigned()
    {
        var user = SeedUser();
        SeedRole("Chef");

        await Auth.AssignRoleToUser(user.Id, "Chef", CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Auth.AssignRoleToUser(user.Id, "Chef", CancellationToken.None));

        Assert.Equal("Role is already assigned to this user", ex.Message);
    }

    [Fact]
    public async Task AssignRoleToUser_TrimsRoleName()
    {
        var user = SeedUser();
        SeedRole("Chef");

        Assert.True(await Auth.AssignRoleToUser(user.Id, "  Chef  ", CancellationToken.None));
    }

    // ==========================================================
    // 7. Actor không tồn tại ⇒ lỗi 400 rõ ràng thay vì 500 (FK users)
    // ==========================================================

    [Fact]
    public async Task CreateOrder_RejectsUnknownActor()
    {
        var scenario = SeedOrderScenario();

        await Assert.ThrowsAsync<ArgumentException>(() => Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = 999999,
            Items = { new CreateOrderItemRequest { SizeId = scenario.Size.SizeId, Quanlity = 1 } }
        }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateOrderStatus_FallsBackToOrderCreator_WhenActorMissing()
    {
        var scenario = SeedOrderScenario();
        var order = await Orders.CreateAsync(new CreateOrderRequest
        {
            TableId = scenario.Table.TableId,
            CreatedBy = scenario.Staff.Id,
            Items = { new CreateOrderItemRequest { SizeId = scenario.Size.SizeId, Quanlity = 1 } }
        }, CancellationToken.None);

        // Không gửi changedBy ⇒ không được ghi id 0 (sẽ hỏng FK sang users).
        Assert.True(await Orders.UpdateStatusAsync(
            order.OrderId,
            new UpdateOrderStatusRequest { Status = OrderStatuses.Preparing },
            CancellationToken.None));

        var history = Db.OrderStatusHistories.Single(x => x.OrderId == order.OrderId);
        Assert.Equal(scenario.Staff.Id, history.ChangedBy);
    }

    // ==========================================================
    // 8. Giờ báo cáo: mốc UTC luôn có Kind=Utc (JSON có đuôi "Z")
    // ==========================================================

    [Theory]
    [InlineData("day")]
    [InlineData("week")]
    [InlineData("month")]
    public void VietnamTime_ReturnsUtcKind(string period)
    {
        var (_, fromUtc, toUtc, startVn, endVn) = VietnamTime.Resolve(period, null);

        Assert.Equal(DateTimeKind.Utc, fromUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, toUtc.Kind);
        Assert.Equal(DateTimeKind.Unspecified, startVn.Kind);
        Assert.Equal(DateTimeKind.Unspecified, endVn.Kind);
    }

    [Fact]
    public void VietnamTime_TreatsUnspecifiedDateAsVietnamTime()
    {
        var (_, fromUtc, _, startVn, _) = VietnamTime.Resolve("day", new DateTime(2026, 10, 20));

        Assert.Equal(new DateTime(2026, 10, 20), startVn);
        Assert.Equal(new DateTime(2026, 10, 19, 17, 0, 0, DateTimeKind.Utc), fromUtc);
    }

    // ==========================================================
    // 9. Biến động kho: referenceId bắt buộc kèm referenceType
    // ==========================================================

    [Fact]
    public async Task GetTransactions_RejectsReferenceIdWithoutType()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => InventorySvc.GetTransactionsAsync(
            new InventoryTransactionQuery { ReferenceId = 7 },
            CancellationToken.None));
    }

    [Fact]
    public async Task AdjustInventory_RejectsZeroQuantity()
    {
        var ingredient = SeedIngredient(5m);
        var staff = SeedUser();

        await Assert.ThrowsAsync<ArgumentException>(() => InventorySvc.AdjustAsync(
            ingredient.IngredientId,
            new InventoryAdjustmentRequest { Quanlity = 0m, CreatedBy = staff.Id },
            CancellationToken.None));
    }
}

/// <summary>Rà soát hồi quy ở mức HTTP (status code + message).</summary>
public sealed class RegressionFixHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public RegressionFixHttpTests(RestaurantApiFactory factory) => _factory = factory;

    private void Seed(Action<AppDbContext> seed)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        seed(db);
        db.SaveChanges();
    }

    [Fact]
    public async Task CreateTable_WithDuplicateNumber_ReturnsConflict()
    {
        var suffix = Guid.NewGuid().ToString("N");

        Seed(db => db.RestaurantTables.Add(new RestaurantTable
        {
            TableNumber = $"DUP-{suffix}",
            Capacity = 4,
            Status = "Available",
            IsActive = true
        }));

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/restaurant-tables", new
        {
            tableNumber = $"DUP-{suffix}",
            capacity = 4
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Table number already exists", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TogglePromotion_WithoutActiveParam_ReturnsBadRequest()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var promotionId = 0;

        Seed(db =>
        {
            var promotion = new Promotion
            {
                Name = $"promo-{suffix}",
                Description = "d",
                DiscountType = "Percent",
                DiscountValue = 10m,
                StartAt = DateTime.UtcNow.AddDays(-1),
                EndAt = DateTime.UtcNow.AddDays(1),
                IsActive = true
            };
            db.Promotions.Add(promotion);
            db.SaveChanges();
            promotionId = promotion.Id;
        });

        using var client = _factory.CreateClient();
        var response = await client.PatchAsync($"/api/management/promotions/{promotionId}/active", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(db.Promotions.Single(x => x.Id == promotionId).IsActive);
    }

    [Fact]
    public async Task AdjustInventory_WithZeroQuantity_ReturnsBadRequest()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var ingredientId = 0;
        var userId = 0;

        Seed(db =>
        {
            var user = new Users
            {
                UserName = $"adj-{suffix}",
                UserPassword = "unused",
                UserFullName = "Adjust",
                UserEmail = $"adj-{suffix}@test.local",
                UserPhone = "0000000000",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);

            var ingredient = new Ingredient
            {
                Name = $"ing-{suffix}",
                Unit = "kg",
                MinimumStock = 0m,
                IsActive = true
            };
            db.Ingredients.Add(ingredient);
            db.SaveChanges();

            db.Inventories.Add(new Inventory
            {
                IngredientId = ingredient.IngredientId,
                Quanlity = 5m,
                UpdatedAt = DateTime.UtcNow
            });
            db.SaveChanges();

            ingredientId = ingredient.IngredientId;
            userId = user.Id;
        });

        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync($"/api/management/inventory/{ingredientId}/adjust", new
        {
            quantity = 0m,
            createdBy = userId
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
