using System.ComponentModel.DataAnnotations;

public sealed class IngredientRequest { [Required] public string Name { get; set; } = ""; [Required] public string Unit { get; set; } = ""; [Range(0, double.MaxValue)] public decimal MinimumStock { get; set; } }
public sealed class SupplierRequest { [Required] public string Name { get; set; } = ""; [Required] public string Phone { get; set; } = ""; [Required, EmailAddress] public string Email { get; set; } = ""; [Required] public string Address { get; set; } = ""; }
public sealed class RecipeRequest { [Range(1, int.MaxValue)] public int SizeId { get; set; } [Required] public string Name { get; set; } = ""; public string Version { get; set; } = "1"; [MinLength(1)] public List<RecipeIngredientRequest> Ingredients { get; set; } = []; }
public sealed class RecipeIngredientRequest { [Range(1, int.MaxValue)] public int IngredientId { get; set; } [Range(0.001, double.MaxValue)] public decimal Quantity { get; set; } }
public sealed class PurchaseOrderRequest { [Range(1, int.MaxValue)] public int SupplierId { get; set; } [Range(1, int.MaxValue)] public int CreatedBy { get; set; } public List<PurchaseOrderItemRequest> Items { get; set; } = []; }
public sealed class PurchaseOrderItemRequest { [Range(1, int.MaxValue)] public int IngredientId { get; set; } [Range(0.001, double.MaxValue)] public decimal Quantity { get; set; } [Range(0, double.MaxValue)] public decimal UnitPrice { get; set; } }
public sealed class ReceivePurchaseRequest { [Range(1, int.MaxValue)] public int ReceivedBy { get; set; } }
public sealed class PromotionRequest { [Required] public string Name { get; set; } = ""; public string Description { get; set; } = ""; [Required] public string DiscountType { get; set; } = "Percent"; [Range(0, double.MaxValue)] public decimal DiscountValue { get; set; } public DateTime StartAt { get; set; } public DateTime EndAt { get; set; } [Range(0, double.MaxValue)] public decimal MinimumOrderAmount { get; set; } [Range(0, double.MaxValue)] public decimal MaxDiscountAmount { get; set; } }
public sealed class KitchenStatusRequest { [Required] public string Status { get; set; } = ""; }
public sealed class InventoryAdjustmentRequest { [Range(-1000000, 1000000)] public decimal Quantity { get; set; } [Range(1, int.MaxValue)] public int CreatedBy { get; set; } public string? Note { get; set; } }
public sealed class PromotionLinkRequest { [Range(1, int.MaxValue)] public int Id { get; set; } }
