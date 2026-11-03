/// <summary>
/// Body của <c>PATCH /api/foods/{id}/active</c> (GĐ6/A4) — khớp payload
/// <c>frontend/js/pages/foods.js</c> gửi: <c>{ isActive: true }</c> để kích hoạt lại món.
/// Khác với <see cref="UpdateFoodAvailabilityRequest"/> (đó là <c>IsAvailable</c> — còn hàng hay không).
/// </summary>
public class UpdateFoodActiveRequest
{
    public bool IsActive { get; set; }
}
