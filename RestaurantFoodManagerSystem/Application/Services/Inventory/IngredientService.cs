public sealed class IngredientService
{
    private readonly ManagementService _management;
    public IngredientService(ManagementService management) => _management = management;
    public Task<List<Ingredient>> GetAllAsync(CancellationToken ct) => _management.Ingredients(ct);
    public Task<Ingredient> CreateAsync(IngredientRequest request, CancellationToken ct) => _management.CreateIngredient(request, ct);
    public Task<bool> UpdateAsync(int id, IngredientRequest request, CancellationToken ct) => _management.UpdateIngredient(id, request, ct);
    public Task<bool> DeleteAsync(int id, CancellationToken ct) => _management.DeleteIngredient(id, ct);
}
