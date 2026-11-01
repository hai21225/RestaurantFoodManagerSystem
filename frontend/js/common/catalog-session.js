(function () {
    const pendingKey = "restaurant.catalogPending";
    const fail = (message, status = 409) => Object.assign(new Error(message), { status });
    const stockStatuses = { Available: "Bình thường", OutOfStock: "Báo hết hàng", Spoiled: "Báo hỏng" };
    const kinds = ["createIngredient", "updateIngredient", "ingredientActive", "deleteIngredient", "ingredientStatus", "createRecipe", "updateRecipe", "deleteRecipe", "recipeActive"];
    const ingredientSnapshot = (item) => JSON.stringify(item && [item.ingredientId, item.name, item.unit, item.minimumStock, item.isActive, item.stockStatus]);
    const recipeSnapshot = (item) => JSON.stringify(item && [item.recipeId, item.sizeId, item.name, item.version, item.isActive,
        [...item.ingredients].sort((a, b) => a.ingredientId - b.ingredientId).map((i) => [i.ingredientId, i.quantity, i.unit])]);
    function decimal(value, min = 0) {
        if (value === null || value === undefined || String(value).trim() === "") return false;
        const scaled = Number(value) * 1000;
        return Number.isFinite(scaled) && Number(value) >= min && scaled <= Number.MAX_SAFE_INTEGER && Math.abs(scaled - Math.round(scaled)) < 0.00001;
    }
    const validId = (value) => Number.isInteger(Number(value)) && Number(value) >= 1 && Number(value) <= 2147483647;
    function ingredientBody(data) {
        const name = String(data.name || "").trim(), unit = String(data.unit || "").trim();
        if (!name || name.length > 150 || !unit || unit.length > 50) throw fail("Tên nguyên liệu từ 1–150 ký tự; đơn vị từ 1–50 ký tự.", 400);
        if (!decimal(data.minimumStock)) throw fail("Mức tồn tối thiểu không âm, tối đa 3 số lẻ và nằm trong giới hạn số chính xác.", 400);
        return { name, unit, minimumStock: Number(data.minimumStock) };
    }
    class CatalogSession {
        constructor({ api, auth, storage }) {
            this.api = api; this.auth = auth; this.storage = storage;
            this.ingredients = []; this.recipes = []; this.foods = []; this.sizes = []; this.inventory = [];
            this.detail = null; this.detailVersion = 0; this.detailLoading = false;
            this.busy = false; this.loading = false; this.loaded = false; this.pending = null;
            try {
                const saved = JSON.parse(storage.getItem(pendingKey));
                if (saved?.userId === auth.getUser()?.userId && kinds.includes(saved.kind)) this.pending = { ...saved, reviewed: false };
                else if (saved) storage.removeItem(pendingKey);
            } catch (_) { storage.removeItem(pendingKey); }
        }
        hasAccess() { return this.auth.hasRole("WarehouseManager", "Admin"); }
        assertAccess() { if (!this.hasAccess()) throw fail("Chỉ Nhân viên kho hoặc Quản trị viên được quản lý nguyên liệu và công thức.", 403); }
        savePending() { this.storage.setItem(pendingKey, JSON.stringify(this.pending)); }
        clearPending() { this.pending = null; this.storage.removeItem(pendingKey); }
        activeRecipe(sizeId) { return this.recipes.find((r) => r.sizeId === sizeId && r.isActive); }
        async load() {
            this.assertAccess(); this.loaded = false;
            const [ingredients, recipes, foods, inventory] = await Promise.all([
                this.api.get("/management/ingredients"), this.api.get("/management/recipes?includeInactive=true"),
                this.api.get("/foods?includeInactive=true"), this.api.get("/management/inventory")
            ]);
            if (![ingredients, recipes, foods, inventory].every(Array.isArray)) throw fail("Không đọc được đầy đủ danh mục nguyên liệu/công thức.", 502);
            const byFood = await Promise.all(foods.map((food) => this.api.get(`/foods/${food.foodId}/sizes?includeInactive=true`)));
            if (!byFood.every(Array.isArray)) throw fail("Không đọc được danh sách kích cỡ món.", 502);
            this.ingredients = ingredients; this.recipes = recipes; this.foods = foods; this.inventory = inventory;
            // Giữ mọi tên kích cỡ từ API, không giới hạn vào S/M/L và không bỏ lịch sử món đã ngừng.
            this.sizes = foods.flatMap((food, index) => byFood[index].map((size) => ({ ...size, foodId: food.foodId, foodName: food.foodName, foodIsActive: food.isActive })));
            this.loaded = true;
        }
        async refresh() {
            this.assertAccess(); if (this.busy || this.loading) return;
            this.loading = true;
            try { await this.load(); } finally { this.loading = false; }
        }
        async recipeDetails(id) {
            this.assertAccess(); if (!validId(id)) throw fail("Mã công thức không hợp lệ.", 400);
            const data = await this.api.get(`/management/recipes/${id}`);
            if (!data || data.recipeId !== Number(id) || !Array.isArray(data.ingredients)) throw fail("Chi tiết công thức chưa đầy đủ.", 502);
            return data;
        }
        async selectRecipe(id) {
            if (this.busy || this.loading) return null;
            const version = ++this.detailVersion; this.detailLoading = true; this.detail = null;
            try {
                const detail = await this.recipeDetails(id);
                if (version !== this.detailVersion) return null;
                this.detail = detail; return detail;
            } catch (error) { if (version === this.detailVersion) throw error; return null; }
            finally { if (version === this.detailVersion) this.detailLoading = false; }
        }
        async ingredientDetails(id) {
            this.assertAccess(); if (!validId(id)) throw fail("Mã nguyên liệu không hợp lệ.", 400);
            const item = await this.api.get(`/management/ingredients/${id}`);
            if (!item || item.ingredientId !== Number(id)) throw fail("Không đọc được nguyên liệu.", 502);
            return item;
        }
        async ingredientUsage(id) {
            this.assertAccess();
            if (!validId(id)) throw fail("Mã nguyên liệu không hợp lệ.", 400);
            // API chưa có cờ unitLocked; chỉ mở ô đơn vị khi đã kiểm chứng đủ ba nguồn tham chiếu.
            const ledger = await this.api.get(`/management/inventory-transactions?ingredientId=${id}&page=1&pageSize=1`);
            if (!Number.isInteger(ledger?.total) || ledger.total < 0) throw fail("Chưa kiểm tra được lịch sử sử dụng đơn vị.", 502);
            if (ledger.total > 0) return { locked: true, reason: "Đã có biến động kho." };
            const recipes = await this.api.get("/management/recipes?includeInactive=true");
            if (!Array.isArray(recipes)) throw fail("Chưa kiểm tra được lịch sử công thức.", 502);
            const details = await Promise.all(recipes.map((r) => this.recipeDetails(r.recipeId)));
            if (details.some((r) => r.ingredients.some((i) => i.ingredientId === Number(id)))) return { locked: true, reason: "Đã dùng trong công thức, kể cả phiên bản cũ." };
            const purchases = await this.api.get("/management/purchase-orders");
            if (!Array.isArray(purchases)) throw fail("Chưa kiểm tra được phiếu nhập.", 502);
            const purchaseDetails = await Promise.all(purchases.map((p) => this.api.get(`/management/purchase-orders/${p.id}`)));
            if (!purchaseDetails.every((p) => Array.isArray(p.items))) throw fail("Chưa đọc đủ dòng hàng của phiếu nhập.", 502);
            if (purchaseDetails.some((p) => p.items.some((i) => i.ingredientId === Number(id)))) return { locked: true, reason: "Đã có trong phiếu nhập, kể cả phiếu nháp/hủy." };
            return { locked: false, reason: "Chưa có công thức, biến động kho hoặc phiếu nhập tham chiếu." };
        }
        recipeBody(data) {
            const sizeId = Number(data.sizeId), name = String(data.name || "").trim(), version = String(data.version || "").trim();
            if (!this.sizes.some((s) => s.sizeId === sizeId && s.isActive)) throw fail("Chọn kích cỡ đang hoạt động.", 400);
            if (!name || name.length > 150 || version.length > 50) throw fail("Tên công thức từ 1–150 ký tự; phiên bản tối đa 50 ký tự.", 400);
            if (!Array.isArray(data.ingredients) || !data.ingredients.length) throw fail("Thêm ít nhất một nguyên liệu.", 400);
            const ingredients = data.ingredients.map((i) => {
                if (!validId(i.ingredientId) || !this.ingredients.some((x) => x.ingredientId === Number(i.ingredientId) && x.isActive)) throw fail("Chọn nguyên liệu đang hoạt động; thay hoặc khôi phục nguyên liệu đã ngừng dùng.", 400);
                if (!decimal(i.quantity, 0.001)) throw fail("Định lượng ít nhất 0,001 và tối đa 3 số lẻ.", 400);
                return { ingredientId: Number(i.ingredientId), quantity: Number(i.quantity) };
            });
            if (new Set(ingredients.map((i) => i.ingredientId)).size !== ingredients.length) throw fail("Mỗi nguyên liệu chỉ xuất hiện một lần trong công thức.", 400);
            return { sizeId, name, version, ingredients };
        }
        async command(kind, id = null, data = {}, expected = null) {
            this.assertAccess();
            if (this.busy || this.loading || this.detailLoading || this.pending || !this.loaded) throw fail("Chờ tải dữ liệu hoặc đối chiếu thao tác trước khi tiếp tục.");
            if (!kinds.includes(kind)) throw fail("Thao tác danh mục không hợp lệ.", 400);
            if (kind.startsWith("create") && id !== null) throw fail("Tạo mới không nhận mã bản ghi có sẵn.", 400);
            if (!kind.startsWith("create") && !validId(id)) throw fail("Mã dữ liệu không hợp lệ.", 400);
            if (id !== null) id = Number(id);
            this.busy = true;
            try {
                await this.load();
                let path, method = "post", body, current;
                if (kind.endsWith("Ingredient") || kind.startsWith("ingredient")) {
                    if (id) {
                        current = await this.ingredientDetails(id);
                        if (expected && expected !== ingredientSnapshot(current)) throw fail("Nguyên liệu vừa thay đổi. Mở lại để kiểm tra trước khi lưu.");
                    }
                    path = id ? `/management/ingredients/${id}` : "/management/ingredients";
                    if (["createIngredient", "updateIngredient"].includes(kind)) {
                        body = ingredientBody(data); method = id ? "put" : "post";
                        if (current && body.unit.toLocaleLowerCase() !== current.unit.toLocaleLowerCase()) {
                            const usage = await this.ingredientUsage(id);
                            if (usage.locked) throw fail(`Nguyên liệu đã dùng nên không đổi được đơn vị. ${usage.reason} Hãy tạo nguyên liệu mới.`);
                        }
                    } else if (kind === "ingredientActive") {
                        if (typeof data.isActive !== "boolean") throw fail("Trạng thái hoạt động không hợp lệ.", 400);
                        path += "/active"; method = "patch"; body = { isActive: data.isActive };
                    } else if (kind === "ingredientStatus") {
                        if (!Object.hasOwn(stockStatuses, data.stockStatus)) throw fail("Trạng thái kho không hợp lệ.", 400);
                        path += "/status"; method = "patch"; body = { stockStatus: data.stockStatus };
                    } else method = "delete";
                } else {
                    if (id) {
                        current = await this.recipeDetails(id);
                        if (expected && expected !== recipeSnapshot(current)) throw fail("Công thức vừa thay đổi. Mở lại phiên bản hiện tại trước khi thao tác.");
                    }
                    path = id ? `/management/recipes/${id}` : "/management/recipes";
                    if (["createRecipe", "updateRecipe"].includes(kind)) {
                        if (current && !current.isActive) throw fail("Phiên bản này đã ngừng dùng. Hãy sửa bản đang hoạt động.");
                        body = this.recipeBody(data); method = id ? "put" : "post";
                        if (current && body.sizeId !== current.sizeId) throw fail("Không chuyển công thức sang kích cỡ khác trong form sửa.", 400);
                        if (this.recipes.some((r) => r.sizeId === body.sizeId && r.isActive && r.recipeId !== id)) throw fail("Kích cỡ này đã có công thức đang hoạt động.");
                    } else if (kind === "recipeActive") {
                        if (typeof data.isActive !== "boolean") throw fail("Trạng thái công thức không hợp lệ.", 400);
                        if (data.isActive) {
                            if (this.recipes.some((r) => r.sizeId === current.sizeId && r.isActive && r.recipeId !== id)) throw fail("Ngừng công thức đang hoạt động trước khi khôi phục phiên bản này.");
                            this.recipeBody(current);
                        }
                        path += "/active"; method = "patch"; body = { isActive: data.isActive };
                    } else method = "delete";
                }
                // Tạo/sửa công thức có thể sinh bản mới. Không tự retry khi chưa biết server đã ghi hay chưa.
                this.pending = { kind, id, body, userId: this.auth.getUser().userId, accepted: false, reviewed: false };
                try { this.savePending(); }
                catch (_) { this.pending = null; throw fail("Không lưu được yêu cầu vào trình duyệt. Kiểm tra bộ nhớ trước khi thử lại.", 400); }
                const result = method === "delete" ? await this.api.delete(path) : await this.api[method](path, body);
                this.pending.accepted = true;
                if (kind === "createIngredient") this.pending.id = result?.ingredientId;
                if (kind === "createRecipe") this.pending.id = result?.recipeId;
                this.savePending();
                await this.load(); this.detail = null; ++this.detailVersion; this.clearPending(); return result;
            } catch (error) {
                if (this.pending && !this.pending.accepted && error.status >= 400 && error.status < 500) this.clearPending();
                this.loaded = false; throw error;
            } finally { this.busy = false; }
        }
        async reviewPending() {
            this.assertAccess(); if (!this.pending || this.busy || this.loading) return;
            if (this.pending.userId !== this.auth.getUser()?.userId) throw fail("Yêu cầu thuộc phiên đăng nhập khác.", 403);
            this.busy = true;
            try {
                await this.load();
                if (this.pending.accepted) { this.clearPending(); return true; }
                this.pending.reviewed = true; this.savePending(); return false;
            } finally { this.busy = false; }
        }
        acknowledgeReview() {
            if (!this.loaded || !this.pending?.reviewed || this.busy || this.loading) throw fail("Tải dữ liệu mới và đối chiếu trước khi tiếp tục.");
            this.clearPending();
        }
    }
    window.restaurantCatalog = { CatalogSession, ingredientSnapshot, recipeSnapshot, ingredientBody, stockStatuses, decimal, pendingKey };
})();
