(function () {
    const terminal = (status) => ["paid", "cancelled"].includes(String(status).toLowerCase());
    const pending = (status) => String(status).toLowerCase() === "pending";
    const cancelled = (item) => String(item.status).toLowerCase() === "cancelled";
    const done = (item) => ["ready", "completed"].includes(String(item.status).toLowerCase());
    const storageKey = "restaurant.posPending";

    function groups(order) {
        const result = new Map();
        (order?.items || []).forEach((item) => {
            const key = item.orderItemGroupId || `item-${item.id}`;
            if (!result.has(key)) result.set(key, { key, items: [], active: [], quantity: 0 });
            const group = result.get(key);
            group.items.push(item);
            if (!cancelled(item)) {
                group.active.push(item);
                group.quantity += item.quantity;
            }
        });
        return [...result.values()];
    }

    class OrderSession {
        constructor({ tableId, api, auth, storage, makeKey }) {
            this.tableId = Number(tableId);
            this.api = api;
            this.auth = auth;
            this.storage = storage;
            this.makeKey = makeKey || (() => window.crypto.randomUUID());
            this.order = null;
            this.table = null;
            this.loaded = false;
            this.loading = false;
            this.busy = false;
            this.pending = null;
            try {
                const saved = JSON.parse(storage.getItem(storageKey));
                if (saved?.userId === auth.getUser()?.userId) this.pending = saved;
            } catch (_) { storage.removeItem(storageKey); }
        }

        isOperator() { return this.auth.hasRole("Admin", "Waiter"); }
        isAdmin() { return this.auth.hasRole("Admin"); }
        isOpen() { return Boolean(this.order && !terminal(this.order.status)); }
        canReduce(group) { return this.isAdmin() || group.active.every((item) => pending(item.status)); }
        canCancel(item) { return this.isOpen() && this.isOperator() && !cancelled(item) && (this.isAdmin() || pending(item.status)); }

        async findOpenOrder() {
            const found = [];
            let page = 1;
            let total;
            do {
                const result = await this.api.get(`/orders?tableId=${this.tableId}&page=${page}&pageSize=100`);
                if (!Array.isArray(result.items) || !Number.isInteger(result.total)) throw new Error("Không đọc được danh sách đơn. Hãy làm mới.");
                total = result.total;
                found.push(...result.items.filter((order) => !terminal(order.status)));
                page += 1;
            } while ((page - 1) * 100 < total);
            if (found.length > 1) throw new Error("Bàn có nhiều đơn đang mở. Cần quản trị viên kiểm tra trước khi đặt món.");
            return found.length ? this.api.get(`/orders/${found[0].orderId}`) : null;
        }

        async refresh() {
            if (this.busy || this.loading) return;
            this.loaded = false;
            this.loading = true;
            try {
                if (!Number.isInteger(this.tableId) || this.tableId < 1 || this.tableId > 2147483647) throw new Error("Hãy chọn một bàn hợp lệ.");
                // GET bàn theo id có ghi DB; chỉ dùng danh sách để xem.
                const tables = await this.api.get("/restaurant-tables");
                const table = tables.find((item) => item.tableId === this.tableId);
                if (!table || !table.isActive) throw new Error("Bàn không còn hoạt động. Hãy chọn bàn khác.");
                const currentId = this.order?.orderId || (this.pending?.tableId === this.tableId ? this.pending.orderId : null);
                const order = currentId ? await this.api.get(`/orders/${currentId}`) : await this.findOpenOrder();
                if (order && order.tableId !== this.tableId) {
                    this.order = null;
                    throw new Error("Đơn đã được chuyển sang bàn khác. Hãy chọn lại bàn.");
                }
                this.table = table;
                this.order = order;
                this.loaded = true;
            } finally { this.loading = false; }
        }

        assertWritable() {
            if (!this.isOperator()) throw new Error("Chỉ phục vụ hoặc quản trị viên được sửa đơn tại POS.");
            if (!this.loaded || this.loading || this.busy) throw new Error("Hãy chờ tải xong đơn trước khi thao tác.");
            if (this.order && terminal(this.order.status)) throw new Error("Đơn đã kết thúc. Hãy chọn bàn để mở lượt phục vụ mới.");
            if (!this.table?.isActive || this.table.status === "Maintenance") throw new Error("Bàn không sẵn sàng phục vụ.");
            if (this.pending) throw new Error("Yêu cầu trước chưa được xác nhận. Hãy thử lại yêu cầu đó trước.");
        }

        async send(items, note = "") {
            this.assertWritable();
            if (!this.order && this.table.status !== "Available") throw new Error("Bàn chưa sẵn sàng tạo đơn. Hãy làm mới hoặc chọn lại bàn.");
            if (!items.length || items.some((item) => !Number.isInteger(item.quantity) || item.quantity < 1 || item.quantity > 2147483647)) {
                throw new Error("Số lượng món phải là số nguyên dương.");
            }
            const userId = this.auth.getUser().userId;
            const orderId = this.order?.orderId;
            return this.command({
                kind: orderId ? "add" : "create", method: "post", orderId,
                path: orderId ? `/orders/${orderId}/items` : "/orders",
                body: orderId ? { changedBy: userId, items } : { tableId: this.tableId, createdBy: userId, items, note }
            });
        }

        async changeQuantity(groupKey, quantity, reason, allowWastage = false) {
            this.assertWritable();
            const group = groups(this.order).find((item) => item.key === groupKey);
            if (!group?.active.length) throw new Error("Món không còn trong đơn. Hãy làm mới.");
            if (!Number.isInteger(quantity) || quantity < 0 || quantity > 2147483647) throw new Error("Số lượng phải là số nguyên từ 0 đến 2147483647.");
            if (quantity < group.quantity && !this.canReduce(group)) throw new Error("Bếp đã nhận món. Phục vụ không được giảm số lượng; hãy liên hệ quản trị viên.");
            if (quantity === 0 && !reason.trim()) throw new Error("Nhập lý do khi giảm số lượng về 0.");
            const preparedQuantity = group.active.filter(done).reduce((sum, item) => sum + item.quantity, 0);
            if (quantity < preparedQuantity && !allowWastage) throw new Error("Giảm phần đã nấu cần quản trị viên xác nhận hao hụt.");
            if (quantity === group.quantity) return { kind: "unchanged", order: this.order };
            return this.command({
                kind: "quantity", method: "patch", orderId: this.order.orderId,
                path: `/orders/${this.order.orderId}/items/${group.active[0].id}/quantity`,
                body: { quantity, reason: reason.trim(), reasonCode: "CustomerChange", changedBy: this.auth.getUser().userId, allowWastage: this.isAdmin() && allowWastage }
            });
        }

        async cancelItem(itemId, reason, reasonCode = "CustomerChange") {
            this.assertWritable();
            const item = this.order?.items.find((line) => line.id === itemId);
            if (!item || !this.canCancel(item)) throw new Error("Món không còn được phép hủy tại POS. Hãy làm mới hoặc liên hệ quản trị viên.");
            if (!reason.trim()) throw new Error("Nhập lý do hủy món.");
            return this.command({
                kind: "cancelItem", method: "post", orderId: this.order.orderId, itemId,
                path: `/orders/${this.order.orderId}/items/${itemId}/cancel`,
                body: { reason: reason.trim(), reasonCode: this.isAdmin() ? reasonCode : "CustomerChange", changedBy: this.auth.getUser().userId }
            });
        }

        async cancelOrder(reason, reasonCode = "CustomerChange") {
            this.assertWritable();
            if (!this.isAdmin() || !this.order) throw new Error("Chỉ quản trị viên được hủy toàn bộ đơn tại màn hình này.");
            if (!reason.trim()) throw new Error("Nhập lý do hủy đơn.");
            return this.command({
                kind: "cancelOrder", method: "patch", orderId: this.order.orderId,
                path: `/orders/${this.order.orderId}/status`,
                body: { status: "Cancelled", note: reason.trim(), reasonCode, changedBy: this.auth.getUser().userId }
            });
        }

        async command(command) {
            this.pending = { ...command, key: this.makeKey(), userId: this.auth.getUser().userId, tableId: this.tableId, accepted: false };
            this.savePending();
            return this.resume();
        }

        savePending() { this.storage.setItem(storageKey, JSON.stringify(this.pending)); }
        clearPending() { this.pending = null; this.storage.removeItem(storageKey); }

        async resume() {
            const command = this.pending;
            if (!command || this.busy || this.loading) throw new Error("Không có yêu cầu có thể thử lại lúc này.");
            if (command.userId !== this.auth.getUser()?.userId || command.tableId !== this.tableId) throw new Error("Hãy quay lại đúng bàn có yêu cầu chưa xác nhận.");
            if (!this.isOperator()) throw new Error("Bạn không đủ quyền thử lại yêu cầu POS.");
            if (command.kind === "cancelOrder" && !this.isAdmin()) throw new Error("Chỉ quản trị viên được xác nhận lại lệnh hủy toàn bộ đơn.");
            this.busy = true;
            try {
                // Lệnh hủy không có idempotency ở BE: xem trạng thái trước khi thử lại.
                if (!command.accepted && ["cancelItem", "cancelOrder"].includes(command.kind)) {
                    const latest = await this.api.get(`/orders/${command.orderId}`);
                    if ((command.kind === "cancelOrder" && latest.status === "Cancelled")
                        || (command.kind === "cancelItem" && latest.items.some((item) => item.id === command.itemId && cancelled(item)))) command.accepted = true;
                }
                if (!command.accepted) {
                    const options = ["create", "add", "quantity"].includes(command.kind) ? { headers: { "Idempotency-Key": command.key } } : {};
                    const result = await this.api[command.method](command.path, command.body, options);
                    command.orderId = result?.orderId || command.orderId;
                    if (!command.orderId) throw new Error("Chưa nhận được mã đơn. Hãy thử xác nhận lại yêu cầu.");
                    command.accepted = true;
                    // Lưu ACK trước GET: nếu chỉ tải lại đơn bị lỗi thì không gửi món lần nữa.
                    this.savePending();
                }
                const order = await this.api.get(`/orders/${command.orderId}`);
                const moved = order.tableId !== this.tableId;
                this.order = moved ? null : order;
                this.loaded = !moved;
                this.clearPending();
                return { kind: command.kind, order, moved };
            } catch (error) {
                // Lỗi mạng/5xx chưa biết lệnh đã chạy hay chưa: giữ nguyên body/key để thử lại.
                if (!command.accepted && [400, 401, 403, 404, 409, 410, 422].includes(error.status)) this.clearPending();
                throw error;
            } finally { this.busy = false; }
        }
    }

    window.restaurantPos = { OrderSession, groups, terminal, pending, cancelled };
})();
