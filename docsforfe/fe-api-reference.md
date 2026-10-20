# API reference cho FRONTEND (115 action backend)

**Cập nhật:** 2026-10-21 · **Nguồn:** `RestaurantFoodManagerSystem/Controllers/**` (đã quét code)
**Ký hiệu:** ✅ = FE đã gọi · ⬜ = FE **chưa** gọi (xem `fe-work-plan.md`) · 🔒 = cần `Authorization: Bearer`
**Base URL:** `http://localhost:5225/api` (FE đọc `localStorage.restaurant.apiBaseUrl`)

**Tổng:** 115 action — FE hiện dùng **38** (33%), **76** chưa dùng, **1** đã bị bỏ (`PUT /orders/{id}/items` → 410).
**Policy** cần hiểu: `Authenticated` (bất kỳ đã đăng nhập) · `PosOperator` (Admin/Waiter) · `PosOrCashier` (Admin/Waiter/Cashier) · `PosKitchenRead` (Admin/Waiter/Cashier/Chef) · `KitchenDesk` (Admin/Chef) · `WarehouseDesk` (Admin/WarehouseManager) · `CashierDesk` (Admin/Cashier) · `AdminOnly` · `OrderCancellation` (Admin/Waiter/Chef).

> Lưu ý: khi `Auth:Enforce=false` (hiện tại) mọi policy đều cho qua. Khi bật `true`, FE **bắt buộc** gửi Bearer (Phase F0).

---

## 1. Xác thực & khởi tạo (5)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| POST | `/auth/login` | AllowAnonymous | ✅ | Trả token; **FE phải lưu token** |
|⬜| POST | `/auth/change-password` | Authenticated | ⬜ | Cần mật khẩu hiện tại + xác nhận |
|⬜| POST | `/auth/register` | AdminOnly | ⬜ | Dùng `POST /users` thay cho |
|✅| GET | `/setup/status` | AllowAnonymous | ✅ | Có user chưa → cần setup |
|✅| POST | `/setup/first-admin` | AllowAnonymous | ✅ | Chỉ khi bảng user còn rỗng |

## 2. Nhân viên & vai trò (11)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|⬜| GET | `/users/me` | Authenticated | ⬜ | Hồ sơ cá nhân |
|⬜| PUT | `/users/me` | Authenticated | ⬜ | Chỉ `fullName/email/phone` |
|✅| GET | `/users` | AdminOnly | ✅ | Có phân trang |
|✅| POST | `/users` | AdminOnly | ✅ | Body có `roleName` để gán role ngay |
|⬜| GET | `/users/{id}` | AdminOnly | ⬜ | |
|⬜| PUT | `/users/{id}` | AdminOnly | ⬜ | Không sửa `userName`/roles/isActive |
|⬜| PATCH | `/users/{id}/active` | AdminOnly | ⬜ | Khoá / mở khoá tài khoản |
|⬜| POST | `/users/{id}/reset-password` | AdminOnly | ⬜ | Admin đặt lại mật khẩu |
|⬜| POST | `/users/{userId}/roles` | AdminOnly | ⬜ | Gán role (trùng → 409) |
|⬜| DELETE | `/users/{userId}/roles/{roleName}` | AdminOnly | ⬜ | Không gỡ được Admin cuối |
|⬜| GET | `/roles` | AdminOnly | ⬜ | 5 vai trò cố định (chỉ đọc) |

## 3. Danh mục món (30)
### 3.1 Category (6)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/categories` | Authenticated | ✅ |
|⬜| GET | `/categories/{id}` | Authenticated | ⬜ |
|✅| POST | `/categories` | WarehouseDesk | ✅ |
|⬜| PUT | `/categories/{id}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/categories/{id}/active` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/categories/{id}` | WarehouseDesk | ⬜ |

### 3.2 Food (7)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/foods` | Authenticated | ✅ (`?includeInactive=true`) |
|⬜| GET | `/foods/{id}` | Authenticated | ⬜ |
|✅| POST | `/foods` | WarehouseDesk | ✅ |
|✅| PUT | `/foods/{id}` | WarehouseDesk | ✅ |
|✅| PATCH | `/foods/{id}/availability` | WarehouseDesk | ✅ |
|✅| PATCH | `/foods/{id}/active` | WarehouseDesk | ✅ |
|✅| DELETE | `/foods/{id}` | WarehouseDesk | ✅ |

