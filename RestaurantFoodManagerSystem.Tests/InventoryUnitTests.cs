using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;
using Xunit;

namespace RestaurantFoodManagerSystem.Tests;

/// <summary>
/// C-17 — Ledger kho phải ghi đơn vị tính, và đơn vị của nguyên liệu bị chặn sửa
/// khi đã có dữ liệu tham chiếu (công thức / ledger / phiếu mua).
///
/// <para>
/// Nếu không ghi đơn vị, một dòng ledger "2" không nói được là 2 kg hay 2 g ⇒ báo cáo
/// tồn kho sai. Nếu cho sửa đơn vị sau khi đã dùng, mọi số lượng cũ bị hiểu sai theo
/// đơn vị mới mà không có cảnh báo.
/// </para>
/// </summary>
public sealed class InventoryUnitTests : TestBase
{
    private static CreateOrderRequest OrderRequest(int tableId, int createdBy, int sizeId, int quantity = 1)
    {
        var request = new CreateOrderRequest { TableId = tableId, CreatedBy = createdBy };
        request.Items.Add(new CreateOrderItemRequest { SizeId = sizeId, Quanlity = quantity });
        return request;
    }

    // ==========================================================
    // Ledger ghi kèm đơn vị
    // ==========================================================

    [Fact]
    public async Task AdjustInventory_WritesUnitOnLedgerRow()
    {
        var ingredient = SeedIngredient(10m, unit: "kg");
        var staff = SeedUser();

        Assert.True(await InventorySvc.AdjustAsync(
            ingredient.IngredientId,
            new InventoryAdjustmentRequest { Quanlity = 5m, CreatedBy = staff.Id, Note = "restock" },
            CancellationToken.None));

        var transaction = Assert.Single(Db.InventoryTransactions);
        Assert.Equal("kg", transaction.Unit);
    }

    [Fact]
    public async Task AdjustInventory_WritesUnitOfThatIngredient_NotOfOthers()
    {
        var grams = SeedIngredient(10m, unit: "g");
        var litres = SeedIngredient(10m, unit: "l");
        var staff = SeedUser();

        await InventorySvc.AdjustAsync(
            grams.IngredientId,
            new InventoryAdjustmentRequest { Quanlity = 1m, CreatedBy = staff.Id, Note = "nap gram" },
            CancellationToken.None);
        await InventorySvc.AdjustAsync(
            litres.IngredientId,
            new InventoryAdjustmentRequest { Quanlity = 1m, CreatedBy = staff.Id, Note = "nap lit" },
            CancellationToken.None);

        Assert.Equal("g", Db.InventoryTransactions.Single(x => x.IngredientId == grams.IngredientId).Unit);
        Assert.Equal("l", Db.InventoryTransactions.Single(x => x.IngredientId == litres.IngredientId).Unit);
    }

