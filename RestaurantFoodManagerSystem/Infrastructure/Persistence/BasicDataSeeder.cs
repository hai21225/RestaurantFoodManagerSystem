using Microsoft.EntityFrameworkCore;

namespace RestaurantFoodManagerSystem.Infrastructure.Persistence;

public static class BasicDataSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var now = DateTime.UtcNow;

        if (!await db.RestaurantSettings.AnyAsync(x => x.Id == RestaurantSettings.SingletonId, ct))
        {
            db.RestaurantSettings.Add(new RestaurantSettings
            {
                Id = RestaurantSettings.SingletonId,
                RestaurantName = "Nhà Hàng Ẩm Thực Việt",
                Address = "123 Nguyễn Văn Linh, Đà Nẵng",
                Phone = "0905123456",
                TaxCode = "0401234567",
                Currency = "VND",
                TaxRate = 8m,
                TaxIncluded = true,
                ServiceChargeRate = 0m,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(ct);
        }

        await SeedUsersAsync(db, now, ct);
        await SeedTablesAsync(db, ct);
        await SeedSuppliersAndInventoryAsync(db, now, ct);
        await SeedMenuAndRecipesAsync(db, now, ct);
    }

    private static async Task SeedUsersAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var roles = await db.Roles.ToDictionaryAsync(r => r.RoleName, r => r.RoleId, ct);
        var accounts = new (string UserName, string FullName, string Email, string Phone, string Role)[]
        {
            ("admin", "Quản Trị Viên", "admin@restaurant.local", "0905000001", "Admin"),
            ("warehouse", "Quản Lý Kho", "warehouse@restaurant.local", "0905000002", "WarehouseManager"),
            ("chef", "Bếp Trưởng", "chef@restaurant.local", "0905000003", "Chef"),
            ("cashier", "Thu Ngân", "cashier@restaurant.local", "0905000004", "Cashier"),
            ("waiter", "Phục Vụ", "waiter@restaurant.local", "0905000005", "Waiter")
        };

        string? defaultPasswordHash = null;
        foreach (var acc in accounts)
        {
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.UserName == acc.UserName || u.UserEmail == acc.Email, ct);

            if (user is null)
            {
                defaultPasswordHash ??= BCrypt.Net.BCrypt.HashPassword("123456");
                user = new Users
                {
                    UserName = acc.UserName,
                    UserPassword = defaultPasswordHash,
                    UserFullName = acc.FullName,
                    UserEmail = acc.Email,
                    UserPhone = acc.Phone,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }

            if (roles.TryGetValue(acc.Role, out var roleId) &&
                !await db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == roleId, ct))
            {
                db.UserRoles.Add(new UserRoles { UserId = user.Id, RoleId = roleId });
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task SeedTablesAsync(AppDbContext db, CancellationToken ct)
    {
        var areaIds = await db.TableAreas
            .Where(a => a.IsActive)
            .OrderBy(a => a.DisplayOrder)
            .Select(a => a.AreaId)
            .ToListAsync(ct);

        if (areaIds.Count == 0)
        {
            return;
        }

        var existingNumbers = await db.RestaurantTables
            .Where(t => t.IsActive)
            .Select(t => t.TableNumber)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, ct);

        var tables = new (string Number, int Capacity, int AreaIndex)[]
        {
            ("B01", 4, 0), ("B02", 4, 0), ("B03", 6, 0),
            ("B04", 4, 1 % areaIds.Count), ("B05", 8, 1 % areaIds.Count),
            ("B06", 2, 2 % areaIds.Count)
        };

        foreach (var t in tables.Where(t => !existingNumbers.Contains(t.Number)))
        {
            db.RestaurantTables.Add(new RestaurantTable
            {
                TableNumber = t.Number,
                Capacity = t.Capacity,
                Status = "Available",
                AreaId = areaIds[t.AreaIndex],
                IsActive = true
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedSuppliersAndInventoryAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var existingSuppliers = await db.Suppliers
            .Select(s => s.Name)
            .ToHashSetAsync(StringComparer.OrdinalIgnoreCase, ct);

        var suppliers = new (string Name, string Phone, string Email, string Address)[]
        {
            ("Thực Phẩm Sạch Đà Nẵng", "02363888999", "contact@thucphamsachdn.vn", "KCN Hòa Khánh, Đà Nẵng"),
            ("Nông Sản & Đồ Uống Miền Trung", "02363777888", "sales@nongsanmientrung.vn", "Chợ Đầu Mối Hòa Cường, Đà Nẵng")
        };

        foreach (var s in suppliers.Where(s => !existingSuppliers.Contains(s.Name)))
        {
            db.Suppliers.Add(new Supplier
            {
                Name = s.Name,
                Phone = s.Phone,
                Email = s.Email,
                Address = s.Address,
                IsActive = true
            });
        }
        await db.SaveChangesAsync(ct);

        var items = new (string Name, string Unit, decimal MinStock, decimal InitialStock)[]
        {
            ("Thịt bò tươi", "g", 2000m, 50000m),
            ("Thịt gà ta", "g", 2000m, 50000m),
            ("Bánh phở", "g", 3000m, 60000m),
            ("Mì Quảng", "g", 3000m, 60000m),
            ("Rau sống tổng hợp", "g", 1000m, 30000m),
            ("Phô mai Mozzarella", "g", 1000m, 20000m),
            ("Cốt trà đen", "ml", 2000m, 50000m),
            ("Sữa tươi tiệt trùng", "ml", 2000m, 50000m)
        };

        foreach (var item in items)
        {
            var ing = await db.Ingredients.FirstOrDefaultAsync(
                i => i.IsActive && i.Name == item.Name, ct);

            if (ing is null)
            {
                ing = new Ingredient
                {
                    Name = item.Name,
                    Unit = item.Unit,
                    MinimumStock = item.MinStock,
                    StockStatus = IngredientStockStatuses.Available,
                    IsActive = true
                };
                db.Ingredients.Add(ing);
                await db.SaveChangesAsync(ct);
            }

            if (!await db.Inventories.AnyAsync(inv => inv.IngredientId == ing.IngredientId, ct))
            {
                db.Inventories.Add(new Inventory
                {
                    IngredientId = ing.IngredientId,
                    Quanlity = item.InitialStock,
                    UpdatedAt = now
                });
                await db.SaveChangesAsync(ct);
            }
        }
    }

    private static async Task SeedMenuAndRecipesAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var categories = new (string Name, string Description, int Order)[]
        {
            ("Món chính", "Các món phở, mì, cơm", 1),
            ("Món ăn kèm", "Món khai vị và ăn vặt", 2),
            ("Đồ uống", "Trà, sữa và nước giải khát", 3)
        };

        var catMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in categories)
        {
            var cat = await db.Categories.FirstOrDefaultAsync(
                x => x.IsActive && x.CategoryName == c.Name, ct);
            if (cat is null)
            {
                cat = new Category
                {
                    CategoryName = c.Name,
                    CategoryDescription = c.Description,
                    DisplayOrder = c.Order,
                    IsActive = true
                };
                db.Categories.Add(cat);
                await db.SaveChangesAsync(ct);
            }
            catMap[c.Name] = cat.CategoryId;
        }

        var toppings = new (string Name, decimal Price)[]
        {
            ("Thêm bò viên", 15000m),
            ("Thêm sốt phô mai", 12000m),
            ("Trân châu trắng", 10000m)
        };

        var topMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in toppings)
        {
            var top = await db.Toppings.FirstOrDefaultAsync(
                x => x.IsActive && x.ToppingName == t.Name, ct);
            if (top is null)
            {
                top = new Topping
                {
                    ToppingName = t.Name,
                    ToppingPrice = t.Price,
                    IsActive = true,
                    IsAvailable = true
                };
                db.Toppings.Add(top);
                await db.SaveChangesAsync(ct);
            }
            topMap[t.Name] = top.ToppingId;
        }

        var foods = new (string Name, string Cat, string Desc, string Img, decimal Price, string Topping, (string Size, decimal Price, bool IsDefault)[] Sizes)[]
        {
            ("Phở Bò Tái Nạm", "Món chính", "Phở bò truyền thống nước dùng ninh xương 12 giờ", "/images/foods/pho-bo.jpg", 45000m, "Thêm bò viên",
                [("S", 45000m, true), ("M", 55000m, false), ("L", 65000m, false)]),
            ("Mì Quảng Gà", "Món chính", "Mì Quảng gà ta chuẩn vị Quảng Nam", "/images/foods/mi-quang-ga.jpg", 45000m, "Thêm bò viên",
                [("M", 45000m, true), ("L", 55000m, false)]),
            ("Gà Rán Phủ Phô Mai", "Món ăn kèm", "Gà rán giòn phủ phô mai Mozzarella kéo sợi", "/images/foods/ga-ran-pho-mai.jpg", 50000m, "Thêm sốt phô mai",
                [("M", 50000m, true)]),
            ("Trà Sữa Truyền Thống", "Đồ uống", "Hồng trà ủ lạnh kết hợp sữa tươi thơm béo", "/images/foods/tra-sua.jpg", 30000m, "Trân châu trắng",
                [("M", 30000m, true), ("L", 38000m, false)])
        };

        foreach (var f in foods)
        {
            var food = await db.Foods.FirstOrDefaultAsync(
                x => x.IsActive && x.FoodName == f.Name, ct);
            if (food is null)
            {
                food = new Food
                {
                    CategoryId = catMap[f.Cat],
                    FoodName = f.Name,
                    FoodDescription = f.Desc,
                    FoodImageUrl = f.Img,
                    FoodPrice = f.Price,
                    IsAvailable = true,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                db.Foods.Add(food);
                await db.SaveChangesAsync(ct);
            }

            if (topMap.TryGetValue(f.Topping, out var topId) &&
                !await db.FoodToppings.AnyAsync(ft => ft.FoodId == food.FoodId && ft.ToppingId == topId, ct))
            {
                db.FoodToppings.Add(new FoodTopping { FoodId = food.FoodId, ToppingId = topId });
                await db.SaveChangesAsync(ct);
            }

            foreach (var s in f.Sizes)
            {
                if (!await db.FoodSizes.AnyAsync(fs => fs.FoodId == food.FoodId && fs.IsActive && fs.SizeName == s.Size, ct))
                {
                    db.FoodSizes.Add(new FoodSize
                    {
                        FoodId = food.FoodId,
                        SizeName = s.Size,
                        SizePrice = s.Price,
                        IsDefault = s.IsDefault,
                        IsActive = true,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // Đảm bảo mọi FoodSize đang active đều có công thức active để gọi món POS không lỗi 409
        var activeIngredients = await db.Ingredients
            .Where(i => i.IsActive)
            .OrderBy(i => i.IngredientId)
            .ToListAsync(ct);

        if (activeIngredients.Count == 0)
        {
            return;
        }

        var sizesWithoutRecipe = await (
            from fs in db.FoodSizes
            join f in db.Foods on fs.FoodId equals f.FoodId
            where fs.IsActive && f.IsActive && !db.Recipes.Any(r => r.SizeId == fs.SizeId && r.IsActive)
            select new { fs.SizeId, fs.SizeName, f.FoodName }
        ).ToListAsync(ct);

        foreach (var size in sizesWithoutRecipe)
        {
            var recipe = new Recipe
            {
                SizeId = size.SizeId,
                Name = $"Công thức {size.FoodName} ({size.SizeName})",
                Version = "1.0",
                IsActive = true
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync(ct);

            var primaryIngredient = PickIngredientForFood(size.FoodName, activeIngredients);
            db.RecipeIngredients.Add(new RecipeIngredient
            {
                RecipeId = recipe.RecipeId,
                IngredientId = primaryIngredient.IngredientId,
                Quanlity = size.SizeName == "L" ? 150m : 100m
            });
            await db.SaveChangesAsync(ct);
        }
    }

    private static Ingredient PickIngredientForFood(string foodName, List<Ingredient> ingredients)
    {
        var lower = foodName.ToLowerInvariant();
        if (lower.Contains("phở") || lower.Contains("bò"))
            return ingredients.FirstOrDefault(i => i.Name.Contains("bò", StringComparison.OrdinalIgnoreCase)) ?? ingredients[0];
        if (lower.Contains("gà") || lower.Contains("mì"))
            return ingredients.FirstOrDefault(i => i.Name.Contains("gà", StringComparison.OrdinalIgnoreCase)) ?? ingredients[0];
        if (lower.Contains("trà") || lower.Contains("sữa"))
            return ingredients.FirstOrDefault(i => i.Name.Contains("trà", StringComparison.OrdinalIgnoreCase)) ?? ingredients[0];
        return ingredients[0];
    }
}
