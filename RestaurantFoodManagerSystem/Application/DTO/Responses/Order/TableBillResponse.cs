using System.Text.Json.Serialization;

/// <summary>
/// Hoá đơn gộp theo bàn — <c>GET /api/orders/table/{tableId}/bill</c> (Đợt 3 / B2).
///
/// <para>
/// Khách ngồi 1 bàn có thể gọi món nhiều lần ⇒ mỗi lần là 1 <c>Order</c> riêng.
/// Thu ngân cần **một** con số tổng của cả bàn để chào khách và in hoá đơn.
/// Endpoint này gom mọi đơn **đang mở** của bàn thành 1 bản tổng hợp.
/// </para>
///
/// <para>
/// Tổng tiền = **tổng của từng đơn** (mỗi đơn đã chốt tiền riêng theo khuyến mãi riêng),
/// không tính lại từ đầu — nên không bao giờ lệch với số tiền thực đã ghi trên từng đơn.
/// </para>
///
/// <para>
/// <b>Phạm vi:</b> endpoint này chỉ **đọc/gộp** để hiển thị. Thanh toán vẫn làm từng đơn
/// (<c>POST /api/payments</c> nhận <c>orderId</c>); FE dùng <c>orderIds</c> để thanh toán lần lượt.
/// </para>
/// </summary>
public sealed class TableBillResponse
{
    public int TableId { get; set; }
    public string TableNumber { get; set; } = string.Empty;

    /// <summary>Các đơn đang mở được gộp vào hoá đơn này.</summary>
    public int[] OrderIds { get; set; } = [];

    /// <summary>Số đơn đang mở của bàn.</summary>
    public int OrderCount { get; set; }

    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxAmount { get; set; }

    /// <summary>Phí dịch vụ (Đợt 2 / A1).</summary>
    public decimal ServiceChargeAmount { get; set; }

    public decimal TotalAmount { get; set; }

    /// <summary>Các dòng món đã gộp (cùng món + cùng đơn giá + cùng ghi chú thì gộp lại).</summary>
    public List<TableBillLineResponse> Items { get; set; } = [];

    /// <summary>Ghi chú của các đơn được gộp (Đợt 3 / B3) — rỗng nếu không đơn nào có ghi chú.</summary>
    public List<string> Notes { get; set; } = [];

    /// <summary>Chi tiết từng đơn trong hoá đơn gộp.</summary>
    public List<TableBillOrderResponse> Orders { get; set; } = [];
}

/// <summary>Một dòng món trong hoá đơn gộp theo bàn.</summary>
public sealed class TableBillLineResponse
{
    public int SizeId { get; set; }
    public string FoodName { get; set; } = string.Empty;
    public string SizeName { get; set; } = string.Empty;

    // C# dùng Quanlity theo quy ước project/DB; JSON vẫn là "quantity" cho frontend.
    [JsonPropertyName("quantity")]
    public int Quanlity { get; set; }

    /// <summary>Đơn giá đã gồm topping, đúng như giá chốt trên đơn.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Thành tiền = đơn giá × số lượng.</summary>
    public decimal LineTotal { get; set; }

    /// <summary>Ghi chú của dòng món (bỏ hành, ít đá…).</summary>
    public string? Note { get; set; }
}

/// <summary>Tóm tắt 1 đơn trong hoá đơn gộp theo bàn.</summary>
public sealed class TableBillOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public int? PromotionId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