    [Fact]
    public async Task ReceivePurchase_WritesUnitOnLedgerRow()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest
        {
            Name = "S", Phone = "1", Email = "a@b.test", Address = "A"
        }, CancellationToken.None);
        var ingredient = SeedIngredient(0m, unit: "kg");
        var staff = SeedUser();

        var purchase = await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items = { new PurchaseOrderItemRequest { IngredientId = ingredient.IngredientId, Quanlity = 9m, UnitPrice = 2m } }
        }, CancellationToken.None);

        Assert.True(await Purchases.ReceiveAsync(
            purchase.Id, new ReceivePurchaseRequest { ReceivedBy = staff.Id }, CancellationToken.None));

        var transaction = Assert.Single(Db.InventoryTransactions.Where(x => x.ReferenceId == purchase.Id));
        Assert.Equal("Purchase", transaction.Type);
        Assert.Equal("kg", transaction.Unit);
    }

    [Fact]
    public async Task CreateOrder_SaleRow_WritesUnit()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);

        var order = await Orders.CreateAsync(
            OrderRequest(scenario.Table.TableId, scenario.Staff.Id, scenario.Size.SizeId, quantity: 2),
            CancellationToken.None);

        var transaction = Assert.Single(Db.InventoryTransactions);
        Assert.Equal("Sale", transaction.Type);
        Assert.Equal(4m, transaction.Quanlity);
        Assert.Equal("kg", transaction.Unit);
        Assert.Equal(order.OrderId, transaction.ReferenceId);
    }

    [Fact]
    public async Task CancelItem_ReturnRow_WritesUnit()
    {
        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        var order = await Orders.CreateAsync(
            OrderRequest(scenario.Table.TableId, scenario.Staff.Id, scenario.Size.SizeId),
            CancellationToken.None);
        var item = Db.OrderItems.Single(x => x.OrderId == order.OrderId);

        Assert.True(await Orders.CancelItemAsync(
            order.OrderId,
            item.Id,
            new CancelOrderItemRequest { Reason = "Khách đổi món", ChangedBy = scenario.Staff.Id },
            CancellationToken.None));

        using var check = NewContext();
        var returned = Assert.Single(check.InventoryTransactions.Where(x =>
            x.Type == "AdjustmentIn" && x.ReferenceId == order.OrderId));
        Assert.Equal("kg", returned.Unit);
    }

    [Fact]
    public async Task GetTransactions_ReturnsUnit_AndKeepsLegacyRowsReadable()
    {
        var ingredient = SeedIngredient(10m, unit: "kg");
        var staff = SeedUser();

        await InventorySvc.AdjustAsync(
            ingredient.IngredientId,
            new InventoryAdjustmentRequest { Quanlity = 5m, CreatedBy = staff.Id, Note = "nap kho" },
            CancellationToken.None);

        // Dòng cũ (trước C-17) chưa có đơn vị ⇒ vẫn đọc được nhờ đơn vị hiện tại.
        Db.InventoryTransactions.Add(new InventoryTransaction
        {
            IngredientId = ingredient.IngredientId,
            Type = "AdjustmentIn",
            Quanlity = 1m,
            QuanlityBefore = 15m,
            QuanlityAfter = 16m,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = staff.Id
        });
        Db.SaveChanges();

        var page = await InventorySvc.GetTransactionsAsync(
            new InventoryTransactionQuery { IngredientId = ingredient.IngredientId });

        Assert.Equal(2, page.Total);
        Assert.All(page.Items, x => Assert.Equal("kg", x.Unit));
    }

    // ==========================================================
    // Chặn sửa đơn vị của nguyên liệu
    // ==========================================================

    [Fact]
    public async Task UpdateIngredient_UnitChange_WhenUnused_IsAllowed()
    {
        var ingredient = SeedIngredient(10m, unit: "kg");

        Assert.True(await Ingredients.UpdateAsync(
            ingredient.IngredientId,
            new IngredientRequest { Name = ingredient.Name, Unit = "g", MinimumStock = 1m },
            CancellationToken.None));

        Assert.Equal("g", Db.Ingredients.Single(x => x.IngredientId == ingredient.IngredientId).Unit);
    }

    [Fact]
    public async Task UpdateIngredient_SameUnitDifferentCase_IsAllowed()
    {
        var ingredient = SeedIngredient(10m, unit: "kg");
        var size = SeedSize(SeedFood(SeedCategory().CategoryId).FoodId, 10m);
        SeedRecipe(size.SizeId, (ingredient.IngredientId, 1m));

        Assert.True(await Ingredients.UpdateAsync(
            ingredient.IngredientId,
            new IngredientRequest { Name = ingredient.Name, Unit = "KG", MinimumStock = 0m },
            CancellationToken.None));

        Assert.Equal("kg", Db.Ingredients.Single(x => x.IngredientId == ingredient.IngredientId).Unit);
    }

    [Fact]
    public async Task UpdateIngredient_UnitChange_WhenUsedInRecipe_IsRejected()
    {
        var ingredient = SeedIngredient(10m, unit: "kg");
        var size = SeedSize(SeedFood(SeedCategory().CategoryId).FoodId, 10m);
        SeedRecipe(size.SizeId, (ingredient.IngredientId, 1m));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Ingredients.UpdateAsync(
            ingredient.IngredientId,
            new IngredientRequest { Name = ingredient.Name, Unit = "g", MinimumStock = 0m },
            CancellationToken.None));

        Assert.Contains("Cannot change unit", ex.Message);
        Assert.Contains("recipes", ex.Message);
        Assert.Equal("kg", Db.Ingredients.Single(x => x.IngredientId == ingredient.IngredientId).Unit);
    }

    [Fact]
    public async Task UpdateIngredient_UnitChange_WhenHasLedgerHistory_IsRejected()
    {
        var ingredient = SeedIngredient(10m, unit: "kg");
        var staff = SeedUser();
        await InventorySvc.AdjustAsync(
            ingredient.IngredientId,
            new InventoryAdjustmentRequest { Quanlity = 1m, CreatedBy = staff.Id, Note = "nap kho" },
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Ingredients.UpdateAsync(
            ingredient.IngredientId,
            new IngredientRequest { Name = ingredient.Name, Unit = "g", MinimumStock = 0m },
            CancellationToken.None));

        Assert.Contains("stock history", ex.Message);
        Assert.Equal("kg", Db.Ingredients.Single(x => x.IngredientId == ingredient.IngredientId).Unit);
    }

    [Fact]
    public async Task UpdateIngredient_UnitChange_WhenHasPurchaseHistory_IsRejected()
    {
        var supplier = await Suppliers.CreateAsync(new SupplierRequest
        {
            Name = "S", Phone = "1", Email = "a@b.test", Address = "A"
        }, CancellationToken.None);
        var ingredient = SeedIngredient(0m, unit: "kg");
        var staff = SeedUser();

        await Purchases.CreateAsync(new PurchaseOrderRequest
        {
            SupplierId = supplier.SupplierId,
            CreatedBy = staff.Id,
            Items = { new PurchaseOrderItemRequest { IngredientId = ingredient.IngredientId, Quanlity = 1m, UnitPrice = 1m } }
        }, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Ingredients.UpdateAsync(
            ingredient.IngredientId,
            new IngredientRequest { Name = ingredient.Name, Unit = "g", MinimumStock = 0m },
            CancellationToken.None));

        Assert.Contains("purchase orders", ex.Message);
        Assert.Equal("kg", Db.Ingredients.Single(x => x.IngredientId == ingredient.IngredientId).Unit);
    }

    [Fact]
    public async Task UpdateIngredient_OtherFields_StillWork_WhenUnitIsLocked()
    {
        var ingredient = SeedIngredient(10m, unit: "kg");
        var size = SeedSize(SeedFood(SeedCategory().CategoryId).FoodId, 10m);
        SeedRecipe(size.SizeId, (ingredient.IngredientId, 1m));

        Assert.True(await Ingredients.UpdateAsync(
            ingredient.IngredientId,
            new IngredientRequest { Name = "Tên mới", Unit = "kg", MinimumStock = 7m },
            CancellationToken.None));

        var stored = Db.Ingredients.Single(x => x.IngredientId == ingredient.IngredientId);
        Assert.Equal("Tên mới", stored.Name);
        Assert.Equal(7m, stored.MinimumStock);
        Assert.Equal("kg", stored.Unit);
    }

    [Fact]
    public async Task UpdateIngredient_UnitChange_WhenNeverUsed_ThenUsed_KeepsWorkingOrder()
    {
        // Sửa đơn vị lúc nguyên liệu còn "trắng" ⇒ hợp lệ, và công thức/ledger
        // ghi sau đó phải dùng đơn vị mới.
        var ingredient = SeedIngredient(10m, unit: "kg");
        Assert.True(await Ingredients.UpdateAsync(
            ingredient.IngredientId,
            new IngredientRequest { Name = ingredient.Name, Unit = "g", MinimumStock = 0m },
            CancellationToken.None));

        var scenario = SeedOrderScenario(sizePrice: 50m, stock: 100m);
        Db.Ingredients.Single(x => x.IngredientId == scenario.Ingredient.IngredientId).Unit = "g";
        Db.SaveChanges();

        await Orders.CreateAsync(
            OrderRequest(scenario.Table.TableId, scenario.Staff.Id, scenario.Size.SizeId),
            CancellationToken.None);

        Assert.Equal("g", Db.InventoryTransactions.Single(x => x.Type == "Sale").Unit);
    }
}

