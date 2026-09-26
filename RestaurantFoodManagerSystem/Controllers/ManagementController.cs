using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/management")]
public sealed class ManagementController : ControllerBase
{
    [HttpGet("ingredients")] public Task<List<Ingredient>> Ingredients([FromServices] IngredientService s, CancellationToken ct) => s.GetAllAsync(ct);
    [HttpPost("ingredients")] public async Task<IActionResult> CreateIngredient(IngredientRequest r, [FromServices] IngredientService s, CancellationToken ct) => Ok(await s.CreateAsync(r, ct));
    [HttpDelete("ingredients/{id:int}")] public async Task<IActionResult> DeleteIngredient(int id, [FromServices] IngredientService s, CancellationToken ct) => await s.DeleteAsync(id, ct) ? NoContent() : NotFound();
    [HttpPut("ingredients/{id:int}")] public async Task<IActionResult> UpdateIngredient(int id, IngredientRequest r, [FromServices] IngredientService s, CancellationToken ct) => await s.UpdateAsync(id,r,ct) ? NoContent() : NotFound();
    [HttpGet("suppliers")] public Task<List<Supplier>> Suppliers([FromServices] SupplierService s, CancellationToken ct) => s.GetAllAsync(ct);
    [HttpPost("suppliers")] public async Task<IActionResult> CreateSupplier(SupplierRequest r, [FromServices] SupplierService s, CancellationToken ct) => Ok(await s.CreateAsync(r, ct));
    [HttpDelete("suppliers/{id:int}")] public async Task<IActionResult> DeleteSupplier(int id, [FromServices] SupplierService s, CancellationToken ct) => await s.DeleteAsync(id, ct) ? NoContent() : NotFound();
    [HttpPut("suppliers/{id:int}")] public async Task<IActionResult> UpdateSupplier(int id, SupplierRequest r, [FromServices] SupplierService s, CancellationToken ct) => await s.UpdateAsync(id,r,ct) ? NoContent() : NotFound();
    [HttpGet("inventory")] public Task<List<Inventory>> Inventory([FromServices] InventoryService s, CancellationToken ct) => s.GetAllAsync(ct);
    [HttpPost("inventory/{ingredientId:int}/adjust")] public async Task<IActionResult> AdjustInventory(int ingredientId, InventoryAdjustmentRequest r, [FromServices] InventoryService s, CancellationToken ct) { try { return await s.AdjustAsync(ingredientId,r,ct) ? NoContent() : NotFound(); } catch(InvalidOperationException e) { return BadRequest(e.Message); } }
    [HttpGet("purchase-orders")] public Task<List<PurchaseOrder>> Purchases([FromServices] PurchaseOrderService s, CancellationToken ct) => s.GetAllAsync(ct);
    [HttpPost("purchase-orders")] public async Task<IActionResult> CreatePurchase(PurchaseOrderRequest r, [FromServices] PurchaseOrderService s, CancellationToken ct) { try { return Ok(await s.CreateAsync(r, ct)); } catch (KeyNotFoundException e) { return BadRequest(e.Message); } }
    [HttpPost("purchase-orders/{id:int}/receive")] public async Task<IActionResult> Receive(int id, ReceivePurchaseRequest r, [FromServices] PurchaseOrderService s, CancellationToken ct) => await s.ReceiveAsync(id, r, ct) ? NoContent() : NotFound();
    [HttpGet("promotions")] public Task<List<Promotion>> Promotions([FromServices] PromotionService s, CancellationToken ct) => s.GetAllAsync(ct);
    [HttpPost("promotions")] public async Task<IActionResult> CreatePromotion(PromotionRequest r, [FromServices] PromotionService s, CancellationToken ct) { try { return Ok(await s.CreateAsync(r, ct)); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpPatch("promotions/{id:int}/active")] public async Task<IActionResult> TogglePromotion(int id, [FromQuery] bool active, [FromServices] PromotionService s, CancellationToken ct) => await s.SetActiveAsync(id, active, ct) ? NoContent() : NotFound();
    [HttpPut("promotions/{id:int}")] public async Task<IActionResult> UpdatePromotion(int id, PromotionRequest r, [FromServices] PromotionService s, CancellationToken ct) => await s.UpdateAsync(id,r,ct) ? NoContent() : NotFound();
    [HttpDelete("promotions/{id:int}")] public async Task<IActionResult> DeletePromotion(int id, [FromServices] PromotionService s, CancellationToken ct) => await s.DeleteAsync(id,ct) ? NoContent() : NotFound();
    [HttpGet("recipes")] public Task<List<Recipe>> Recipes([FromServices] RecipeService s, CancellationToken ct) => s.GetAllAsync(ct);
    [HttpGet("recipes/{id:int}")] public async Task<IActionResult> RecipeDetails(int id, [FromServices] RecipeService s, CancellationToken ct) => (await s.GetDetailsAsync(id,ct)) is { } x ? Ok(x) : NotFound();
    [HttpPost("recipes")] public async Task<IActionResult> CreateRecipe(RecipeRequest r, [FromServices] RecipeService s, CancellationToken ct) { try { return Ok(await s.CreateAsync(r, ct)); } catch (KeyNotFoundException e) { return BadRequest(e.Message); } }
    [HttpDelete("recipes/{id:int}")] public async Task<IActionResult> DeleteRecipe(int id, [FromServices] RecipeService s, CancellationToken ct) => await s.DeleteAsync(id,ct) ? NoContent() : NotFound();
    [HttpGet("kitchen/orders")] public Task<List<KitchenOrder>> Kitchen([FromServices] KitchenService s, CancellationToken ct) => s.GetOrdersAsync(ct);
    [HttpGet("kitchen/orders/{id:int}/items")] public Task<List<KitchenOrderItem>> KitchenItems(int id, [FromServices] KitchenService s, CancellationToken ct) => s.GetItemsAsync(id, ct);
    [HttpPatch("kitchen/orders/{id:int}/status")] public async Task<IActionResult> KitchenStatus(int id, KitchenStatusRequest r, [FromServices] KitchenService s, CancellationToken ct) { try { return await s.UpdateOrderStatusAsync(id, r, ct) ? NoContent() : NotFound(); } catch (ArgumentException e) { return BadRequest(e.Message); } }
    [HttpPatch("kitchen/items/{id:int}/status")] public async Task<IActionResult> KitchenItemStatus(int id, KitchenItemStatusRequest r, [FromServices] KitchenService s, CancellationToken ct) { try { return await s.UpdateItemStatusAsync(id, r, ct) ? NoContent() : NotFound(); } catch (ArgumentException e) { return BadRequest(e.Message); } }
}
