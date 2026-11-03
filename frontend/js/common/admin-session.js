(function () {
    const fail = (message, status = 409) => Object.assign(new Error(message), { status });
    const id = (value) => Number.isInteger(Number(value)) && Number(value) > 0 && Number(value) <= 2147483647;
    const text = (value, max, required = false) => {
        const out = String(value ?? "").trim();
        if ((required && !out) || out.length > max) throw fail(`Nội dung ${required ? "bắt buộc, " : ""}tối đa ${max} ký tự.`, 400);
        return out;
    };
    const number = (value, min = 0, max = Number.MAX_SAFE_INTEGER, integer = false) => {
        if (value === "" || value == null) throw fail("Nhập đầy đủ giá trị số.", 400);
        const out = Number(value);
        if (!Number.isFinite(out) || out < min || out > max || (integer && !Number.isInteger(out))) throw fail("Giá trị số nằm ngoài phạm vi cho phép.", 400);
        return out;
    };
    function vnDateTime(value) {
        if (!/^\d{4}-\d\d-\d\dT\d\d:\d\d(?::\d\d)?$/.test(value) || Number(value.slice(0, 4)) < 1) throw fail("Nhập thời điểm hợp lệ theo giờ Việt Nam.", 400);
        const timestamp = Date.parse(value + "+07:00");
        if (!Number.isFinite(timestamp) || new Date(timestamp + 7 * 3600000).toISOString().slice(0, 19) !== (value.length === 16 ? value + ":00" : value)) throw fail("Ngày hoặc giờ không tồn tại.", 400);
        return timestamp;
    }
    const field = (name, label, type = "text", options = {}) => ({ name, label, type, ...options });
    const definitions = {
        categories: { title: "Danh mục món", id: "id", name: "name", path: "/categories", roles: ["Admin", "WarehouseManager"], fields: [field("name", "Tên danh mục", "text", { required: true, max: 100 }), field("description", "Mô tả", "textarea", { max: 500 }), field("displayOrder", "Thứ tự hiển thị", "integer", { min: -2147483648, max: 2147483647, value: 0 })] },
        toppings: { title: "Topping", id: "toppingId", name: "toppingName", path: "/toppings", roles: ["Admin", "WarehouseManager"], fields: [field("toppingName", "Tên topping", "text", { required: true, max: 100 }), field("toppingPrice", "Giá topping (VND)", "number", { value: 0 }), field("isAvailable", "Đang bán", "checkbox", { value: true })] },
        sizes: { title: "Kích cỡ món", id: "sizeId", name: "sizeName", roles: ["Admin", "WarehouseManager"], food: true, fields: [field("sizeName", "Tên kích cỡ", "text", { required: true, max: 50, value: "Vừa" }), field("sizePrice", "Giá bán (VND)", "number", { value: 0 }), field("isDefault", "Kích cỡ mặc định", "checkbox")] },
        links: { title: "Topping của món", id: "toppingId", name: "toppingName", roles: ["Admin", "WarehouseManager"], food: true, fields: [field("toppingId", "Topping cần gán", "topping", { required: true })] },
        areas: { title: "Khu vực bàn", id: "areaId", name: "areaName", path: "/table-areas", roles: ["Admin"], fields: [field("areaName", "Tên khu vực", "text", { required: true, max: 100 }), field("description", "Mô tả", "textarea", { max: 500 }), field("displayOrder", "Thứ tự hiển thị", "integer", { min: -2147483648, max: 2147483647, value: 0 })] },
        tables: { title: "Thông tin bàn", id: "tableId", name: "tableNumber", path: "/restaurant-tables", roles: ["Admin"], editOnly: true, fields: [field("tableNumber", "Số / tên bàn", "text", { required: true, max: 20 }), field("capacity", "Số chỗ", "integer", { min: 1, max: 2147483647, value: 4 }), field("areaId", "Khu vực", "area")] },
        promotions: { title: "Khuyến mãi", id: "id", name: "name", path: "/management/promotions", roles: ["Admin"], fields: [field("name", "Tên khuyến mãi", "text", { required: true, max: 150 }), field("description", "Mô tả", "textarea", { max: 1000 }), field("discountType", "Loại giảm", "select", { options: [["Percent", "Phần trăm"], ["Fixed", "Số tiền cố định"]], value: "Percent" }), field("discountValue", "Mức giảm (% hoặc VND)", "number", { min: 0.01, value: 10 }), field("startAt", "Bắt đầu (giờ Việt Nam)", "datetime-local", { required: true }), field("endAt", "Kết thúc (giờ Việt Nam)", "datetime-local", { required: true }), field("minimumOrderAmount", "Giá trị đơn tối thiểu (VND)", "number", { value: 0 }), field("maxDiscountAmount", "Giảm tối đa (VND, 0 = không giới hạn)", "number", { value: 0 })] },
        settings: { title: "Thiết lập quán", id: "id", name: "restaurantName", path: "/management/settings", roles: ["Admin"], singleton: true, fields: [field("restaurantName", "Tên quán", "text", { max: 200 }), field("address", "Địa chỉ", "textarea", { max: 500 }), field("phone", "Điện thoại", "text", { max: 50 }), field("taxCode", "Mã số thuế", "text", { max: 50 }), field("currency", "Tiền tệ", "select", { options: ["VND"], value: "VND" }), field("taxRate", "VAT (%)", "number", { max: 100, value: 0 }), field("taxIncluded", "Giá menu đã gồm VAT", "checkbox", { value: true }), field("serviceChargeRate", "Phí dịch vụ (%)", "number", { max: 100, value: 0 })] }
    };
    // Metadata chỉ chứa kết quả/định danh để đối chiếu; tuyệt đối không lưu mật khẩu hoặc tự gửi lại lệnh.
    class WriteGuard {
        constructor({ api, auth, storage, key }) {
            this.api = api; this.auth = auth; this.storage = storage; this.key = key;
            this.busy = false; this.pending = null;
            try {
                const saved = JSON.parse(storage.getItem(key));
                if (saved?.userId === auth.getUser()?.userId && saved?.realm === api.baseUrl) this.pending = { ...saved, reviewed: false };
                else storage.removeItem(key);
            } catch (_) { storage.removeItem(key); }
        }
        save() { this.storage.setItem(this.key, JSON.stringify(this.pending)); }
        clear() { this.pending = null; this.storage.removeItem(this.key); }
        async write(description, preflight, mutate, reload) {
            if (this.busy || this.pending) throw fail("Hãy chờ hoặc đối chiếu thao tác trước khi tiếp tục.");
            const actor = this.auth.getUser()?.userId;
            if (!id(actor)) throw fail("Hãy đăng nhập lại.", 401);
            this.busy = true;
            try {
                const changed = await preflight();
                if (this.auth.getUser()?.userId !== actor) throw fail("Phiên đăng nhập đã đổi.", 401);
                if (changed === false) return { unchanged: true };
                this.pending = { userId: actor, realm: this.api.baseUrl, description, context: this.context || null, accepted: false, reviewed: false, createdAt: new Date().toISOString() };
                this.save();
                const result = await mutate();
                this.pending.accepted = true; this.save();
                await reload(result);
                this.clear();
                return result;
            } catch (error) {
                if (this.pending && !this.pending.accepted && [400, 401, 403, 404, 409, 410, 422].includes(error.status)) this.clear();
                throw error;
            } finally { this.busy = false; }
        }
        async review(reload) {
            if (this.busy || !this.pending) throw fail("Không có thao tác cần đối chiếu.");
            if (this.pending.userId !== this.auth.getUser()?.userId || this.pending.realm !== this.api.baseUrl) throw fail("Phiên đăng nhập đã đổi.", 401);
            this.busy = true;
            try { await reload(); this.pending.reviewed = true; this.save(); }
            finally { this.busy = false; }
        }
        acknowledge() {
            if (this.busy || !this.pending?.reviewed) throw fail("Tải dữ liệu mới và đối chiếu trước khi xác nhận.");
            if (this.pending.userId !== this.auth.getUser()?.userId || this.pending.realm !== this.api.baseUrl) throw fail("Phiên đăng nhập đã đổi.", 401);
            this.clear();
        }
    }
    const snapshot = (item) => JSON.stringify(item);
    class AdminSession extends WriteGuard {
        constructor(options) {
            super({ ...options, key: "restaurant.adminPending" });
            this.module = "categories"; this.foodId = null; this.items = []; this.lookups = {}; this.loaded = false; this.loading = false;
            if (Object.prototype.hasOwnProperty.call(definitions, this.pending?.context?.module)) { this.module = this.pending.context.module; this.foodId = this.pending.context.foodId; }
        }
        get definition() { return definitions[this.module]; }
        assertAccess(module = this.module) {
            if (!Object.prototype.hasOwnProperty.call(definitions, module) || !this.auth.hasRole(...definitions[module].roles)) throw fail("Bạn không có quyền quản lý mục này.", 403);
        }
        path() { return this.definition.food ? `/foods/${this.foodId}/${this.module === "links" ? "toppings" : "sizes"}` : this.definition.path; }
        async load() {
            this.assertAccess(); this.loaded = false;
            const api = this.api, module = this.module, lookups = {};
            if (["sizes", "links", "promotions"].includes(module)) lookups.foods = await api.get("/foods?includeInactive=true");
            if (this.definition.food && !id(this.foodId)) this.foodId = lookups.foods.find((f) => f.isActive)?.foodId || lookups.foods[0]?.foodId || null;
            if (module === "links") lookups.toppings = await api.get("/toppings?includeInactive=true");
            if (["tables", "promotions"].includes(module)) lookups[module === "tables" ? "areas" : "categories"] = await api.get(module === "tables" ? "/table-areas?includeInactive=true" : "/categories?includeInactive=true");
            if (module === "promotions") lookups.sizes = (await Promise.all(lookups.foods.map(async (f) => (await api.get(`/foods/${f.foodId}/sizes?includeInactive=true`)).map((s) => ({ ...s, foodName: f.foodName, foodIsActive: f.isActive }))))).flat();
            const data = this.definition.food && !id(this.foodId) ? [] : await api.get(this.path() + (this.definition.singleton ? "" : "?includeInactive=true"));
            const items = this.definition.singleton ? [data] : data;
            if (!Array.isArray(items) || items.some((x) => !x || typeof x !== "object" || (this.definition.singleton ? x.id == null : !id(x[this.definition.id])))) throw fail("Dữ liệu máy chủ chưa đầy đủ. Hãy tải lại.", 502);
            this.items = items; this.lookups = lookups; this.loaded = true;
        }
        async refresh() {
            if (this.busy || this.loading) return;
            this.loading = true; try { await this.load(); } finally { this.loading = false; }
        }
        body(data) {
            const out = {};
            for (const f of this.definition.fields) {
                const v = data[f.name];
                out[f.name] = f.type === "checkbox" ? v === true || v === "on"
                    : ["number", "integer"].includes(f.type) ? number(v, f.min ?? 0, f.max ?? Number.MAX_SAFE_INTEGER, f.type === "integer")
                    : f.type === "area" ? v === "" || v == null ? null : number(v, 1, 2147483647, true)
                    : f.type === "topping" ? number(v, 1, 2147483647, true)
                    : text(v, f.max || 500, f.required);
                if (f.type === "select" && !f.options.map((x) => Array.isArray(x) ? x[0] : x).includes(out[f.name])) throw fail("Lựa chọn không hợp lệ.", 400);
            }
            if (this.module === "promotions") {
                if (vnDateTime(out.endAt) <= vnDateTime(out.startAt)) throw fail("Kết thúc phải sau thời điểm bắt đầu.", 400);
                if (out.discountType === "Percent" && out.discountValue > 100) throw fail("Mức giảm phần trăm không vượt 100%.", 400);
            }
            return out;
        }
        async command(action, itemId, data = {}, expected = null) {
            this.assertAccess();
            if (!this.loaded || this.loading) throw fail("Chờ tải xong dữ liệu.");
            if (!["create", "update", "delete", "active", "availability", "default", "targets"].includes(action) || (action !== "create" && !id(itemId) && !this.definition.singleton)) throw fail("Thao tác không hợp lệ.", 400);
            const module = this.module, definition = this.definition, base = this.path();
            if (definition.food && !id(this.foodId)) throw fail("Chọn món ăn trước.", 400);
            if (definition.editOnly && action !== "update") throw fail("Thao tác này được thực hiện ở màn Bàn ăn.", 400);
            if (definition.singleton && action !== "update") throw fail("Chỉ được lưu cấu hình hiện tại.", 400);
            if (module === "links" && !["create", "delete"].includes(action)) throw fail("Chỉ được gán hoặc gỡ topping.", 400);
            if ((action === "availability" && module !== "toppings") || (action === "default" && module !== "sizes") || (action === "active" && ["links", "tables", "settings"].includes(module))) throw fail("Thao tác không áp dụng cho mục này.", 400);
            this.context = { module, foodId: this.foodId };
            let body = ["create", "update"].includes(action) ? this.body(data) : action === "active" ? { isActive: data.isActive === true } : action === "availability" ? { isAvailable: data.isAvailable === true } : undefined;
            if (action === "targets") {
                if (module !== "promotions") throw fail("Mục này không có phạm vi khuyến mãi.", 400);
                body = { sizeIds: [...new Set(data.sizeIds || [])].map(Number), categoryIds: [...new Set(data.categoryIds || [])].map(Number) };
                if ([...body.sizeIds, ...body.categoryIds].some((x) => !id(x))) throw fail("Mã phạm vi không hợp lệ.", 400);
            }
            let current;
            const path = definition.singleton ? base : action === "create" ? base : `${base}/${Number(itemId)}`;
            return this.write(`${definition.title}: ${action === "create" ? "thêm mới" : `${action} #${itemId}`}`, async () => {
                await this.load(); this.assertAccess(module);
                current = definition.singleton ? this.items[0] : this.items.find((x) => x[definition.id] === Number(itemId));
                if (action !== "create" && (!current || !expected || snapshot(current) !== expected)) throw fail("Dữ liệu đã đổi. Đóng biểu mẫu, tải lại và kiểm tra trước khi lưu.");
                if (definition.food) {
                    const food = this.lookups.foods.find((x) => x.foodId === Number(this.foodId));
                    if (action !== "delete" && !food?.isActive) throw fail("Món đã ngừng sử dụng.");
                }
                if (module === "links" && action === "create" && !this.lookups.toppings.some((x) => x.toppingId === body.toppingId && x.isActive)) throw fail("Topping không còn hoạt động.");
                if (module === "tables" && body.areaId && !this.lookups.areas.some((x) => x.areaId === body.areaId && (x.isActive || x.areaId === current.areaId))) throw fail("Khu vực không còn hoạt động.");
                if (module === "categories" && action === "update") body.isActive = current.isActive;
                if (module === "tables" && body.tableNumber === current.tableNumber && body.capacity === current.capacity && body.areaId === (current.areaId ?? null)) return false;
                if (action === "targets") {
                    if (body.sizeIds.some((v) => !this.lookups.sizes.some((s) => s.sizeId === v && s.isActive && s.foodIsActive)) || body.categoryIds.some((v) => !this.lookups.categories.some((c) => c.id === v && c.isActive))) throw fail("Phạm vi có món/kích cỡ/danh mục đã ngừng dùng.");
                }
            }, () => {
                if (["create", "update"].includes(action)) return this.api[action === "create" ? "post" : "put"](path, body);
                if (action === "delete") return this.api.delete(path);
                if (action === "targets") return this.api.put(`${path}/targets`, body);
                return this.api.patch(`${path}/${action}${module === "promotions" ? `?active=${data.isActive === true}` : ""}`, module === "promotions" ? undefined : body);
            }, () => this.load());
        }
    }
    window.restaurantAdmin = { WriteGuard, AdminSession, definitions, snapshot, fail, id, text, number, vnDateTime };
})();