/// <summary>
/// C-17 — hợp đồng HTTP: kho gặp xung đột khi đổi đơn vị ⇒ 409 kèm message cho FE,
/// và API đọc ledger trả kèm đơn vị.
/// </summary>
public sealed class InventoryUnitHttpTests : IClassFixture<RestaurantApiFactory>
{
    private readonly RestaurantApiFactory _factory;

    public InventoryUnitHttpTests(RestaurantApiFactory factory)
    {
        _factory = factory;
    }

    private (int IngredientId, string Name) SeedIngredient(string unit)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ingredient = new Ingredient
        {
            Name = $"ing-{Guid.NewGuid():N}",
            Unit = unit,
            MinimumStock = 0m,
            IsActive = true
        };
        db.Ingredients.Add(ingredient);
        db.SaveChanges();

        db.Inventories.Add(new Inventory
        {
            IngredientId = ingredient.IngredientId,
            Quanlity = 10m,
            UpdatedAt = DateTime.UtcNow
        });
        db.SaveChanges();

        return (ingredient.IngredientId, ingredient.Name);
    }

    [Fact]
    public async Task PutIngredient_UnitChange_WhenUsedInRecipe_ReturnsConflictWithMessage()
    {
        var (ingredientId, name) = SeedIngredient("kg");

        // Gắn nguyên liệu vào một công thức ⇒ đơn vị bị khoá.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var category = new Category
            {
                CategoryName = $"cat-{Guid.NewGuid():N}",
                CategoryDescription = "desc",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Categories.Add(category);
            db.SaveChanges();

            var food = new Food
            {
                CategoryId = category.CategoryId,
                FoodName = $"food-{Guid.NewGuid():N}",
                FoodDescription = "desc",
                FoodImageUrl = "img.png",
                FoodPrice = 10m,
                IsAvailable = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Foods.Add(food);
            db.SaveChanges();

            var size = new FoodSize
            {
                FoodId = food.FoodId,
                SizeName = "M",
                SizePrice = 10m,
                IsDefault = true,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.FoodSizes.Add(size);
            db.SaveChanges();

            var recipe = new Recipe
            {
                SizeId = size.SizeId,
                Name = $"recipe-{Guid.NewGuid():N}",
                Version = "1",
                IsActive = true
            };
            db.Recipes.Add(recipe);
            db.SaveChanges();
            db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = recipe.RecipeId,
                IngredientId = ingredientId,
                Quanlity = 1m
            });
            db.SaveChanges();
        }

        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync($"/api/management/ingredients/{ingredientId}", new
        {
            name,
            unit = "g",
            minimumStock = 0m
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Cannot change unit", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PutIngredient_UnitChange_WhenUnused_Succeeds()
    {
        var (ingredientId, name) = SeedIngredient("kg");
        using var client = _factory.CreateClient();

        var response = await client.PutAsJsonAsync($"/api/management/ingredients/{ingredientId}", new
        {
            name,
            unit = "g",
            minimumStock = 0m
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("g", db.Ingredients.Single(x => x.IngredientId == ingredientId).Unit);
    }

    [Fact]
    public async Task GetInventoryTransactions_ReturnsUnit()
    {
        var (ingredientId, _) = SeedIngredient("kg");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var userName = $"u-{Guid.NewGuid():N}";
            var user = new Users
            {
                UserName = userName,
                UserPassword = "not-used",
                UserFullName = "Unit Test",
                UserEmail = $"{userName}@test.local",
                UserPhone = "0000000000",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Users.Add(user);
            db.SaveChanges();

            db.InventoryTransactions.Add(new InventoryTransaction
            {
                IngredientId = ingredientId,
                Type = "AdjustmentIn",
                Quanlity = 1m,
                QuanlityBefore = 10m,
                QuanlityAfter = 11m,
                Unit = "kg",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = user.Id
            });
            db.SaveChanges();
        }

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/management/inventory-transactions?ingredientId={ingredientId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = page.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("kg", item.GetProperty("unit").GetString());
    }
}
