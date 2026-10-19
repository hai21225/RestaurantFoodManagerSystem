using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class InventoryService
{
    private readonly AppDbContext _db;

    public InventoryService(AppDbContext db) => _db = db;

    public Task<List<Inventory>> GetAllAsync(CancellationToken ct) =>
        _db.Inventories.AsNoTracking().OrderBy(x => x.IngredientId).ToListAsync(ct);

    public async Task<bool> AdjustAsync(int ingredientId, InventoryAdjustmentRequest r, CancellationToken ct)
    {
        var x = await _db.Inventories.FirstOrDefaultAsync(i => i.IngredientId == ingredientId, ct);
        if (x is null) return false;

        var before = x.Quanlity;
        if (before + r.Quantity < 0) throw new InvalidOperationException("Inventory cannot become negative");

        x.Quanlity += r.Quantity;
        x.UpdatedAt = DateTime.UtcNow;
        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            IngredientId = ingredientId,
            Type = r.Quantity >= 0 ? "AdjustmentIn" : "AdjustmentOut",
            Quanlity = Math.Abs(r.Quantity),
            QuanlityBefore = before,
            QuanlityAfter = x.Quanlity,
            Note = r.Note,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = r.CreatedBy
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