### 3.3 Food size (7)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/foods/{foodId}/sizes` | Authenticated | ✅ |
|⬜| GET | `/foods/{foodId}/sizes/{sizeId}` | Authenticated | ⬜ |
|✅| POST | `/foods/{foodId}/sizes` | WarehouseDesk | ✅ |
|⬜| PUT | `/foods/{foodId}/sizes/{sizeId}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/foods/{foodId}/sizes/{sizeId}/default` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/foods/{foodId}/sizes/{sizeId}/active` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/foods/{foodId}/sizes/{sizeId}` | WarehouseDesk | ⬜ |

### 3.4 Topping (7) + gán topping cho món (3)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|⬜| GET | `/toppings` | Authenticated | ⬜ **chưa có màn hình nào** |
|⬜| GET | `/toppings/{id}` | Authenticated | ⬜ |
|⬜| POST | `/toppings` | WarehouseDesk | ⬜ |
|⬜| PUT | `/toppings/{id}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/toppings/{id}/availability` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/toppings/{id}/active` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/toppings/{id}` | WarehouseDesk | ⬜ |
|⬜| GET | `/foods/{foodId}/toppings` | Authenticated | ⬜ |
|⬜| POST | `/foods/{foodId}/toppings` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/foods/{foodId}/toppings/{toppingId}` | WarehouseDesk | ⬜ |

### 3.5 Menu POS (1)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/menu` | Authenticated | ✅ (chỉ món `IsAvailable`) |

## 4. Bàn & khu vực (12)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| GET | `/restaurant-tables` | PosOrCashier | ✅ | Trả cả bàn đã xoá (soft) |
|✅| GET | `/restaurant-tables/{id}` | PosKitchenRead | ✅ | |
|✅| POST | `/restaurant-tables` | AdminOnly | ✅ | |
|⬜| PUT | `/restaurant-tables/{id}` | AdminOnly | ⬜ | Sửa số bàn / sức chứa / khu vực |
|✅| DELETE | `/restaurant-tables/{id}` | AdminOnly | ✅ | Soft-delete; bàn có đơn mở → 409 |
|✅| PATCH | `/restaurant-tables/{id}/maintenance` | AdminOnly | ✅ | `?enabled=true|false` |
|✅| GET | `/table-areas` | PosOrCashier | ✅ (`?includeInactive=true`) | |
|⬜| GET | `/table-areas/{id}` | PosOrCashier | ⬜ | |
|⬜| POST | `/table-areas` | AdminOnly | ⬜ | |
|⬜| PUT | `/table-areas/{id}` | AdminOnly | ⬜ | |
|⬜| PATCH | `/table-areas/{id}/active` | AdminOnly | ⬜ | |
|⬜| DELETE | `/table-areas/{id}` | AdminOnly | ⬜ | |

