using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

/// <summary>
/// Compatibility facade kept from the pre-refactor "god service".
///
/// Phase 4 moved the real inventory/supply/purchase/promotion/recipe logic into the
/// dedicated feature services (IngredientService, SupplierService, InventoryService,
/// PurchaseOrderService, PromotionService, RecipeService). This type now only forwards
/// to them so existing callers keep working; it no longer owns any of that logic.
///
/// The kitchen pair (<see cref="Kitchen"/>, <see cref="UpdateKitchen"/>) is intentionally
/// retained for now: it is a duplicate of KitchenService and is not reachable from any
/// controller, but it must not be deleted until the frontend mock-up work is finished
/// and a decision is made about the duplicate kitchen behaviour.
/// </summary>
public sealed class ManagementService
{
    private readonly AppDbContext _db;
    private readonly IngredientService _ingredients;
    private readonly SupplierService _suppliers;
    private readonly InventoryService _inventory;
    private readonly PurchaseOrderService _purchaseOrders;
    private readonly PromotionService _promotions;
    private readonly RecipeService _recipes;

    public ManagementService(
        AppDbContext db,
        IngredientService ingredients,
        SupplierService suppliers,
        InventoryService inventory,
        PurchaseOrderService purchaseOrders,
        PromotionService promotions,
        RecipeService recipes)
    {
        _db = db;
        _ingredients = ingredients;
        _suppliers = suppliers;
        _inventory = inventory;
        _purchaseOrders = purchaseOrders;
        _promotions = promotions;
        _recipes = recipes;
    }

    // ---- ingredients ----

    public Task<List<Ingredient>> Ingredients(CancellationToken ct) => _ingredients.GetAllAsync(ct);
    public Task<Ingredient> CreateIngredient(IngredientRequest r, CancellationToken ct) => _ingredients.CreateAsync(r, ct);
    public Task<bool> UpdateIngredient(int id, IngredientRequest r, CancellationToken ct) => _ingredients.UpdateAsync(id, r, ct);
    public Task<bool> DeleteIngredient(int id, CancellationToken ct) => _ingredients.DeleteAsync(id, ct);

    // ---- suppliers ----

    public Task<List<Supplier>> Suppliers(CancellationToken ct) => _suppliers.GetAllAsync(ct);
    public Task<Supplier> CreateSupplier(SupplierRequest r, CancellationToken ct) => _suppliers.CreateAsync(r, ct);
    public Task<bool> UpdateSupplier(int id, SupplierRequest r, CancellationToken ct) => _suppliers.UpdateAsync(id, r, ct);
    public Task<bool> DeleteSupplier(int id, CancellationToken ct) => _suppliers.DeleteAsync(id, ct);

    // ---- inventory ----

    public Task<List<Inventory>> Inventory(CancellationToken ct) => _inventory.GetAllAsync(ct);
    public Task<bool> AdjustInventory(int ingredientId, InventoryAdjustmentRequest r, CancellationToken ct) => _inventory.AdjustAsync(ingredientId, r, ct);

    // ---- purchase orders ----

    public Task<List<PurchaseOrder>> Purchases(CancellationToken ct) => _purchaseOrders.GetAllAsync(ct);
    public Task<PurchaseOrder> CreatePurchase(PurchaseOrderRequest r, CancellationToken ct) => _purchaseOrders.CreateAsync(r, ct);
    public Task<bool> ReceivePurchase(int id, ReceivePurchaseRequest r, CancellationToken ct) => _purchaseOrders.ReceiveAsync(id, r, ct);

    // ---- promotions ----

    public Task<List<Promotion>> Promotions(CancellationToken ct) => _promotions.GetAllAsync(ct);
    public Task<Promotion> CreatePromotion(PromotionRequest r, CancellationToken ct) => _promotions.CreateAsync(r, ct);
    public Task<bool> UpdatePromotion(int id, PromotionRequest r, CancellationToken ct) => _promotions.UpdateAsync(id, r, ct);
    public Task<bool> TogglePromotion(int id, bool active, CancellationToken ct) => _promotions.SetActiveAsync(id, active, ct);
    public Task<bool> DeletePromotion(int id, CancellationToken ct) => _promotions.DeleteAsync(id, ct);

    // ---- recipes ----

    public Task<List<RecipeSummaryResponse>> Recipes(CancellationToken ct) => _recipes.GetAllAsync(ct);
    public Task<Recipe> CreateRecipe(RecipeRequest r, CancellationToken ct) => _recipes.CreateAsync(r, ct);
    public Task<RecipeResponse?> GetRecipeDetails(int id, CancellationToken ct) => _recipes.GetDetailsAsync(id, ct);
    public Task<bool> DeleteRecipe(int id, CancellationToken ct) => _recipes.DeleteAsync(id, ct);

    // ---- kitchen (duplicate of KitchenService, kept on purpose for now) ----

    public Task<List<KitchenOrder>> Kitchen(CancellationToken ct) =>
        _db.KitchenOrders.AsNoTracking().OrderBy(x => x.Priority).ThenBy(x => x.SentAt).ToListAsync(ct);

    public async Task<bool> UpdateKitchen(int id, KitchenStatusRequest r, CancellationToken ct)
    {
        var x = await _db.KitchenOrders.FindAsync([id], ct);
        if (x is null) return false;

        x.Status = r.Status.Trim();
        if (x.Status.Equals("Preparing", StringComparison.OrdinalIgnoreCase)) x.StartedAt ??= DateTime.UtcNow;
        if (x.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase)) x.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
