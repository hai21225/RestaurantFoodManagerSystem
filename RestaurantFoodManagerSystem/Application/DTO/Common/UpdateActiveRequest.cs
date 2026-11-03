/// <summary>
/// Body của các endpoint <c>PATCH .../{id}/active</c> dùng chung cho các entity có
/// soft-delete bằng cột <c>IsActive</c> (topping, size, nguyên liệu, nhà cung cấp, công thức).
/// </summary>
public sealed class UpdateActiveRequest
{
    public bool IsActive { get; set; }
}
