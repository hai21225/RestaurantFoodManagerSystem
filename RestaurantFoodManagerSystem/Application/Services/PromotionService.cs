public sealed class PromotionService
{
    private readonly ManagementService _management;
    public PromotionService(ManagementService management) => _management = management;
    public Task<List<Promotion>> GetAllAsync(CancellationToken ct) => _management.Promotions(ct);
    public Task<Promotion> CreateAsync(PromotionRequest request, CancellationToken ct) => _management.CreatePromotion(request, ct);
    public Task<bool> UpdateAsync(int id, PromotionRequest request, CancellationToken ct) => _management.UpdatePromotion(id, request, ct);
    public Task<bool> SetActiveAsync(int id, bool active, CancellationToken ct) => _management.TogglePromotion(id, active, ct);
    public Task<bool> DeleteAsync(int id, CancellationToken ct) => _management.DeletePromotion(id, ct);
}
