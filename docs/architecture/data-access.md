# Data access — sau GĐ5

Ngày: 2026-10-19 · Nhánh: `hai`

## Chuyện đã xảy ra

Trước GĐ5 tồn tại **hai phong cách truy cập dữ liệu song song**:

1. `IRepository<T>` + `IUnitOfWork` (Infrastructure/Repositories) — dùng bởi Food catalog,
   Users/Roles, RestaurantTable.
2. `AppDbContext` dùng trực tiếp — dùng bởi Order, Payment, Kitchen, Menu, Management.

GĐ5 đã hợp nhất về **một phong cách duy nhất: `AppDbContext` trực tiếp**. Đã xóa
`IRepository`, `IUnitOfWork`, `Repository<T>`, `UnitOfWork` và 2 dòng DI tương ứng.

Lý do hợp nhất (không phải vì repository "lỗi"): chúng làm phát sinh hai semantics
tracking khác nhau trong cùng một interface, dẫn tới bug thật — xem quirk GET-all bàn
bên dưới.

## Ánh xạ semantics (QUAN TRỌNG — giữ nguyên khi sửa tiếp)

Khi bỏ repository, từng method được thay bằng đúng semantics cũ. **Đừng đổi các
`AsNoTracking()` này** nếu không có chủ đích, vì hành vi hiện tại phụ thuộc vào chúng:

| Repository cũ | Tương đương `AppDbContext` | Tracking |
|---|---|---|
| `GetByIdAsync(id)` | `_db.Set<T>().FindAsync(id)` | **có track** |
| `GetAllAsync()` | `_db.Set<T>().AsNoTracking().ToListAsync()` | không track |
| `FindAsync(pred)` | `_db.Set<T>().AsNoTracking().Where(pred).ToListAsync()` | không track |
| `FindOneAsync(pred)` | `_db.Set<T>().AsNoTracking().FirstOrDefaultAsync(pred)` | không track |
| `ExistsAsync(pred)` | `_db.Set<T>().AnyAsync(pred)` | – |
| `AddAsync(e)` | `_db.Set<T>().AddAsync(e)` | – |
| `Update(e)` | `_db.Set<T>().Update(e)` | gắn detached entity ở trạng thái Modified |
| `Delete(e)` | `_db.Set<T>().Remove(e)` | hard delete |
| `SaveChangesAsync()` | `_db.SaveChangesAsync()` | – |

Ghi chú:

- **`GetByIdAsync` khác tracking với mọi method đọc còn lại.** Đây là nguồn gốc của
  quirk: `RestaurantTableService.GetRestaurantTableByIdAsync` (tracked) lưu được
  thay đổi khi tính lại `Status`, còn `GetAllRestaurantTablesAsync` (AsNoTracking)
  gọi `SaveChangesAsync()` nhưng **không lưu gì**. Cả hai đang được test pin:
  `GetById_ComputesOccupied_AndPersistsIt`, `GetAll_ComputesStatus_ButDoesNotPersistIt`.
- `Update(e)` trên entity đã detached (pattern của `FoodSizeService`, `CategoryService`…)
  đánh dấu **tất cả** cột là Modified. Giữ nguyên hành vi này.
- `Delete(e)` là hard delete. Hiện chỉ `FoodToppingService.RemoveAsync` dùng (đúng, vì
  `food_toppings` là bảng nối). Mọi chỗ khác vẫn soft delete bằng `IsActive = false`.

## Trạng thái hiện tại

| Nhóm | Service | Kiểu |
|---|---|---|
| Food catalog | `FoodService`, `CategoryService`, `FoodSizeService`, `ToppingService`, `FoodToppingService` | `AppDbContext` |
| Restaurant | `RestaurantTableService` | `AppDbContext` |
| Auth/Users | `AuthService`, `UserService`, `UserRoleService`, `RoleService` | `AppDbContext` |
| Orders/Payments | `OrderService`, `PaymentService` | `AppDbContext` (+ `RestaurantTableService`) |
| Kitchen | `KitchenService` | `AppDbContext` |
| Menu | `MenuService` | `AppDbContext` |
| Inventory group | `IngredientService`, `SupplierService`, `InventoryService`, `PurchaseOrderService`, `PromotionService`, `RecipeService` | `AppDbContext` |

> `ManagementService` (facade cũ) đã **xoá ở D2 `8fa8daa`**; không còn lớp trung gian nào.

## Việc chưa làm (cần phê duyệt riêng)

- Sửa quirk GET-all-bàn-không-ghi-DB → **thay đổi hành vi**, hiện đang được test pin.
- `Category.DisplayOrder` là `bool` → **thay đổi schema**.
- Authentication/authorization, DTO hoá response, actor lấy từ body → thay đổi hợp đồng.
- **Batch A2 (đã làm)**: `OrderService.CreateCoreAsync` nay kiểm tra “bàn có đơn mở” **bên trong** transaction `Serializable`, cùng với đọc bàn/kho — tránh hai đơn mở cùng bàn. Xem `backend-frontend-audit.md` mục 15.1/16.
