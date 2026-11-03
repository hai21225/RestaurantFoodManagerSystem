(function () {
    const $ = (id) => document.getElementById(id);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const quantity = (value) => Number.isFinite(Number(value)) ? Number(value).toLocaleString("vi-VN", { maximumFractionDigits: 3 }) : "—";
    const money = (value) => Number.isFinite(Number(value)) ? `${Number(value).toLocaleString("vi-VN", { maximumFractionDigits: 2 })}đ` : "—";
    const time = (value) => {
        if (!value) return "—";
        const date = new Date(/[zZ]$|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
        return Number.isFinite(date.getTime()) ? date.toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" }) : "—";
    };
    const kindLabels = { adjust: "Điều chỉnh tồn", createPurchase: "Tạo phiếu nhập", receive: "Nhận hàng", cancelPurchase: "Hủy phiếu nhập", createSupplier: "Thêm nhà cung cấp", updateSupplier: "Sửa nhà cung cấp", supplierActive: "Đổi trạng thái nhà cung cấp", deleteSupplier: "Ngừng hợp tác nhà cung cấp" };
    function createPage(session, renderPage) {
        const locked = () => session.busy || session.loading || Boolean(session.pending) || !session.loaded;
        function message(text, error = false, target = "uiMessage") {
            $(target).textContent = text; $(target).classList.toggle("is-error", error);
        }
        function render() {
            renderPage();
            const p = session.pending;
            $("warehousePending").hidden = !p;
            if (p) {
                const details = p.kind === "adjust" ? `Nguyên liệu #${p.id} · ${quantity(p.body.quantity)} · ${p.body.note}`
                    : p.kind === "createPurchase" ? `Nhà cung cấp #${p.body.supplierId} · ${p.body.items.length} dòng nguyên liệu`
                    : p.kind.endsWith("Supplier") ? `${p.body?.name || "Nhà cung cấp"} #${p.id || "chưa rõ mã"}` : `Chứng từ/nhà cung cấp #${p.id}`;
                $("warehousePendingSummary").textContent = `${kindLabels[p.kind] || "Thao tác kho"}: ${details}. Chưa đối chiếu xong. Hãy tải dữ liệu mới, không gửi lại lệnh.`;
            }
            $("warehouseReview").disabled = session.busy || session.loading;
            const reviewPage = p?.kind === "adjust" ? "inventory" : "purchases";
            const link = $("warehousePendingLink");
            if (link) { link.hidden = !p || reviewPage === session.mode; link.href = `${reviewPage}.html`; }
            if (p && reviewPage !== session.mode) $("warehouseReview").disabled = true;
            $("warehouseAck").hidden = !p?.reviewed;
            $("warehouseAck").disabled = session.busy || session.loading || !session.loaded;
            $("warehouseRefresh").disabled = session.busy || session.loading;
        }
        async function run(action, success = "", errorTarget = "uiMessage") {
            try {
                const promise = action(); render(); const result = await promise;
                if (success) message(success); return result;
            } catch (error) {
                message(error.message, true, errorTarget);
                if ([400, 404, 409].includes(error.status) && !session.pending && session.hasAccess()) {
                    try { await session.refresh(); } catch (_) { /* Giữ lỗi của thao tác chính. */ }
                }
            } finally { render(); }
        }
        $("warehouseRefresh").addEventListener("click", () => run(() => session.refresh(), "Đã tải dữ liệu mới nhất."));
        $("warehouseReview").addEventListener("click", () => run(async () => {
            const resolved = await session.reviewPending();
            message(resolved ? "Đã đối chiếu kết quả thao tác." : "Đã tải dữ liệu mới. Đối chiếu phiếu, tồn kho và lịch sử trước khi tiếp tục.");
        }));
        $("warehouseAck").addEventListener("click", () => run(() => { session.acknowledgeReview(); message("Đã xác nhận đối chiếu. Kiểm tra dữ liệu trước khi tạo yêu cầu mới."); }));
        return { $, esc, quantity, money, time, locked, message, render, run };
    }
    window.restaurantWarehouseUi = { createPage, $, esc, quantity, money, time };
})();
