/*
    Dọn dữ liệu tạo bởi smoke test E2E (mọi bản ghi liên quan tới tên có "E2E").

    AN TOÀN: chỉ xoá dữ liệu mà script E2E tạo ra. Dữ liệu thật của quán được giữ nguyên.
    Nhận diện theo chuỗi "E2E" trong TÊN danh mục / món / nguyên liệu / khuyến mãi /
    nhà cung cấp / khu vực / số bàn, rồi lan sang cỡ, đơn, dòng món, công thức, phiếu mua.

    CÁCH DÙNG:
        sqlcmd -S HAI2\SQLEXPRESS -d RestaurantFoodManager -E -i scripts\cleanup-e2e.sql
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;
-- Bắt buộc: các bảng có filtered index (unique) cần QUOTED_IDENTIFIER ON.
-- sqlcmd mặc định để OFF ⇒ lệnh DELETE sẽ bị chặn.
SET QUOTED_IDENTIFIER ON;

BEGIN TRANSACTION;

DECLARE @cats TABLE (Id INT);
DECLARE @foods TABLE (Id INT);
DECLARE @sizes TABLE (Id INT);
DECLARE @ings TABLE (Id INT);
DECLARE @promos TABLE (Id INT);
DECLARE @tables TABLE (Id INT);
DECLARE @orders TABLE (Id INT);
DECLARE @items TABLE (Id INT);
DECLARE @po TABLE (Id INT);
DECLARE @areas TABLE (Id INT);

-- 1) Danh mục / khu vực / bàn / nguyên liệu / khuyến mãi / nhà cung cấp (theo tên)
INSERT INTO @cats   SELECT CategoryId   FROM categories         WHERE CategoryName LIKE '%E2E%';
INSERT INTO @areas  SELECT AreaId       FROM table_areas        WHERE AreaName     LIKE '%E2E%';
INSERT INTO @tables SELECT TableId      FROM restaurant_tables  WHERE TableNumber  LIKE 'E2E-%';
INSERT INTO @ings   SELECT IngredientId FROM ingredients        WHERE Name          LIKE '%E2E%';
INSERT INTO @promos SELECT Id           FROM promotions         WHERE Name          LIKE '%E2E%';

-- 2) Món: theo tên E2E, hoặc thuộc danh mục E2E
INSERT INTO @foods SELECT f.FoodId FROM foods f
    WHERE f.FoodName LIKE '%E2E%'
       OR f.CategoryId IN (SELECT Id FROM @cats);

-- 3) Cỡ: thuộc món E2E  (tên cỡ chỉ là "L"/"M" nên phải tra qua món)
INSERT INTO @sizes SELECT s.SizeId FROM food_sizes s WHERE s.FoodId IN (SELECT Id FROM @foods);

-- 4) Phiếu mua: gắn nguyên liệu E2E hoặc nhà cung cấp E2E
INSERT INTO @po SELECT p.Id FROM purchase_orders p
    WHERE EXISTS (SELECT 1 FROM purchase_order_items i JOIN @ings e ON e.Id = i.IngredientId
                   WHERE i.PurchaseOrderId = p.Id)
       OR EXISTS (SELECT 1 FROM suppliers su WHERE su.SupplierId = p.SupplierId AND su.Name LIKE '%E2E%');

-- 5) Đơn: thuộc bàn E2E, có ghi chú E2E, hoặc có dòng món dùng cỡ E2E
INSERT INTO @orders SELECT o.OrderId FROM orders o
    WHERE o.TableId IN (SELECT Id FROM @tables)
       OR o.Note LIKE '%E2E%'
       OR EXISTS (SELECT 1 FROM order_items oi WHERE oi.OrderId = o.OrderId AND oi.SizeId IN (SELECT Id FROM @sizes));

-- 6) Dòng món: thuộc đơn E2E hoặc dùng cỡ E2E
INSERT INTO @items SELECT oi.Id FROM order_items oi
    WHERE oi.OrderId IN (SELECT Id FROM @orders)
       OR oi.SizeId IN (SELECT Id FROM @sizes);

-- ===== Xoá theo thứ tự phụ thuộc (FK Restrict buộc phải đúng thứ tự) =====
DELETE FROM order_item_toppings          WHERE OrderItemId IN (SELECT Id FROM @items);
DELETE FROM order_item_status_histories  WHERE OrderItemId IN (SELECT Id FROM @items)
                                            OR OrderId IN (SELECT Id FROM @orders);
-- inventory_transactions không có cột OrderId: tham chiếu đơn nằm ở ReferenceType/ReferenceId.
DELETE FROM inventory_transactions       WHERE OrderItemId IN (SELECT Id FROM @items)
                                            OR (ReferenceType = 'Order' AND ReferenceId IN (SELECT Id FROM @orders))
                                            OR IngredientId IN (SELECT Id FROM @ings);
DELETE FROM kitchen_order_items          WHERE OrderItemId IN (SELECT Id FROM @items)
                                            OR KitchenOrderId IN (SELECT KitchenOrderId FROM kitchen_orders WHERE OrderId IN (SELECT Id FROM @orders));
DELETE FROM kitchen_orders               WHERE OrderId IN (SELECT Id FROM @orders);
DELETE FROM payments                     WHERE OrderId IN (SELECT Id FROM @orders);
DELETE FROM order_status_histories       WHERE OrderId IN (SELECT Id FROM @orders);
DELETE FROM order_items                  WHERE Id IN (SELECT Id FROM @items);
DELETE FROM orders                        WHERE OrderId IN (SELECT Id FROM @orders);

DELETE FROM promotion_foods              WHERE PromotionId IN (SELECT Id FROM @promos)
                                            OR SizeId IN (SELECT Id FROM @sizes);
DELETE FROM promotion_categories         WHERE PromotionId IN (SELECT Id FROM @promos)
                                            OR CategoryId IN (SELECT Id FROM @cats);
DELETE FROM recipe_ingredients           WHERE RecipeId IN (SELECT RecipeId FROM recipes WHERE SizeId IN (SELECT Id FROM @sizes))
                                            OR IngredientId IN (SELECT Id FROM @ings);
DELETE FROM recipes                      WHERE SizeId IN (SELECT Id FROM @sizes);
DELETE FROM promotions                   WHERE Id IN (SELECT Id FROM @promos);

DELETE FROM food_toppings                WHERE FoodId IN (SELECT Id FROM @foods)
                                            OR ToppingId IN (SELECT ToppingId FROM toppings WHERE ToppingName LIKE '%E2E%');
DELETE FROM food_sizes                   WHERE SizeId IN (SELECT Id FROM @sizes) OR FoodId IN (SELECT Id FROM @foods);
DELETE FROM foods                        WHERE FoodId IN (SELECT Id FROM @foods);
DELETE FROM toppings                    WHERE ToppingName LIKE '%E2E%';

DELETE FROM purchase_order_items         WHERE PurchaseOrderId IN (SELECT Id FROM @po)
                                            OR IngredientId IN (SELECT Id FROM @ings);
DELETE FROM purchase_orders              WHERE Id IN (SELECT Id FROM @po);
DELETE FROM suppliers                    WHERE Name LIKE '%E2E%';
DELETE FROM inventory                    WHERE IngredientId IN (SELECT Id FROM @ings);
DELETE FROM ingredients                  WHERE IngredientId IN (SELECT Id FROM @ings);

DELETE FROM restaurant_tables            WHERE TableId IN (SELECT Id FROM @tables);
DELETE FROM table_areas                  WHERE AreaId IN (SELECT Id FROM @areas);
DELETE FROM categories                   WHERE CategoryId IN (SELECT Id FROM @cats);

COMMIT TRANSACTION;

PRINT 'Da don du lieu E2E.';
