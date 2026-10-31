(function () {
    const pendingKey = "restaurant.paymentPending";
    const momoKey = "restaurant.paymentMomo";
    const methods = { Cash: "Tiền mặt", Card: "Thẻ", Bank: "Ngân hàng", EWallet: "Ví điện tử khác", Transfer: "Chuyển khoản", MoMo: "MoMo" };
    const done = (status) => ["Ready", "Completed"].includes(status);
    const fail = (message, status = 409) => Object.assign(new Error(message), { status });
    const snapshot = (order) => JSON.stringify(order && [order.orderId, order.tableId, order.status, order.updatedAt,
        order.subtotal, order.discountAmount, order.taxAmount, order.serviceChargeAmount, order.totalAmount, order.items]);

    function payBlock(order, payments = []) {
        if (!order || !Array.isArray(order.items)) return "Chọn đơn và tải đầy đủ chi tiết trước khi thu tiền.";
        if (order.status === "Paid" || payments.some((p) => p.status === "Paid")) return "Đơn đã được thanh toán.";
        if (order.status === "Cancelled") return "Đơn đã hủy.";
        if (!done(order.status)) return "Đang chờ bếp hoàn thành món.";
        const active = order.items.filter((i) => i.status !== "Cancelled");
        if (!active.length) return "Đơn không còn món để thu tiền.";
        if (active.some((i) => !done(i.status))) return "Đang chờ bếp hoàn thành món.";
        if (!(order.totalAmount > 0)) return "Tổng đơn phải lớn hơn 0đ.";
        if (payments.some((p) => p.status === "Pending")) return "Có giao dịch đang chờ kết quả. Hãy đối chiếu trước khi thu tiếp.";
        return "";
    }

    // Backend lọc PaidAt theo UTC. Ngày trên bộ lọc là ngày Việt Nam, gồm trọn ngày cuối.
    function dateBound(value, end = false) {
        if (!value) return "";
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) throw fail("Ngày lọc không hợp lệ.", 400);
        const date = new Date(`${value}T${end ? "23:59:59.999" : "00:00:00.000"}+07:00`);
        if (!Number.isFinite(date.getTime()) || new Date(date.getTime() + 7 * 3600000).toISOString().slice(0, 10) !== value) throw fail("Ngày lọc không hợp lệ.", 400);
        return date.toISOString();
    }

    class PaymentSession {
        constructor({ api, auth, storage, makeKey }) {
            this.api = api; this.auth = auth; this.storage = storage; this.makeKey = makeKey;
            this.orders = []; this.tables = []; this.order = null; this.payments = [];
            this.ledger = { items: [], total: 0, page: 1, pageSize: 20 };
            this.loaded = false; this.busy = false; this.loading = false; this.pending = null; this.momo = null;
            for (const [key, field] of [[pendingKey, "pending"], [momoKey, "momo"]]) {
                try {
                    const saved = JSON.parse(storage.getItem(key));
                    if (saved?.userId === auth.getUser()?.userId && Number.isInteger(saved.orderId)) this[field] = { ...saved, reviewed: false };
                } catch (_) { storage.removeItem(key); }
            }
        }
        hasAccess() { return this.auth.hasRole("Admin", "Cashier"); }
        assertAccess() { if (!this.hasAccess()) throw fail("Chỉ Thu ngân hoặc Quản trị viên được truy cập màn Thanh toán.", 403); }
        savePending() { this.storage.setItem(pendingKey, JSON.stringify(this.pending)); }
        clearPending() { this.pending = null; this.storage.removeItem(pendingKey); }
        saveMomo(intent) {
            this.momo = { ...intent, userId: this.auth.getUser().userId };
            this.storage.setItem(momoKey, JSON.stringify(this.momo));
        }

        async allOrders(status, tableId = null) {
            const result = []; let page = 1;
            for (;;) {
                const payload = await this.api.get(`/orders?status=${status}&page=${page}&pageSize=100${tableId ? `&tableId=${tableId}` : ""}`);
                if (!Array.isArray(payload.items) || !Number.isInteger(payload.total)) throw fail("Không đọc được danh sách đơn.", 502);
                result.push(...payload.items);
                if (page * 100 >= payload.total) return result;
                if (!payload.items.length) throw fail("Danh sách đơn vừa thay đổi. Hãy làm mới.", 409);
                page += 1;
            }
        }
        async loadQueue() {
            this.assertAccess();
            const [tables, ...pages] = await Promise.all([this.api.get("/restaurant-tables"),
                ...["Ready", "Completed", "Pending", "SentToKitchen", "Preparing"].map((s) => this.allOrders(s))]);
            if (!Array.isArray(tables)) throw fail("Không đọc được danh sách bàn.", 502);
            this.tables = tables;
            this.orders = Array.from(new Map(pages.flat().map((o) => [o.orderId, o])).values());
        }
        async loadOrder(orderId) {
            this.assertAccess(); this.loaded = false;
            const [order, payments] = await Promise.all([this.api.get(`/orders/${orderId}`), this.api.get(`/payments/order/${orderId}`)]);
            if (!Array.isArray(order.items) || !Array.isArray(payments)) throw fail("Chi tiết đơn hoặc phiếu thu chưa đầy đủ.", 502);
            this.order = order; this.payments = payments; this.loaded = true;
        }
        async select(orderId) {
            if (this.busy || this.loading) return;
            this.loading = true; this.loaded = false; this.order = null; this.payments = [];
            try { await this.loadOrder(orderId); } finally { this.loading = false; }
        }
        async loadLedger(filters = {}, page = 1) {
            this.assertAccess();
            const query = new URLSearchParams({ page: String(page), pageSize: "20" });
            for (const key of ["status", "method", "orderId"]) if (filters[key]) query.set(key, filters[key]);
            if (filters.orderId && (!/^\d+$/.test(String(filters.orderId)) || Number(filters.orderId) < 1)) throw fail("Mã đơn phải là số nguyên dương.", 400);
            if (filters.from && filters.to && filters.from > filters.to) throw fail("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.", 400);
            if (filters.from) query.set("from", dateBound(filters.from));
            if (filters.to) query.set("to", dateBound(filters.to, true));
            const data = await this.api.get(`/payments?${query}`);
            if (!Array.isArray(data.items)) throw fail("Không đọc được danh sách phiếu thu.", 502);
            this.ledger = data;
        }
        async refresh(filters = {}, page = this.ledger.page) {
            this.assertAccess();
            if (this.busy || this.loading) return;
            this.loading = true;
            try {
                await Promise.all([this.loadQueue(), this.loadLedger(filters, page),
                    this.order ? this.loadOrder(this.order.orderId) : Promise.resolve()]);
            } catch (error) { this.loaded = false; throw error; }
            finally { this.loading = false; }
        }
        block() { return !this.loaded ? "Tải lại dữ liệu đơn trước khi thanh toán." : payBlock(this.order, this.payments); }
        pendingMomo() { return this.payments.find((p) => p.provider === "MoMo" && p.status === "Pending"); }
        intent() {
            const pending = this.pendingMomo();
            return pending && this.momo?.paymentId === pending.paymentId && this.momo.orderId === this.order?.orderId ? this.momo : null;
        }

        async command(kind, data = {}, expected) {
            this.assertAccess();
            if (this.busy || this.loading || this.pending) throw fail("Chờ hoặc đối chiếu yêu cầu trước trước khi tiếp tục.");
            if (!this.order) throw fail("Chọn đơn cần xử lý.", 400);
            this.busy = true;
            try {
                const orderId = this.order.orderId;
                await this.loadOrder(orderId);
                if (expected && expected !== snapshot(this.order)) throw fail("Đơn vừa thay đổi. Hãy kiểm tra lại số tiền và món trước khi xác nhận.");
                const createdBy = this.auth.getUser().userId;
                let path, body, key = null;
                if (kind === "direct" || kind === "momo") {
                    // Khôi phục URL của intent đang chờ không tạo thêm intent; backend tái dùng cùng paymentId.
                    const existing = this.pendingMomo();
                    const reason = payBlock(this.order, kind === "momo" && existing ? this.payments.filter((p) => p.paymentId !== existing.paymentId) : this.payments);
                    if (reason) throw fail(reason);
                    if (kind === "direct") {
                        if (!Object.hasOwn(methods, data.method) || data.method === "MoMo") throw fail("Chọn phương thức thu trực tiếp hợp lệ.", 400);
                        const code = String(data.transactionCode || "").trim();
                        if (code.length > 100) throw fail("Mã giao dịch tối đa 100 ký tự.", 400);
                        key = this.makeKey();
                        // Mã ổn định còn đối chiếu được sau khi cache idempotency của backend khởi động lại.
                        body = { orderId, paymentMethod: data.method, transactionCode: code || `FE-${key}`, createdBy };
                        path = "/payments";
                    } else { path = "/payments/momo"; body = { orderId, createdBy }; }
                } else if (kind === "refund") {
                    const payment = this.payments.find((p) => p.paymentId === data.paymentId);
                    if (payment?.status !== "Paid" || this.order.status !== "Paid") throw fail("Chỉ hoàn được phiếu đã thu của đơn đang Paid.");
                    const reason = String(data.reason || "").trim();
                    if (!reason || reason.length > 500) throw fail("Nhập lý do hoàn tiền từ 1 đến 500 ký tự.", 400);
                    // Hoàn tiền mở lại đơn ở bàn cũ. Không mở chồng lên lượt khách mới.
                    const active = await Promise.all(["Ready", "Completed", "Pending", "SentToKitchen", "Preparing"].map((status) => this.allOrders(status, this.order.tableId)));
                    if (active.flat().some((o) => o.orderId !== orderId)) throw fail("Bàn này đã có đơn khác đang mở. Cần đối chiếu với quản trị viên trước khi hoàn và mở lại đơn cũ.");
                    path = `/payments/${payment.paymentId}/refund`; body = { reason, createdBy };
                } else if (kind === "mock") {
                    const payment = this.pendingMomo(), intent = this.intent();
                    if (!payment || !intent?.isMock || typeof data.success !== "boolean") throw fail("Chỉ giả lập giao dịch Mock đang chờ.", 400);
                    if (data.success && (payBlock(this.order, this.payments.filter((p) => p.paymentId !== payment.paymentId)) || payment.amount !== this.order.totalAmount)) throw fail("Đơn hoặc số tiền đã thay đổi. Không thể giả lập thu thành công.");
                    path = `/payments/momo/mock/${payment.paymentId}?success=${data.success}`;
                } else throw fail("Thao tác thanh toán không hợp lệ.", 400);

                this.pending = { kind, userId: createdBy, orderId, path, body, key, paymentId: data.paymentId || this.pendingMomo()?.paymentId,
                    accepted: false, reviewed: false };
                this.savePending();
                const result = await this.api.post(path, body, key ? { headers: { "Idempotency-Key": key } } : {});
                if (kind === "mock" && !result?.accepted) throw fail(result?.message || "Backend từ chối kết quả giả lập.");
                this.pending.accepted = true; this.pending.result = result; this.savePending();
                if (kind === "momo") this.saveMomo(result);
                await this.loadOrder(orderId);
                this.clearPending();
                return result;
            } catch (error) {
                if (this.pending && !this.pending.accepted && [400, 401, 403, 404, 409, 410, 422].includes(error.status)) this.clearPending();
                if (this.pending) this.loaded = false;
                throw error;
            } finally { this.busy = false; }
        }

        async reviewPending() {
            this.assertAccess();
            if (!this.pending || this.busy || this.loading) return;
            if (this.pending.userId !== this.auth.getUser()?.userId) throw fail("Yêu cầu thuộc phiên đăng nhập khác.", 403);
            this.busy = true;
            try {
                await this.loadOrder(this.pending.orderId);
                const p = this.pending;
                const matched = p.kind === "direct" ? this.payments.find((x) => x.transactionCode === p.body.transactionCode)
                    : p.kind === "refund" ? this.payments.find((x) => x.paymentId === p.paymentId && x.status === "Refunded") : null;
                if (p.accepted || matched) { this.clearPending(); return { resolved: true, payment: matched || p.result }; }
                p.reviewed = true; this.savePending(); return { resolved: false };
            } finally { this.busy = false; }
        }
        acknowledgeReview() {
            if (!this.loaded || !this.pending?.reviewed || this.loading || this.busy) throw fail("Tải dữ liệu mới để đối chiếu trước khi tiếp tục.");
            this.clearPending();
        }
    }
    window.restaurantPayments = { PaymentSession, methods, payBlock, snapshot, dateBound };
})();
