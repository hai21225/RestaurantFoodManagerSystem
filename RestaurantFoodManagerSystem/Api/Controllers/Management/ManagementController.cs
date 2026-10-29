using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController, Route("api/management")]
public sealed class ManagementController : ControllerBase
{
    // --- Kho: ingredients / suppliers / inventory / purchase-orders => WarehouseDesk ---
    // C4: trả DTO thay vì entity EF thô; giữ nguyên tên field để frontend hiện tại không phải sửa.

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("ingredients")]
    public async Task<List<IngredientResponse>> Ingredients([FromServices] IngredientService s, CancellationToken ct) =>
        (await s.GetAllAsync(ct)).Select(IngredientResponse.From).ToList();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("ingredients/{id:int}")]
    public async Task<IActionResult> IngredientDetails(int id, [FromServices] IngredientService s, CancellationToken ct)
    {
        var ingredient = await s.GetByIdAsync(id, ct);
        return ingredient is null ? NotFound() : Ok(IngredientResponse.From(ingredient));
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost("ingredients")]
    public async Task<IActionResult> CreateIngredient(IngredientRequest r, [FromServices] IngredientService s, CancellationToken ct)
    {
        try
        {
            return Ok(IngredientResponse.From(await s.CreateAsync(r, ct)));
        }
        catch (InvalidOperationException e)
        {
            return Conflict(new { message = e.Message });
        }
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpDelete("ingredients/{id:int}")]
    public async Task<IActionResult> DeleteIngredient(int id, [FromServices] IngredientService s, CancellationToken ct) =>
        await s.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPut("ingredients/{id:int}")]
    public async Task<IActionResult> UpdateIngredient(int id, IngredientRequest r, [FromServices] IngredientService s, CancellationToken ct)
    {
        try
        {
            return await s.UpdateAsync(id, r, ct) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException e)
        {
            return Conflict(new { message = e.Message });
        }
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("ingredients/{id:int}/active")]
    public async Task<IActionResult> SetIngredientActive(int id, UpdateActiveRequest r, [FromServices] IngredientService s, CancellationToken ct)
    {
        try
        {
            return await s.SetActiveAsync(id, r.IsActive, ct) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException e)
        {
            return Conflict(new { message = e.Message });
        }
    }

    /// <summary>
    /// C-13: kho đánh dấu trạng thái đáp ứng của nguyên liệu (bình thường / hết hàng / hư hỏng).
    /// Nguyên liệu không bị xoá — chỉ đổi trạng thái để quản lý và làm báo cáo.
    /// </summary>
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("ingredients/{id:int}/status")]
    public async Task<IActionResult> SetIngredientStatus(int id, IngredientStatusRequest r, [FromServices] IngredientService s, CancellationToken ct)
    {
        try
        {
            return await s.SetStockStatusAsync(id, r.StockStatus, ct) ? NoContent() : NotFound();
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("suppliers")]
    public async Task<List<SupplierResponse>> Suppliers([FromServices] SupplierService s, CancellationToken ct) =>
        (await s.GetAllAsync(ct)).Select(SupplierResponse.From).ToList();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("suppliers/{id:int}")]
    public async Task<IActionResult> SupplierDetails(int id, [FromServices] SupplierService s, CancellationToken ct)
    {
        var supplier = await s.GetByIdAsync(id, ct);
        return supplier is null ? NotFound() : Ok(SupplierResponse.From(supplier));
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost("suppliers")]
    public async Task<IActionResult> CreateSupplier(SupplierRequest r, [FromServices] SupplierService s, CancellationToken ct) =>
        Ok(SupplierResponse.From(await s.CreateAsync(r, ct)));

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpDelete("suppliers/{id:int}")]
    public async Task<IActionResult> DeleteSupplier(int id, [FromServices] SupplierService s, CancellationToken ct) =>
        await s.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPut("suppliers/{id:int}")]
    public async Task<IActionResult> UpdateSupplier(int id, SupplierRequest r, [FromServices] SupplierService s, CancellationToken ct) =>
        await s.UpdateAsync(id, r, ct) ? NoContent() : NotFound();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("suppliers/{id:int}/active")]
    public async Task<IActionResult> SetSupplierActive(int id, UpdateActiveRequest r, [FromServices] SupplierService s, CancellationToken ct) =>
        await s.SetActiveAsync(id, r.IsActive, ct) ? NoContent() : NotFound();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("inventory")]
    public async Task<List<InventoryResponse>> Inventory([FromServices] InventoryService s, CancellationToken ct) =>
        (await s.GetAllAsync(ct)).Select(InventoryResponse.From).ToList();

    /// <summary>
    /// Đợt 3 / B4 — danh sách nguyên liệu **sắp hết** (tồn ≤ mức tối thiểu), kèm số lượng
    /// còn thiếu. Trước Đợt 3 dashboard chỉ có con số <c>lowStockCount</c> và frontend phải
    /// tự ghép 2 danh sách để biết cái nào sắp hết.
    /// </summary>
    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("inventory/low-stock")]
    public async Task<List<LowStockItemResponse>> LowStockInventory(
        [FromServices] InventoryService s,
        CancellationToken ct) =>
        await s.GetLowStockAsync(ct);

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost("inventory/{ingredientId:int}/adjust")]
    public async Task<IActionResult> AdjustInventory(int ingredientId, InventoryAdjustmentRequest r, [FromServices] InventoryService s, CancellationToken ct)
    {
        try { return await s.AdjustAsync(ingredientId, r, ct) ? NoContent() : NotFound(); }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return BadRequest(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("inventory-transactions")]
    public async Task<IActionResult> InventoryTransactions([FromQuery] InventoryTransactionQuery query, [FromServices] InventoryService s, CancellationToken ct)
    {
        try { return Ok(await s.GetTransactionsAsync(query, ct)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("purchase-orders")]
    public async Task<List<PurchaseOrderResponse>> Purchases([FromServices] PurchaseOrderService s, CancellationToken ct) =>
        (await s.GetAllAsync(ct)).Select(PurchaseOrderResponse.From).ToList();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost("purchase-orders")]
    public async Task<IActionResult> CreatePurchase(PurchaseOrderRequest r, [FromServices] PurchaseOrderService s, CancellationToken ct)
    {
        try { return Ok(PurchaseOrderResponse.From(await s.CreateAsync(r, ct))); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("purchase-orders/{id:int}")]
    public async Task<IActionResult> PurchaseDetails(int id, [FromServices] PurchaseOrderService s, CancellationToken ct) =>
        (await s.GetByIdAsync(id, ct)) is { } x ? Ok(x) : NotFound();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost("purchase-orders/{id:int}/receive")]
    public async Task<IActionResult> Receive(int id, ReceivePurchaseRequest r, [FromServices] PurchaseOrderService s, CancellationToken ct)
    {
        try { return await s.ReceiveAsync(id, r, ct) ? NoContent() : NotFound(); }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost("purchase-orders/{id:int}/cancel")]
    public async Task<IActionResult> CancelPurchase(int id, [FromServices] PurchaseOrderService s, CancellationToken ct)
    {
        try { return await s.CancelAsync(id, ct) ? NoContent() : NotFound(); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    // --- Cấu hình quán (Đợt 2 / A1) => AdminOnly ---

    /// <summary>
    /// Cấu hình quán: tỉ lệ VAT, tỉ lệ phí dịch vụ, thông tin in trên hoá đơn.
    /// Nếu admin chưa cấu hình lần nào thì trả về cấu hình mặc định (VAT 0%, phí DV 0%).
    /// </summary>
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings([FromServices] RestaurantSettingsService s, CancellationToken ct) =>
        Ok(RestaurantSettingsResponse.From(await s.GetAsync(ct)));

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(RestaurantSettingsRequest r, [FromServices] RestaurantSettingsService s, CancellationToken ct)
    {
        try
        {
            return Ok(RestaurantSettingsResponse.From(await s.UpdateAsync(r, ct)));
        }
        catch (ArgumentException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    // --- Khuyến mãi => AdminOnly ---

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpGet("promotions")]
    public async Task<List<PromotionResponse>> Promotions([FromServices] PromotionService s, CancellationToken ct)
    {
        var promotions = await s.GetAllAsync(ct);

        // C-18: target của khuyến mãi (món/danh mục được áp giảm). Đọc 1 lần cho cả danh sách.
        var targets = await s.GetTargetsAsync(
            promotions.Select(x => x.Id).ToList(), ct);

        return promotions
            .Select(x => PromotionResponse.From(x, targets.GetValueOrDefault(x.Id)))
            .ToList();
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpGet("promotions/{id:int}")]
    public async Task<IActionResult> PromotionDetails(int id, [FromServices] PromotionService s, CancellationToken ct)
    {
        var promotion = await s.GetByIdAsync(id, ct);
        if (promotion is null) return NotFound(new { message = "Promotion not found" });

        // C-18: kèm target để FE mở form sửa là có đủ dữ liệu, không cần gọi thêm.
        var targets = await s.GetTargetsAsync([id], ct);

        return Ok(PromotionResponse.From(promotion, targets.GetValueOrDefault(id)));
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPost("promotions")]
    public async Task<IActionResult> CreatePromotion(PromotionRequest r, [FromServices] PromotionService s, CancellationToken ct)
    {
        try { return Ok(PromotionResponse.From(await s.CreateAsync(r, ct))); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    /// <summary>
    /// C-18: **thay toàn bộ** target của khuyến mãi (món theo size + danh mục).
    ///
    /// <para>
    /// Gửi mảng rỗng ⇒ bỏ giới hạn ⇒ khuyến mãi áp cho toàn bộ hoá đơn.
    /// Thao tác này **không** tính lại các đơn đang mở đã áp khuyến mãi — số tiền giảm
    /// đã ghi trên đơn giữ nguyên; đơn sửa món sẽ tính lại theo target mới.
    /// </para>
    /// </summary>
    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("promotions/{id:int}/targets")]
    public async Task<IActionResult> SetPromotionTargets(
        int id,
        PromotionTargetsRequest r,
        [FromServices] PromotionService s,
        CancellationToken ct)
    {
        // `KeyNotFoundException` (target không tồn tại) do ApiExceptionFilter đổi thành 404.
        return await s.SetTargetsAsync(id, r, ct)
            ? NoContent()
            : NotFound(new { message = "Promotion not found" });
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPatch("promotions/{id:int}/active")]
    public async Task<IActionResult> TogglePromotion(int id, [FromQuery] bool? active, [FromServices] PromotionService s, CancellationToken ct)
    {
        // `bool` không nullable sẽ mặc định false khi thiếu query param ⇒ gọi thiếu tham số
        // lại vô tình TẮT promotion. Bắt buộc phải truyền `?active=true|false`.
        if (active is null) return BadRequest(new { message = "active is required" });

        return await s.SetActiveAsync(id, active.Value, ct) ? NoContent() : NotFound();
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpPut("promotions/{id:int}")]
    public async Task<IActionResult> UpdatePromotion(int id, PromotionRequest r, [FromServices] PromotionService s, CancellationToken ct)
    {
        try { return await s.UpdateAsync(id, r, ct) ? NoContent() : NotFound(); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.AdminOnly)]
    [HttpDelete("promotions/{id:int}")]
    public async Task<IActionResult> DeletePromotion(int id, [FromServices] PromotionService s, CancellationToken ct) =>
        await s.DeleteAsync(id, ct) ? NoContent() : NotFound();

    // --- Công thức => WarehouseDesk ---

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("recipes")]
    public Task<List<RecipeSummaryResponse>> Recipes(
        [FromQuery] bool includeInactive,
        [FromServices] RecipeService s,
        CancellationToken ct) =>
        s.GetAllAsync(includeInactive, ct);

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpGet("recipes/{id:int}")]
    public async Task<IActionResult> RecipeDetails(int id, [FromServices] RecipeService s, CancellationToken ct) =>
        (await s.GetDetailsAsync(id, ct)) is { } x ? Ok(x) : NotFound();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPost("recipes")]
    public async Task<IActionResult> CreateRecipe(RecipeRequest r, [FromServices] RecipeService s, CancellationToken ct)
    {
        try
        {
            var recipe = await s.CreateAsync(r, ct);
            return Ok(RecipeSummaryResponse.From(recipe, r.Ingredients.Count));
        }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPut("recipes/{id:int}")]
    public async Task<IActionResult> UpdateRecipe(int id, RecipeRequest r, [FromServices] RecipeService s, CancellationToken ct)
    {
        try { return await s.UpdateAsync(id, r, ct) ? NoContent() : NotFound(); }
        catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpDelete("recipes/{id:int}")]
    public async Task<IActionResult> DeleteRecipe(int id, [FromServices] RecipeService s, CancellationToken ct) =>
        await s.DeleteAsync(id, ct) ? NoContent() : NotFound();

    [Authorize(Policy = AuthPolicies.WarehouseDesk)]
    [HttpPatch("recipes/{id:int}/active")]
    public async Task<IActionResult> SetRecipeActive(int id, UpdateActiveRequest r, [FromServices] RecipeService s, CancellationToken ct)
    {
        try
        {
            return await s.SetActiveAsync(id, r.IsActive, ct) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException e)
        {
            return Conflict(new { message = e.Message });
        }
    }

    // --- Bếp => KitchenDesk ---

    [Authorize(Policy = AuthPolicies.KitchenDesk)]
    [HttpGet("kitchen/orders")]
    public async Task<List<KitchenOrderResponse>> Kitchen(
        [FromQuery] string? status,
        [FromQuery] DateTime? since,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] KitchenService s,
        CancellationToken ct) =>
        await s.GetOrdersAsync(ct, status, since, page, pageSize);

    [Authorize(Policy = AuthPolicies.KitchenDesk)]
    [HttpGet("kitchen/orders/{id:int}/items")]
    public Task<List<KitchenItemResponse>> KitchenItems(int id, [FromServices] KitchenService s, CancellationToken ct) =>
        s.GetItemsAsync(id, ct);

    [Authorize(Policy = AuthPolicies.KitchenDesk)]
    [HttpPatch("kitchen/orders/{id:int}/status")]
    public async Task<IActionResult> KitchenStatus(int id, KitchenStatusRequest r, [FromServices] KitchenService s, CancellationToken ct)
    {
        try { return await s.UpdateOrderStatusAsync(id, r, ct) ? NoContent() : NotFound(); }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.KitchenDesk)]
    [HttpPatch("kitchen/items/{id:int}/status")]
    public async Task<IActionResult> KitchenItemStatus(int id, KitchenItemStatusRequest r, [FromServices] KitchenService s, CancellationToken ct)
    {
        try { return await s.UpdateItemStatusAsync(id, r, ct) ? NoContent() : NotFound(); }
        catch (ConcurrencyConflictException e) { return Conflict(new { message = e.Message }); }
        catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.KitchenDesk)]
    [HttpPost("kitchen/items/{id:int}/reopen")]
    public async Task<IActionResult> KitchenItemReopen(int id, ReopenKitchenItemRequest r, [FromServices] KitchenService s, CancellationToken ct)
    {
        try { return await s.ReopenItemAsync(id, r, ct) ? NoContent() : NotFound(); }
        catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.KitchenDesk)]
    [HttpPatch("kitchen/items/{id:int}/prepared-quantity")]
    public async Task<IActionResult> KitchenPreparedQuantity(int id, PreparedQuantityRequest r, [FromServices] KitchenService s, CancellationToken ct)
    {
        try { return await s.UpdatePreparedQuantityAsync(id, r, ct) ? NoContent() : NotFound(); }
        catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }

    [Authorize(Policy = AuthPolicies.KitchenDesk)]
    [HttpPost("kitchen/items/{id:int}/shortage")]
    public async Task<IActionResult> KitchenShortage(int id, ReportShortageRequest r, [FromServices] KitchenService s, CancellationToken ct)
    {
        try { return await s.ReportShortageAsync(id, r, ct) ? NoContent() : NotFound(); }
        catch (UnauthorizedAccessException e) { return StatusCode(StatusCodes.Status403Forbidden, new { message = e.Message }); }
        catch (InvalidOperationException e) { return Conflict(new { message = e.Message }); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
    }
}
