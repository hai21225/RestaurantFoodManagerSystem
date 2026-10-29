/// <summary>
/// Kết quả của <c>GET/POST /api/management/suppliers</c> (C4).
/// Giữ nguyên tên field của entity <see cref="Supplier"/>.
/// </summary>
public sealed class SupplierResponse
{
    public int SupplierId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    public static SupplierResponse From(Supplier x) => new()
    {
        SupplierId = x.SupplierId,
        Name = x.Name,
        Phone = x.Phone,
        Email = x.Email,
        Address = x.Address,
        IsActive = x.IsActive
    };
}