## 5. Đơn (10) — trọng tâm Phase F1
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|⬜| GET | `/orders` | PosOrCashier | ⬜ | Lọc `status/tableId/from/to/page/pageSize` — **chưa có màn hình nào dùng** (POS cần để mở đơn theo bàn) |
|✅| GET | `/orders/{id}` | PosKitchenRead | ✅ | Chi tiết đơn + món + topping |
|✅| POST | `/orders` | PosOperator | ✅ | Tạo đơn; hỗ trợ header `Idempotency-Key` |
|⬜| POST | `/orders/{id}/items` | PosOperator | ⬜ | **Gọi thêm** — luôn tạo vòng bếp mới |
|⬜| PATCH | `/orders/{orderId}/items/{itemId}/quantity` | PosOperator | ⬜ | Đổi SL (đã nấu 1 phần vẫn dùng được) |
|⬜| POST | `/orders/{orderId}/items/{itemId}/cancel` | OrderCancellation | ⬜ | Huỷ 1 món + lý do |
|⬜| PATCH | `/orders/{id}/promotion/{promotionId}` | PosOperator | ⬜ | Áp khuyến mãi |
|⬜| PATCH | `/orders/{id}/status` | PosOperator | ⬜ | Đổi trạng thái đơn (huỷ đơn) |
|⬜| GET | `/orders/{id}/history` | PosOrCashier | ⬜ | Lịch sử trạng thái **đơn** |
|⬜| GET | `/orders/{id}/item-history` | PosOrCashier | ⬜ | Lịch sử trạng thái **từng món** (C-10) |
|⛔| PUT | `/orders/{id}/items` | PosOperator | ❌ | **410 Gone (C-09)** — không dùng |

## 6. Bếp (7) — Phase F2
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| GET | `/management/kitchen/orders` | KitchenDesk | ✅ | Tất cả phiếu (FE tự lọc) |
|✅| GET | `/management/kitchen/orders/{id}/items` | KitchenDesk | ✅ | Món của phiếu |
|✅| PATCH | `/management/kitchen/orders/{id}/status` | KitchenDesk | ✅ | Trạng thái phiếu (kèm `reason`,`reasonCode`) |
|✅| PATCH | `/management/kitchen/items/{id}/status` | KitchenDesk | ✅ | Trạng thái món — **`Accepted` không hợp lệ** (C-11) |
|⬜| PATCH | `/management/kitchen/items/{id}/prepared-quantity` | KitchenDesk | ⬜ | Báo đã nấu được mấy phần |
|⬜| POST | `/management/kitchen/items/{id}/shortage` | KitchenDesk | ⬜ | Báo thiếu nguyên liệu (không hard-block) |
|⬜| POST | `/management/kitchen/items/{id}/reopen` | KitchenDesk | ⬜ | Mở lại món đã xong (C-06) |

