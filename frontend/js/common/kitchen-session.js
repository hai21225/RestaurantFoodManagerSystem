(function () {
    const storageKey = "restaurant.kitchenPending";
    const isDone = (status) => ["Ready", "Completed"].includes(status);
    const orderClosed = (status) => ["Paid", "Cancelled"].includes(status);
    const ticketClosed = (status) => ["Completed", "Cancelled"].includes(status);
    const snapshot = (item) => JSON.stringify([item.id, item.orderItemId, item.status, item.quantity]);
    const failure = (message, status = 409) => Object.assign(new Error(message), { status });

    class KitchenSession {
        constructor({ api, auth, storage }) {
            this.api = api; this.auth = auth; this.storage = storage;
            this.tickets = []; this.receipt = null;
            this.loaded = false; this.loading = false; this.busy = false;
            this.pending = null;
            try {
                const saved = JSON.parse(storage.getItem(storageKey));
                if (saved?.userId === auth.getUser()?.userId && Number.isInteger(saved.ticketId) && Number.isInteger(saved.itemId)) {
                    this.pending = { ...saved, reviewed: false };
                }
            } catch (_) { storage.removeItem(storageKey); }
        }

        hasAccess() { return this.auth.hasRole("Chef", "Admin"); }
        find(ticketId, itemId) {
            const ticket = this.tickets.find((row) => row.kitchenOrderId === ticketId)
                || (this.receipt?.kitchenOrderId === ticketId ? this.receipt : null);
            return { ticket, item: ticket?.items.find((row) => row.id === itemId) };
        }
        allowed(action, ticket, item) {
            if (!this.hasAccess() || !ticket || !item || ticketClosed(ticket.status) || orderClosed(ticket.order.status)) return false;
            if (action === "receive") return item.status === "Pending";
            if (action === "prepared") return item.status === "Preparing";
            if (action === "shortage") return ["Pending", "Preparing"].includes(item.status);
            if (action === "cancel") return ["Pending", "Preparing"].includes(item.status) || (item.status === "Ready" && this.auth.hasRole("Admin"));
            if (action === "reopen") return isDone(item.status);
            return false;
        }

        async load(focusId = null) {
            if (!this.hasAccess()) { this.loaded = false; this.tickets = []; this.receipt = null; throw failure("Chỉ nhân viên bếp hoặc quản trị viên được truy cập màn Bếp.", 403); }
            this.loading = true;
            this.loaded = false;
            try {
                // Snapshot đầy đủ của phiếu mở: phiếu bị đóng/hủy sẽ biến mất ở lần poll sau.
                const open = await this.api.get("/management/kitchen/orders?status=open");
                if (!Array.isArray(open)) throw failure("Không đọc được danh sách phiếu bếp.", 502);
                let focus = open.find((ticket) => ticket.kitchenOrderId === focusId);
                if (focusId && !focus) {
                    const all = await this.api.get("/management/kitchen/orders");
                    focus = all.find((ticket) => ticket.kitchenOrderId === focusId);
                }
                const orderReads = new Map();
                const hydrate = async (ticket) => {
                    if (!orderReads.has(ticket.orderId)) orderReads.set(ticket.orderId, this.api.get(`/orders/${ticket.orderId}`));
                    const [items, order] = await Promise.all([
                        this.api.get(`/management/kitchen/orders/${ticket.kitchenOrderId}/items`), orderReads.get(ticket.orderId)
                    ]);
                    if (!Array.isArray(items) || !Array.isArray(order.items)) throw failure("Dữ liệu món chưa đầy đủ. Hãy làm mới.", 502);
                    return { ...ticket, order, items: items.map((item) => {
                        const detail = order.items.find((line) => line.id === item.orderItemId);
                        return { ...item, quantity: item.quantity ?? item.quanlity, cancelReason: detail?.cancelReason || "", cancelledBy: detail?.cancelledBy,
                            toppings: detail?.toppings || [] };
                    }) };
                };
                const rows = await Promise.all(open.map(hydrate));
                const receipt = focus ? rows.find((ticket) => ticket.kitchenOrderId === focusId) || await hydrate(focus) : null;
                this.tickets = rows;
                if (focusId) this.receipt = receipt;
                else if (this.receipt) this.receipt = rows.find((row) => row.kitchenOrderId === this.receipt.kitchenOrderId) || this.receipt;
                this.loaded = true;
            } finally { this.loading = false; }
        }

        async refresh(focusId = this.pending?.ticketId) {
            if (this.busy || this.loading) return;
            await this.load(focusId);
        }

        save() { this.storage.setItem(storageKey, JSON.stringify(this.pending)); }
        clearPending() { this.pending = null; this.storage.removeItem(storageKey); }

        async perform(action, ticketId, itemId, data = {}, expected) {
            if (!this.hasAccess()) throw failure("Bạn không có quyền xử lý món bếp.", 403);
            if (this.busy || this.loading || this.pending) throw failure("Hãy chờ hoặc đối chiếu yêu cầu trước trước khi xử lý món tiếp.");
            this.busy = true;
            try {
                await this.load(ticketId);
                const { ticket, item } = this.find(ticketId, itemId);
                if (!this.allowed(action, ticket, item)) throw failure("Trạng thái món/phiếu đã đổi hoặc bạn không đủ quyền. Hãy kiểm tra phiếu mới nhất.");
                if (expected && expected !== snapshot(item)) throw failure("Số phần hoặc trạng thái món vừa thay đổi. Hãy mở lại thao tác trên dữ liệu mới nhất.");
                const reason = String(data.reason || "").trim();
                if (reason.length > 500) throw failure("Lý do tối đa 500 ký tự.", 400);
                if (["shortage", "cancel", "reopen"].includes(action) && !reason) throw failure("Nhập lý do trước khi xác nhận.", 400);
                const quantity = Number(data.preparedQuantity);
                if (["prepared", "shortage"].includes(action) && (!Number.isInteger(quantity) || quantity < (action === "shortage" ? 0 : 1)
                    || quantity > item.quantity || (action === "shortage" && quantity === item.quantity))) {
                    throw failure(action === "shortage" ? `Số phần làm được phải là số nguyên từ 0 đến ${item.quantity - 1}. Nếu làm đủ, dùng Báo đã làm.`
                        : `Số phần đã làm phải là số nguyên từ 1 đến ${item.quantity}.`, 400);
                }
                if (action === "shortage" && !["OutOfStock", "PhysicalShortage"].includes(data.reasonCode)) throw failure("Chọn lý do hết hoặc thiếu/hỏng nguyên liệu.", 400);
                const base = `/management/kitchen/items/${itemId}`;
                const changedBy = this.auth.getUser().userId;
                let path, method, body;
                if (action === "receive") { path = `${base}/status`; method = "patch"; body = { status: "Preparing", changedBy }; }
                if (action === "prepared") { path = `${base}/prepared-quantity`; method = "patch"; body = { preparedQuantity: quantity, changedBy, reason }; }
                if (action === "shortage") {
                    path = `${base}/shortage`; method = "post";
                    body = { preparedQuantity: quantity, reasonCode: data.reasonCode, changedBy, reason, deactivateDish: Boolean(data.deactivateDish),
                        inactiveReason: data.deactivateDish ? reason : null };
                }
                if (action === "cancel") {
                    if (!["CustomerChange", "KitchenError"].includes(data.reasonCode)) throw failure("Chọn lý do hủy hợp lệ.", 400);
                    path = `${base}/status`; method = "patch"; body = { status: "Cancelled", changedBy, reason, reasonCode: data.reasonCode };
                }
                if (action === "reopen") { path = `${base}/reopen`; method = "post"; body = { changedBy, reason }; }
                this.pending = { userId: changedBy, action, ticketId, itemId, body, before: { name: item.foodName, quantity: item.quantity, status: item.status }, accepted: false, reviewed: false };
                this.save();
                await this.api[method](path, body);
                this.pending.accepted = true;
                this.save();
                await this.load(ticketId);
                this.clearPending();
                return this.find(ticketId, itemId);
            } catch (error) {
                if (this.pending && !this.pending.accepted && [400, 401, 403, 404, 409, 410, 422].includes(error.status)) this.clearPending();
                if (this.pending) this.loaded = false;
                throw error;
            } finally { this.busy = false; }
        }

        async reviewPending() {
            if (!this.pending || this.busy || this.loading) return;
            if (this.pending.userId !== this.auth.getUser()?.userId) throw failure("Yêu cầu thuộc phiên đăng nhập khác.", 403);
            this.busy = true;
            try {
                await this.load(this.pending.ticketId);
                if (this.pending.accepted) this.clearPending();
                else { this.pending.reviewed = true; this.save(); }
            } finally { this.busy = false; }
        }

        acknowledgeReview() {
            if (!this.loaded || !this.pending?.reviewed || this.loading || this.busy) throw failure("Tải dữ liệu mới để đối chiếu phiếu trước khi tiếp tục.");
            this.clearPending();
        }
    }
    window.restaurantKitchen = { KitchenSession, isDone, orderClosed, ticketClosed, snapshot };
})();
