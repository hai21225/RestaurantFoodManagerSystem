(function () {
    const pendingKey = "restaurant.warehousePending";
    const fail = (message, status = 409) => Object.assign(new Error(message), { status });
    const stockSnapshot = (stock, ingredient) => JSON.stringify([stock?.ingredientId, stock?.quanlity, stock?.updatedAt, ingredient?.unit]);
    const supplierSnapshot = (supplier) => JSON.stringify(supplier && [supplier.supplierId, supplier.name, supplier.phone, supplier.email, supplier.address, supplier.isActive]);
    const purchaseSnapshot = (purchase) => JSON.stringify(purchase && [purchase.id, purchase.supplierId, purchase.status, purchase.totalAmount, purchase.items]);
    function decimal(value, scale, min = 0, max = Number.MAX_SAFE_INTEGER / (10 ** scale)) {
        if (value === "" || value === null || value === undefined) return false;
        const number = Number(value), scaled = number * (10 ** scale);
        return Number.isFinite(number) && number >= min && number <= max && Math.abs(scaled - Math.round(scaled)) < 0.00001;
    }
    function dateBound(value, end = false) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) throw fail("Ngày lọc không hợp lệ.", 400);
        const date = new Date(`${value}T${end ? "23:59:59.999" : "00:00:00.000"}+07:00`);
        if (!Number.isFinite(date.getTime()) || new Date(date.getTime() + 25200000).toISOString().slice(0, 10) !== value) throw fail("Ngày lọc không hợp lệ.", 400);
        return date.toISOString();
    }
    class WarehouseSession {
        constructor({ api, auth, storage, mode }) {
            this.api = api; this.auth = auth; this.storage = storage; this.mode = mode;
            this.inventory = []; this.ingredients = []; this.lowStock = []; this.suppliers = []; this.purchases = [];
            this.purchase = null; this.selectedPurchaseId = null;
            this.ledger = { items: [], total: 0, page: 1, pageSize: 20 }; this.ledgerFilters = {};
            this.busy = false; this.loading = false; this.loaded = false; this.pending = null;
            try {
                const saved = JSON.parse(storage.getItem(pendingKey));
                if (saved?.userId === auth.getUser()?.userId && typeof saved.kind === "string") this.pending = { ...saved, reviewed: false };
            } catch (_) { storage.removeItem(pendingKey); }
        }
        hasAccess() { return this.auth.hasRole("WarehouseManager", "Admin"); }
        assertAccess() { if (!this.hasAccess()) throw fail("Chỉ Nhân viên kho hoặc Quản trị viên được truy cập nghiệp vụ kho.", 403); }
        savePending() { this.storage.setItem(pendingKey, JSON.stringify(this.pending)); }
        clearPending() { this.pending = null; this.storage.removeItem(pendingKey); }
        async loadInventory() {
            this.assertAccess();
            const [inventory, ingredients, low] = await Promise.all([this.api.get("/management/inventory"), this.api.get("/management/ingredients"), this.api.get("/management/inventory/low-stock")]);
            if (![inventory, ingredients, low].every(Array.isArray)) throw fail("Không đọc được đầy đủ dữ liệu kho.", 502);
            this.inventory = inventory; this.ingredients = ingredients; this.lowStock = low;
        }
        async loadPurchases() {
            this.assertAccess();
            const [purchases, suppliers, ingredients] = await Promise.all([this.api.get("/management/purchase-orders"), this.api.get("/management/suppliers"), this.api.get("/management/ingredients")]);
            if (![purchases, suppliers, ingredients].every(Array.isArray)) throw fail("Không đọc được đầy đủ phiếu nhập/nhà cung cấp/nguyên liệu.", 502);
            this.purchases = purchases; this.suppliers = suppliers; this.ingredients = ingredients;
        }
        async loadPurchase(id) {
            this.assertAccess();
            const purchase = await this.api.get(`/management/purchase-orders/${id}`);
            if (!Array.isArray(purchase.items)) throw fail("Không đọc được đầy đủ dòng hàng của phiếu.", 502);
            this.purchase = purchase; this.selectedPurchaseId = id;
            return purchase;
        }
        async selectPurchase(id) {
            if (this.busy || this.loading) return;
            this.loading = true; this.purchase = null;
            try { return await this.loadPurchase(id); } finally { this.loading = false; }
        }
        async loadLedger(filters = this.ledgerFilters, page = this.ledger.page) {
            this.assertAccess();
            const query = new URLSearchParams({ page: String(page), pageSize: "20" });
            for (const key of ["ingredientId", "referenceId"]) if (filters[key] && (!/^\d+$/.test(String(filters[key])) || Number(filters[key]) < 1)) throw fail("Mã nguyên liệu/chứng từ phải là số nguyên dương.", 400);
            if (filters.referenceId && !["Order", "PurchaseOrder"].includes(filters.referenceType)) throw fail("Chọn nguồn Đơn hàng hoặc Phiếu nhập trước khi lọc mã chứng từ.", 400);
            if (filters.from && filters.to && filters.from > filters.to) throw fail("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.", 400);
            for (const key of ["ingredientId", "type", "referenceType", "referenceId"]) if (filters[key]) query.set(key, filters[key]);
            if (filters.from) query.set("from", dateBound(filters.from));
            if (filters.to) query.set("to", dateBound(filters.to, true));
            const data = await this.api.get(`/management/inventory-transactions?${query}`);
            if (!Array.isArray(data.items)) throw fail("Không đọc được lịch sử kho.", 502);
            this.ledger = data; this.ledgerFilters = { ...filters };
        }
        async load() {
            this.loaded = false;
            if (this.mode === "inventory") await Promise.all([this.loadInventory(), this.loadLedger()]);
            else await Promise.all([this.loadPurchases(), this.selectedPurchaseId ? this.loadPurchase(this.selectedPurchaseId) : Promise.resolve()]);
            this.loaded = true;
        }
        async refresh() {
            this.assertAccess(); if (this.busy || this.loading) return;
            this.loading = true;
            try { await this.load(); } finally { this.loading = false; }
        }
        async command(kind, id = null, data = {}, expected = null) {
            this.assertAccess();
            if (this.busy || this.loading || this.pending) throw fail("Hãy chờ hoặc đối chiếu thao tác trước khi tiếp tục.");
            this.busy = true;
            try {
                let path, method = "post", body;
                const userId = this.auth.getUser().userId;
                if (kind === "adjust") {
                    await this.loadInventory();
                    const stock = this.inventory.find((s) => s.ingredientId === id), ingredient = this.ingredients.find((i) => i.ingredientId === id);
                    if (!stock || !ingredient) throw fail("Nguyên liệu chưa có dữ liệu tồn kho.", 404);
                    if (expected && expected !== stockSnapshot(stock, ingredient)) throw fail("Tồn kho hoặc đơn vị vừa đổi. Hãy kiểm tra lại trước khi điều chỉnh.");
                    if (!decimal(data.quantity, 3, -1000000, 1000000) || Number(data.quantity) === 0) throw fail("Lượng điều chỉnh phải khác 0, tối đa 3 số lẻ, trong khoảng -1.000.000 đến 1.000.000.", 400);
                    const note = String(data.note || "").trim();
                    if (!note || note.length > 500) throw fail("Nhập lý do điều chỉnh từ 1 đến 500 ký tự.", 400);
                    if (stock.quanlity + Number(data.quantity) < 0) throw fail("Lượng giảm vượt tồn kho hiện tại.", 400);
                    path = `/management/inventory/${id}/adjust`; body = { quantity: Number(data.quantity), createdBy: userId, note };
                } else if (kind === "createPurchase") {
                    await this.loadPurchases();
                    const supplierId = Number(data.supplierId);
                    if (!this.suppliers.some((s) => s.supplierId === supplierId && s.isActive)) throw fail("Chọn nhà cung cấp đang hoạt động.", 400);
                    if (!Array.isArray(data.items) || !data.items.length) throw fail("Thêm ít nhất một dòng nguyên liệu.", 400);
                    const items = data.items.map((i) => ({ ingredientId: Number(i.ingredientId), quantity: Number(i.quantity), unitPrice: Number(i.unitPrice) }));
                    if (new Set(items.map((i) => i.ingredientId)).size !== items.length) throw fail("Mỗi nguyên liệu chỉ được thêm một lần trong phiếu.", 400);
                    for (let index = 0; index < items.length; index++) {
                        const line = items[index], raw = data.items[index];
                        if (!this.ingredients.some((i) => i.ingredientId === line.ingredientId && i.isActive)) throw fail("Chọn nguyên liệu đang hoạt động.", 400);
                        if (!decimal(raw.quantity, 3, 0.001) || !decimal(raw.unitPrice, 2)) throw fail("Số lượng tối thiểu 0,001, tối đa 3 số lẻ; đơn giá không âm, tối đa 2 số lẻ.", 400);
                    }
                    path = "/management/purchase-orders"; body = { supplierId, createdBy: userId, items };
                } else if (["receive", "cancelPurchase"].includes(kind)) {
                    const purchase = await this.loadPurchase(id);
                    if (expected && expected !== purchaseSnapshot(purchase)) throw fail("Phiếu vừa thay đổi. Hãy xem lại trước khi xử lý.");
                    if (purchase.status !== "Draft") throw fail("Chỉ xử lý phiếu đang ở trạng thái Nháp.");
                    path = `/management/purchase-orders/${id}/${kind === "receive" ? "receive" : "cancel"}`;
                    body = kind === "receive" ? { receivedBy: userId } : {};
                } else if (["createSupplier", "updateSupplier", "supplierActive", "deleteSupplier"].includes(kind)) {
                    if (kind !== "createSupplier") {
                        const supplier = await this.api.get(`/management/suppliers/${id}`);
                        if (expected && expected !== supplierSnapshot(supplier)) throw fail("Nhà cung cấp vừa thay đổi. Hãy mở lại thông tin mới nhất.");
                    }
                    if (["createSupplier", "updateSupplier"].includes(kind)) {
                        body = {};
                        for (const [key, max, label] of [["name", 150, "Tên nhà cung cấp"], ["phone", 20, "Số điện thoại"], ["email", 150, "Email"], ["address", 500, "Địa chỉ"]]) {
                            body[key] = String(data[key] || "").trim();
                            if (!body[key] || body[key].length > max) throw fail(`${label} không được trống và tối đa ${max} ký tự.`, 400);
                        }
                        if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(body.email)) throw fail("Email nhà cung cấp không hợp lệ.", 400);
                        path = kind === "createSupplier" ? "/management/suppliers" : `/management/suppliers/${id}`;
                        method = kind === "createSupplier" ? "post" : "put";
                    } else if (kind === "supplierActive") {
                        if (typeof data.isActive !== "boolean") throw fail("Trạng thái nhà cung cấp không hợp lệ.", 400);
                        path = `/management/suppliers/${id}/active`; method = "patch"; body = { isActive: data.isActive };
                    } else { path = `/management/suppliers/${id}`; method = "delete"; }
                } else throw fail("Thao tác kho không hợp lệ.", 400);

                // Không có idempotency cho các lệnh kho. Lưu yêu cầu trước khi gửi, tuyệt đối không tự retry.
                this.pending = { kind, id, userId, body, accepted: false, reviewed: false }; this.savePending();
                const result = method === "delete" ? await this.api.delete(path) : await this.api[method](path, body);
                this.pending.accepted = true; this.pending.result = result;
                if (kind === "createPurchase") { this.pending.id = result.id; this.selectedPurchaseId = result.id; }
                if (kind === "createSupplier") this.pending.id = result.supplierId;
                this.savePending();
                await this.load(); this.clearPending(); return result;
            } catch (error) {
                if (this.pending && !this.pending.accepted && [400, 401, 403, 404, 409, 422].includes(error.status)) this.clearPending();
                if (this.pending) this.loaded = false;
                throw error;
            } finally { this.busy = false; }
        }
        async reviewPending() {
            this.assertAccess(); if (!this.pending || this.busy || this.loading) return;
            if (this.pending.userId !== this.auth.getUser()?.userId) throw fail("Yêu cầu thuộc phiên đăng nhập khác.", 403);
            if (this.mode !== (this.pending.kind === "adjust" ? "inventory" : "purchases")) throw fail("Mở đúng màn Kho/Nhập hàng của thao tác để đối chiếu.");
            this.busy = true;
            try {
                const pending = this.pending;
                if (["receive", "cancelPurchase", "createPurchase"].includes(pending.kind) && pending.id) this.selectedPurchaseId = pending.id;
                await this.load();
                let resolved = pending.accepted;
                if (["receive", "cancelPurchase"].includes(pending.kind)) {
                    const purchase = await this.loadPurchase(pending.id);
                    resolved = resolved || purchase.status === (pending.kind === "receive" ? "Received" : "Cancelled");
                }
                if (resolved) { this.clearPending(); return true; }
                pending.reviewed = true; this.savePending(); return false;
            } finally { this.busy = false; }
        }
        acknowledgeReview() {
            if (!this.loaded || !this.pending?.reviewed || this.busy || this.loading) throw fail("Tải dữ liệu mới để đối chiếu trước khi tiếp tục.");
            this.clearPending();
        }
    }
    window.restaurantWarehouse = { WarehouseSession, stockSnapshot, supplierSnapshot, purchaseSnapshot, decimal, dateBound };
})();
