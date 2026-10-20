using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

public sealed class CreateOrderRequest
{
    [Range(1, int.MaxValue)] public int TableId { get; set; }
    [Range(1, int.MaxValue)] public int CreatedBy { get; set; }
    [MinLength(1)] public List<CreateOrderItemRequest> Items { get; set; } = [];
}

public sealed class CreateOrderItemRequest
{
    [Range(1, int.MaxValue)] public int SizeId { get; set; }
    // C# dùng tên Quanlity theo quy ước của project/DB; JSON vẫn là "quantity" để không phá frontend.
    [Range(1, int.MaxValue)] [JsonPropertyName("quantity")] public int Quanlity { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
    public List<CreateOrderToppingRequest> Toppings { get; set; } = [];
}

public sealed class CreateOrderToppingRequest
{
    [Range(1, int.MaxValue)] public int ToppingId { get; set; }
    [Range(1, int.MaxValue)] [JsonPropertyName("quantity")] public int Quanlity { get; set; } = 1;
}

/// <summary>
/// Thay toàn bộ dòng món của một đơn đang mở (<c>PUT /api/orders/{id}/items</c>).
/// Đây là thao tác replace-all: dòng không gửi lên sẽ bị xoá, dòng mới sẽ được thêm.
/// </summary>
public sealed class UpdateOrderItemsRequest
{
    [Range(1, int.MaxValue)] public int ChangedBy { get; set; }
    [MinLength(1)] public List<CreateOrderItemRequest> Items { get; set; } = [];
}

/// <summary>
/// Gọi thêm món vào một đơn đang mở (<c>POST /api/orders/{id}/items</c>).
/// Khác với <see cref="UpdateOrderItemsRequest"/>: chỉ thêm dòng mới và luôn tạo một
/// round/ticket bếp mới, không replace-all.
/// </summary>
public sealed class AddOrderItemsRequest
{
    [Range(1, int.MaxValue)] public int ChangedBy { get; set; }
    [MinLength(1)] public List<CreateOrderItemRequest> Items { get; set; } = [];
}

/// <summary>
/// Giảm/tăng số lượng của một món (<c>PATCH /api/orders/{id}/items/{itemId}/quantity</c>).
/// Chỉ truyền một trong <see cref="Quantity"/> (số lượng đích) hoặc <see cref="Delta"/>.
/// </summary>
public sealed class AdjustOrderItemQuantityRequest
{
    /// <summary>Số lượng đích của cả nhóm dòng (>= 0). 0 tương đương huỷ món.</summary>
    [Range(0, int.MaxValue)] public int? Quantity { get; set; }

    /// <summary>Số lượng thay đổi so với hiện tại (âm = giảm, dương = tăng).</summary>
    public int? Delta { get; set; }

    public int ChangedBy { get; set; }

    /// <summary>
    /// <c>CustomerChange</c> (mặc định), <c>OutOfStock</c>, <c>PhysicalShortage</c>,
    /// hoặc <c>KitchenError</c>.
    /// </summary>
    [MaxLength(50)] public string? ReasonCode { get; set; }

    /// <summary>Lý do chi tiết; bắt buộc khi số lượng đích = 0.</summary>
    [MaxLength(500)] public string? Reason { get; set; }

    /// <summary>
    /// Cho phép Admin giảm xuống dưới số phần đã nấu. Phần đã nấu bị giảm sẽ được ghi
    /// nhận là wastage, không hoàn kho ảo.
    /// </summary>
    public bool AllowWastage { get; set; }
}

/// <summary>
/// Tham số lọc/phân trang cho <c>GET /api/orders</c> (P1). Tất cả đều tùy chọn.
/// <c>From</c>/<c>To</c> lọc theo <c>Order.CreatedAt</c> (bao gồm cả hai đầu).
/// </summary>
public sealed class OrderListQuery
{
    /// <summary>Lọc theo <c>Order.Status</c> (không phân biệt hoa/thường).</summary>
    public string? Status { get; set; }

    /// <summary>Lọc theo bàn.</summary>
    public int? TableId { get; set; }

    /// <summary>Cận dưới của <c>CreatedAt</c> (UTC).</summary>
    public DateTime? From { get; set; }

    /// <summary>Cận trên của <c>CreatedAt</c> (UTC).</summary>
    public DateTime? To { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class UpdateOrderStatusRequest
{
    [Required] public string Status { get; set; } = string.Empty;
    public int ChangedBy { get; set; }

    /// <summary>Ghi chú trạng thái; bắt buộc khi <see cref="Status"/> = Cancelled (lý do huỷ).</summary>
    public string? Note { get; set; }

    /// <summary>
    /// Lý do huỷ: <c>CustomerChange</c> (mặc định), <c>OutOfStock</c>,
    /// <c>PhysicalShortage</c>, hoặc <c>KitchenError</c>. Chỉ dùng khi <see cref="Status"/>
    /// = <c>Cancelled</c>; quyết định có hoàn kho hay ghi wastage.
    /// </summary>
    [MaxLength(50)] public string? ReasonCode { get; set; }
}

/// <summary>
/// Huỷ một dòng món tại POS/bếp (<c>POST /api/orders/{orderId}/items/{itemId}/cancel</c>).
/// </summary>
public sealed class CancelOrderItemRequest
{
    [Required, MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Lý do huỷ: <c>CustomerChange</c> (mặc định), <c>OutOfStock</c>,
    /// <c>PhysicalShortage</c>, hoặc <c>KitchenError</c>; quyết định có hoàn kho hay ghi wastage.
    /// </summary>
    [MaxLength(50)] public string? ReasonCode { get; set; }

    public int ChangedBy { get; set; }
}

/// <summary>
/// Tham số lọc/phân trang cho <c>GET /api/payments</c> (P1). Tất cả đều tùy chọn.
/// <c>From</c>/<c>To</c> lọc theo <c>Payment.PaidAt</c> (bao gồm cả hai đầu).
/// </summary>
public sealed class PaymentListQuery
{
    /// <summary>Lọc theo <c>Payment.Status</c> (không phân biệt hoa/thường).</summary>
    public string? Status { get; set; }

    /// <summary>Lọc theo <c>Payment.PaymentMethod</c> (không phân biệt hoa/thường).</summary>
    public string? Method { get; set; }

    /// <summary>Lọc theo <c>Payment.OrderId</c>.</summary>
    public int? OrderId { get; set; }

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public sealed class CreatePaymentRequest
{
    [Range(1, int.MaxValue)] public int OrderId { get; set; }
    [Required] public string PaymentMethod { get; set; } = string.Empty;
    [MaxLength(100)] public string? TransactionCode { get; set; }
    [Range(1, int.MaxValue)] public int CreatedBy { get; set; }
}
