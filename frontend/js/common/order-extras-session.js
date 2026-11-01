(function () {
    const { WriteGuard, fail, id, text, snapshot } = window.restaurantAdmin;
    const terminal = (o) => !o || ["Paid", "Cancelled"].includes(o.status);
    class OrderExtrasSession extends WriteGuard {
        constructor(options) {
            super({ ...options, key: "restaurant.orderExtrasPending" });
            this.orderId = Number(this.pending?.context?.orderId || options.orderId);
            this.order = null; this.tables = []; this.promotions = []; this.loaded = false; this.loading = false;
        }
        canEdit() { return this.auth.hasRole("Admin", "Waiter"); }
        async load() {
            this.loaded = false;
            if (!this.auth.hasRole("Admin", "Waiter", "Cashier") || !id(this.orderId)) throw fail("Chọn một đơn hợp lệ mà bạn có quyền xem.", 403);
            const [order, tables] = await Promise.all([this.api.get(`/orders/${this.orderId}`), this.api.get("/restaurant-tables")]);
            if (order?.orderId !== this.orderId || !Array.isArray(order.items) || !Array.isArray(tables)) throw fail("Chưa đọc được đầy đủ đơn/bàn.", 502);
            this.order = order; this.tables = tables;
            // Danh sách khuyến mãi hiện là AdminOnly; không mở quyền quản trị cho Phục vụ.
            this.promotions = this.auth.hasRole("Admin") ? await this.api.get("/management/promotions") : [];
            this.loaded = true;
        }
        async refresh() { if (this.busy || this.loading) return; this.loading = true; try { await this.load(); } finally { this.loading = false; } }
        otherPending() {
            for (const key of ["restaurant.posPending", "restaurant.paymentPending"]) {
                try {
                    const p = JSON.parse(this.storage.getItem(key));
                    if (p?.userId === this.auth.getUser()?.userId) return true;
                } catch (_) { /* Dữ liệu hỏng không được coi là một lệnh có thể gửi lại. */ }
            }
            return false;
        }
        async command(kind, data, expected) {
            if (!this.canEdit()) throw fail("Chỉ Quản trị hoặc Phục vụ được sửa đơn.", 403);
            if (!this.loaded || this.loading || this.otherPending()) throw fail("Tải xong đơn và đối chiếu yêu cầu Đặt món/Thanh toán trước.");
            if (!["note", "move", "apply", "remove"].includes(kind)) throw fail("Thao tác không hợp lệ.", 400);
            let body;
            if (kind === "note") body = { note: text(data.note, 500), changedBy: this.auth.getUser().userId };
            if (kind === "move") { if (!id(data.tableId)) throw fail("Chọn bàn đích hợp lệ.", 400); body = { tableId: Number(data.tableId), changedBy: this.auth.getUser().userId }; }
            if (["apply", "remove"].includes(kind) && !id(data.promotionId)) throw fail("Mã khuyến mãi không hợp lệ.", 400);
            this.context = { orderId: this.orderId, tableId: this.order.tableId };
            let path = `/orders/${this.orderId}`;
            return this.write(`Đơn #${this.orderId}: ${({ note: "ghi chú hóa đơn", move: "chuyển bàn", apply: "áp khuyến mãi", remove: "gỡ khuyến mãi" })[kind]}`, async () => {
                await this.load();
                if (!this.canEdit() || this.otherPending()) throw fail("Quyền hoặc yêu cầu đang chờ đã thay đổi.");
                if (terminal(this.order) || !expected || snapshot(this.order) !== expected) throw fail("Đơn đã đổi hoặc kết thúc. Tải lại và kiểm tra trước khi thực hiện.");
                if (kind === "move") {
                    const target = this.tables.find((t) => t.tableId === body.tableId);
                    if (!target?.isActive || target.status !== "Available" || target.tableId === this.order.tableId) throw fail("Bàn đích phải khác bàn hiện tại, đang hoạt động và đang trống.");
                    // GET danh sách là thuần đọc; backend vẫn khóa/chốt nguyên tử bàn đích.
                    const orders = await this.api.get(`/orders?tableId=${body.tableId}&page=1&pageSize=100`);
                    if (!Array.isArray(orders.items) || orders.items.some((o) => !terminal(o))) throw fail("Bàn đích đang có đơn hoặc chưa đối chiếu được trạng thái.");
                }
                if (kind === "remove" && this.order.promotionId !== Number(data.promotionId)) throw fail("Khuyến mãi trên đơn đã đổi.");
            }, () => {
                if (kind === "note") return this.api.patch(`${path}/note`, body);
                if (kind === "move") return this.api.patch(`${path}/table`, body);
                if (kind === "apply") return this.api.patch(`${path}/promotion/${Number(data.promotionId)}`);
                return this.api.delete(`${path}/promotion/${Number(data.promotionId)}`);
            }, () => this.load());
        }
    }
    window.restaurantOrderExtras = { OrderExtrasSession };
})();