## 7. Kho & nhập hàng (20)
### 7.1 Nguyên liệu (6)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/management/ingredients` | WarehouseDesk | ✅ |
|⬜| GET | `/management/ingredients/{id}` | WarehouseDesk | ⬜ |
|✅| POST | `/management/ingredients` | WarehouseDesk | ✅ |
|⬜| PUT | `/management/ingredients/{id}` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/management/ingredients/{id}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/management/ingredients/{id}/active` | WarehouseDesk | ⬜ |

### 7.2 Nhà cung cấp (6)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|⬜| GET | `/management/suppliers` | WarehouseDesk | ⬜ |
|⬜| GET | `/management/suppliers/{id}` | WarehouseDesk | ⬜ |
|⬜| POST | `/management/suppliers` | WarehouseDesk | ⬜ |
|⬜| PUT | `/management/suppliers/{id}` | WarehouseDesk | ⬜ |
|⬜| DELETE | `/management/suppliers/{id}` | WarehouseDesk | ⬜ |
|⬜| PATCH | `/management/suppliers/{id}/active` | WarehouseDesk | ⬜ |

### 7.3 Tồn kho & biến động (3)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| GET | `/management/inventory` | WarehouseDesk | ✅ | Tồn hiện tại từng nguyên liệu |
|⬜| POST | `/management/inventory/{ingredientId}/adjust` | WarehouseDesk | ⬜ | Điều chỉnh tồn (có `note`) |
|⬜| GET | `/management/inventory-transactions` | WarehouseDesk | ⬜ | Biến động kho; `referenceId` cần kèm `referenceType` |

### 7.4 Phiếu nhập (5)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| GET | `/management/purchase-orders` | WarehouseDesk | ✅ | Chỉ danh sách |
|⬜| POST | `/management/purchase-orders` | WarehouseDesk | ⬜ | Tạo phiếu nhập |
|⬜| GET | `/management/purchase-orders/{id}` | WarehouseDesk | ⬜ | Chi tiết + dòng hàng |
|⬜| POST | `/management/purchase-orders/{id}/receive` | WarehouseDesk | ⬜ | Nhập hàng (cộng kho) |
|⬜| POST | `/management/purchase-orders/{id}/cancel` | WarehouseDesk | ⬜ | Huỷ phiếu (đã nhập → 409) |

### 7.5 Công thức (6)
| | Method | Path | Policy | FE |
|---|---|---|---|---|
|✅| GET | `/management/recipes` | WarehouseDesk | ✅ |
|✅| GET | `/management/recipes/{id}` | WarehouseDesk | ✅ |
|✅| POST | `/management/recipes` | WarehouseDesk | ✅ |
|✅| PUT | `/management/recipes/{id}` | WarehouseDesk | ✅ |
|✅| DELETE | `/management/recipes/{id}` | WarehouseDesk | ✅ |
|⬜| PATCH | `/management/recipes/{id}/active` | WarehouseDesk | ⬜ |

## 8. Khuyến mãi (6)
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|⬜| GET | `/management/promotions` | AdminOnly | ⬜ | |
|⬜| GET | `/management/promotions/{id}` | AdminOnly | ⬜ | |
|⬜| POST | `/management/promotions` | AdminOnly | ⬜ | `discountType`: `Percent`\|`Fixed` |
|⬜| PUT | `/management/promotions/{id}` | AdminOnly | ⬜ | |
|⬜| PATCH | `/management/promotions/{id}/active` | AdminOnly | ⬜ | |
|⬜| DELETE | `/management/promotions/{id}` | AdminOnly | ⬜ | Soft-delete |

## 9. Thanh toán (3) — Phase F3
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|✅| POST | `/payments` | CashierDesk | ✅ | `paymentMethod`: `Cash`\|`Card`\|`Bank`\|`EWallet`\|`Transfer` |
|⬜| GET | `/payments` | CashierDesk | ⬜ | Lọc `status/method/orderId/from/to/page/pageSize` |
|⬜| GET | `/payments/order/{orderId}` | CashierDesk | ⬜ | Lịch sử thu của đơn |

## 10. Báo cáo (3) — Phase F5
| | Method | Path | Policy | FE | Ghi chú |
|---|---|---|---|---|---|
|⬜| GET | `/reports/dashboard` | AdminOnly | ⬜ | Doanh thu hôm nay, đơn mở, bàn, bếp, tồn thấp, 10 đơn gần nhất |
|⬜| GET | `/reports/revenue` | AdminOnly | ⬜ | `period=day\|week\|month`, `date=yyyy-MM-dd` |
|⬜| GET | `/reports/revenue/export.csv` | AdminOnly | ⬜ | UTF-8 BOM, cần tải bằng `blob` |

---

## Phụ lục — Endpoint BE **chưa có** mà FE sẽ cần (theo audit 2026-10-21)
| Đề xuất route | Dùng cho | Ưu tiên BE |
|---|---|---|
| `DELETE /api/orders/{id}/promotion/{promotionId}` | Gỡ khuyến mãi khỏi đơn | P0 |
| `GET/PUT /api/management/settings` | Cấu hình quán + VAT + phí dịch vụ | P0 |
| `GET /api/orders/table/{tableId}/bill` | Bill gom đơn của bàn cho thu ngân | P1 |
| `PATCH /api/orders/{id}/table` | Chuyển bàn | P1 |
| `POST /api/orders/{id}/split` | Chia / gộp bill | P1 |
| `GET /api/management/inventory/low-stock` | Danh sách tồn thấp | P1 |
| `GET/POST /api/management/shifts/*` | Ca làm việc / két tiền | P1 |
| `GET /api/reports/{revenue-by-method,top-dishes,wastage,stock-movement,purchases,by-staff}` | Báo cáo vận hành | P1 |
| `PATCH /api/orders/{id}/note` | Ghi chú mức đơn | P1 |
| `/api/payments/mo-mo/*` | QR MoMo, IPN, trạng thái, hoàn tiền | cuối cùng |
