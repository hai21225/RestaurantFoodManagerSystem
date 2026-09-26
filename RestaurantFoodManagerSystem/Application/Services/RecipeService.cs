public sealed class RecipeService
{
    private readonly ManagementService _management;
    public RecipeService(ManagementService management) => _management = management;
    public Task<List<Recipe>> GetAllAsync(CancellationToken ct) => _management.Recipes(ct);
    public Task<Recipe> CreateAsync(RecipeRequest request, CancellationToken ct) => _management.CreateRecipe(request, ct);
    public Task<RecipeResponse?> GetDetailsAsync(int id, CancellationToken ct) => _management.GetRecipeDetails(id, ct);
    public Task<bool> DeleteAsync(int id, CancellationToken ct) => _management.DeleteRecipe(id, ct);
}
