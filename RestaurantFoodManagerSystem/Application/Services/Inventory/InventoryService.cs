using Microsoft.EntityFrameworkCore;
using RestaurantFoodManagerSystem.Infrastructure.Persistence;

public sealed class InventoryService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(AppDbContext db, ICurrentUser currentUser, ILogger<InventoryService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public Task<List<Inventory>> GetAllAsync(CancellationToken ct) =>
        _db.Inventories.AsNoTracking().OrderBy(x => x.IngredientId).ToListAsync(ct);

    public async Task<bool> AdjustAsync(int ingredientId, InventoryAdjustmentRequest r, CancellationToken ct)
    {
        var x = await _db.Inventories.FirstOrDefaultAsync(i => i.IngredientId == ingredientId, ct);
        if (x is null) return false;

        var before = x.Quanlity;
        if (before + r.Quanlity < 0) throw new InvalidOperationException("Inventory cannot become negative");

        x.Quanlity += r.Quanlity;
        x.UpdatedAt = DateTime.UtcNow;
        _db.InventoryTransactions.Add(new InventoryTransaction
        {
            IngredientId = ingredientId,
            Type = r.Quanlity >= 0 ? "AdjustmentIn" : "AdjustmentOut",
            Quanlity = Math.Abs(r.Quanlity),
            QuanlityBefore = before,
            QuanlityAfter = x.Quanlity,
            Note = r.Note,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.ResolveActorId(r.CreatedBy, _logger, nameof(r.CreatedBy))
        });
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
