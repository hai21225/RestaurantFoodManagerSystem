(function () {
    const { PaymentSession, methods, snapshot } = window.restaurantPayments;
    const auth = window.restaurantAuth;
    const session = new PaymentSession({ api: window.restaurantApi, auth, storage: window.sessionStorage, makeKey: () => window.crypto.randomUUID() });
    const $ = (id) => document.getElementById(id);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const money = (value) => Number.isFinite(Number(value)) ? `${Number(value).toLocaleString("vi-VN", { maximumFractionDigits: 2 })}đ` : "—";
    const time = (value) => {
        if (!value) return "—";
        const date = new Date(/[zZ]$|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
        return Number.isFinite(date.getTime()) ? date.toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" }) : "—";
    };
    const labels = { Pending: "Chờ xử lý", SentToKitchen: "Đã gửi bếp", Preparing: "Bếp đang làm", Ready: "Sẵn sàng", Completed: "Hoàn tất", Paid: "Đã thu", Cancelled: "Đã hủy", Failed: "Thất bại", Refunded: "Đã hoàn tiền" };
    const state = { method: "Cash", filters: {}, modal: null, lastRefresh: 0, printing: false };
    const htmlCache = new Map();
    function html(id, value) { if (htmlCache.get(id) !== value) { $(id).innerHTML = value; htmlCache.set(id, value); } }
    const tableName = (id) => session.tables.find((t) => t.tableId === id)?.tableNumber || `#${id}`;
    const locked = () => session.busy || session.loading || Boolean(session.pending);
    function notice(message = "", error = false) {
        $("paymentNotice").textContent = message;
        $("paymentNotice").classList.toggle("is-error", error);
        $("paymentNotice").hidden = !message;
    }
    function totals(order) {
        return [["Tạm tính", order?.subtotal], ["Giảm giá", order?.discountAmount], ["Phí dịch vụ", order?.serviceChargeAmount], ["Thuế", order?.taxAmount], ["Tổng đơn", order?.totalAmount]]
            .map(([label, value], i) => `<div${i === 4 ? ' class="grand-total"' : ""}><span>${label}</span><strong>${order ? money(value) : "—"}</strong></div>`).join("");
    }
    function itemsHtml(items, tableBill = false) {
        return `<ul class="bill-items">${items.map((i) => `<li${i.status === "Cancelled" ? ' class="cancelled-item"' : ""}>
            <div><strong>${esc(i.foodName)}</strong><span>${esc(i.sizeName)} · Đơn giá ${money(i.unitPrice)} (đã gồm topping)</span>
            ${(i.toppings || []).length ? `<span>${i.toppings.map((t) => `${esc(t.toppingName)} × ${esc(t.quantity)}`).join(", ")}</span>` : ""}
            ${i.note ? `<span>Ghi chú: ${esc(i.note)}</span>` : ""}${i.cancelReason ? `<span>Lý do hủy: ${esc(i.cancelReason)}</span>` : ""}</div>
            <strong>× ${esc(i.quantity)}</strong><span>${tableBill ? money(i.lineTotal) : esc(labels[i.status] || i.status)}</span></li>`).join("")}</ul>`;
    }
    function renderQueue() {
        const term = $("billSearchInput").value.trim().toLocaleLowerCase("vi"), filter = $("queueStatus").value;
        const orders = session.orders.filter((o) => (filter !== "ready" || ["Ready", "Completed"].includes(o.status))
            && (filter !== "kitchen" || !["Ready", "Completed"].includes(o.status))
            && `${tableName(o.tableId)} ORD-${o.orderId}`.toLocaleLowerCase("vi").includes(term));
        $("billCount").textContent = `${orders.length} đơn`;
        html("billList", orders.length ? orders.map((o) => `<button class="bill-button${o.orderId === session.order?.orderId ? " is-selected" : ""}"
            type="button" data-select-order="${o.orderId}"${locked() ? " disabled" : ""}>
            <div class="bill-button-top"><strong>Bàn ${esc(tableName(o.tableId))}</strong><span>${esc(labels[o.status] || o.status)}</span></div>
            <span>ORD-${o.orderId}</span><div class="bill-button-bottom"><span>${time(o.createdAt)}</span><strong>${money(o.totalAmount)}</strong></div></button>`).join("")
            : '<p class="empty-state">Không có đơn phù hợp. Mở đơn đã thu từ danh sách phiếu thu bên dưới.</p>');
        const ready = session.orders.filter((o) => ["Ready", "Completed"].includes(o.status)).length;
        html("paymentSummary", [["Đơn đã xong bếp", ready], ["Đơn chờ bếp", session.orders.length - ready], ["Phiếu thu theo bộ lọc", session.ledger.total], ["Bàn đang chọn", session.order ? tableName(session.order.tableId) : "—"]]
            .map(([label, value]) => `<article class="payment-summary-card"><span>${label}</span><strong>${esc(value)}</strong></article>`).join(""));
    }
    function renderDetail() {
        const order = session.order;
        $("selectedBillStatus").textContent = order ? labels[order.status] || order.status : "Chưa chọn";
        const tableStatuses = { Available: "Trống", Occupied: "Có khách", Maintenance: "Bảo trì", Reserved: "Đã đặt" };
        const tableStatus = session.tables.find((t) => t.tableId === order?.tableId)?.status;
        html("billDetail", order ? `<div class="bill-heading"><strong>Bàn ${esc(tableName(order.tableId))} · ORD-${order.orderId}</strong>
            <span>Mở lúc ${time(order.createdAt)}</span><span>Trạng thái bàn: ${esc(tableStatuses[tableStatus] || tableStatus || "—")}</span>
            ${order.note ? `<p>Ghi chú hóa đơn: ${esc(order.note)}</p>` : ""}</div>${itemsHtml(order.items)}` : '<p class="empty-state">Chọn đơn để xem món và lịch sử thanh toán.</p>');
        html("paymentTotals", totals(order)); $("paymentBlock").textContent = session.block();
        $("confirmPaymentButton").disabled = locked() || Boolean(session.block()) || state.method === "MoMo";
        $("confirmPaymentButton").textContent = session.busy ? "Đang xử lý…" : "Xác nhận đã nhận tiền";
        $("transactionCode").disabled = locked() || state.method === "MoMo";
        $("printBillButton").disabled = locked() || !session.loaded;
        $("tableBillButton").disabled = locked() || !session.loaded || ["Paid", "Cancelled"].includes(order?.status);
        html("methodList", Object.entries(methods).map(([value, label]) => `<button class="method-button${value === state.method ? " is-active" : ""}"
            aria-pressed="${value === state.method}" type="button" data-method="${value}"${locked() ? " disabled" : ""}>${label}</button>`).join(""));
        $("paymentMethodLabel").textContent = methods[state.method];
        $("methodHint").textContent = state.method === "MoMo" ? "Tạo giao dịch và chờ xác nhận kết quả. Tạo mã chưa có nghĩa đã thu tiền."
            : "Chỉ xác nhận sau khi đã nhận tiền hoặc kiểm tra giao dịch bên ngoài. Phần mềm ghi nhận khoản thu.";
        html("orderPayments", session.payments.length ? session.payments.map((p) => `<article class="payment-record">
            <div><strong>Phiếu #${p.paymentId} · ${esc(labels[p.status] || p.status)}</strong><span>${esc(methods[p.paymentMethod] || p.paymentMethod)} · ${money(p.amount)}</span>
            <span>Mã: ${esc(p.transactionCode)}</span><span>Tạo: ${time(p.createdAt)} · Thu: ${time(p.paidAt)} · Người thu #${p.createdBy}</span>
            ${p.refundedAt ? `<span>Hoàn: ${time(p.refundedAt)}</span>` : ""}${p.note ? `<span>${esc(p.note)}</span>` : ""}</div>
            ${p.status === "Paid" && order?.status === "Paid" ? `<button class="secondary-action" type="button" data-refund="${p.paymentId}"${locked() || !session.loaded ? " disabled" : ""}>Hoàn toàn bộ phiếu #${p.paymentId}</button>` : ""}</article>`).join("") : '<p class="empty-state">Đơn chưa có phiếu thu.</p>');
        renderMomo();
    }
    function safeUrl(value) {
        try { const url = new URL(value); return ["http:", "https:"].includes(url.protocol) ? url.href : ""; } catch (_) { return ""; }
    }
    function renderMomo() {
        const pending = session.pendingMomo(), intent = session.intent();
        $("momoPanel").hidden = state.method !== "MoMo" && !pending;
        $("createMomoButton").disabled = locked() || !session.loaded || (pending ? !["Ready", "Completed"].includes(session.order?.status) : Boolean(session.block()));
        $("createMomoButton").textContent = pending ? "Lấy lại thông tin MoMo đang chờ" : "Tạo giao dịch MoMo";
        let html = pending ? `<p>Phiếu MoMo #${pending.paymentId} đang chờ · ${money(pending.amount)}. Kiểm tra kết quả mỗi 3 giây.</p>` : "<p>Chưa có giao dịch MoMo đang chờ.</p>";
        if (intent?.isMock) html += '<p class="mock-label">CHẾ ĐỘ GIẢ LẬP — Không thu tiền thật, không có QR thanh toán thật.</p>';
        else if (intent) {
            const redirect = safeUrl(intent.redirectUrl), qr = safeUrl(intent.qrCodeUrl);
            html += `${qr ? `<img class="momo-qr" src="${esc(qr)}" alt="QR MoMo do nhà cung cấp trả về" referrerpolicy="no-referrer">` : ""}
                ${redirect ? `<a class="secondary-action" href="${esc(redirect)}" target="_blank" rel="noopener noreferrer">Mở trang thanh toán MoMo</a>` : "<p>Nhà cung cấp chưa trả đường dẫn thanh toán.</p>"}`;
        }
        if (pending && pending.amount !== session.order?.totalAmount) html += '<p class="is-error">Số tiền giao dịch khác tổng đơn mới nhất. Cần đối chiếu trước khi thu.</p>';
        $("momoInfo").innerHTML = html; $("mockActions").hidden = !intent?.isMock;
        $("mockSuccessButton").disabled = locked() || !session.loaded || !["Ready", "Completed"].includes(session.order?.status) || pending?.amount !== session.order?.totalAmount;
        $("mockFailButton").disabled = locked() || !session.loaded;
    }
    function renderLedger() {
        html("paymentRows", session.ledger.items.map((p) => `<tr><td>#${p.paymentId}</td><td><button class="link-button" data-select-order="${p.orderId}" type="button"${locked() ? " disabled" : ""}>ORD-${p.orderId}</button></td>
            <td>${esc(methods[p.paymentMethod] || p.paymentMethod)}</td><td>${esc(labels[p.status] || p.status)}</td><td>${money(p.amount)}</td><td>${esc(p.transactionCode)}</td><td>${time(p.paidAt)}</td></tr>`).join("") || '<tr><td colspan="7">Không có phiếu thu theo bộ lọc.</td></tr>');
        $("ledgerPage").textContent = `Trang ${session.ledger.page} · ${session.ledger.total} phiếu`;
        $("ledgerPrev").disabled = locked() || session.ledger.page <= 1;
        $("ledgerNext").disabled = locked() || session.ledger.page * session.ledger.pageSize >= session.ledger.total;
    }
    function render() {
        renderQueue(); renderDetail(); renderLedger();
        $("pendingPayment").hidden = !session.pending;
        $("pendingDescription").textContent = session.pending ? `Yêu cầu ${session.pending.kind === "refund" ? "hoàn tiền" : "thanh toán"} của ORD-${session.pending.orderId} chưa được đối chiếu xong. Không gửi lại lệnh. Hãy tải phiếu thu mới nhất.` : "";
        $("reviewPaymentButton").disabled = session.busy || session.loading;
        $("ackPaymentButton").hidden = !session.pending?.reviewed;
        $("ackPaymentButton").disabled = session.busy || session.loading || !session.loaded;
        $("refreshPaymentsButton").disabled = session.busy || session.loading;
    }
    async function run(action, message = "") {
        try { const promise = action(); render(); const result = await promise; if (message) notice(message); return result; }
        catch (error) {
            notice(error.message, true);
            if ([403, 409].includes(error.status) && !session.pending && session.hasAccess()) {
                try { await refresh(); } catch (_) { /* Giữ thông báo của thao tác vừa bị từ chối. */ }
            }
        }
        finally { render(); }
    }
    async function refresh() { await session.refresh(state.filters); state.lastRefresh = Date.now(); }
    async function select(id) {
        await session.select(id);
        if (session.order) {
            const url = new URL(window.location.href); url.searchParams.set("orderId", session.order.orderId); window.history.replaceState(null, "", url);
            $("transactionCode").value = "";
        }
    }
    function openConfirm(kind, data = {}) {
        if (locked() || !session.loaded) return;
        state.modal = { kind, data, expected: snapshot(session.order) };
        const p = session.payments.find((row) => row.paymentId === data.paymentId);
        $("confirmTitle").textContent = kind === "refund" ? "Hoàn toàn bộ khoản thu" : kind === "mock" ? "Giả lập kết quả MoMo" : kind === "momo" ? "Tạo / lấy giao dịch MoMo" : "Xác nhận khoản tiền đã nhận";
        $("confirmDescription").textContent = kind === "refund" ? `Hoàn ${money(p?.amount)} của phiếu #${data.paymentId}. Đơn sẽ mở lại chờ thu; hãy trả tiền cho khách theo quy trình của quán.`
            : kind === "mock" ? `Đánh dấu giao dịch thử ${data.success ? "thành công" : "thất bại"}. Không có tiền thật.`
            : kind === "momo" ? `ORD-${session.order.orderId} · ${money(session.order.totalAmount)}. Tạo giao dịch chưa đóng đơn.`
            : `ORD-${session.order.orderId} · ${methods[data.method]} · ${money(session.order.totalAmount)}. Chỉ xác nhận khi đã nhận đủ tiền.`;
        $("refundReasonLabel").hidden = kind !== "refund"; $("refundReason").required = kind === "refund"; $("refundReason").value = "";
        $("confirmDialog").showModal();
    }
    function receipt(order, tableBill = false) {
        const paid = tableBill ? null : session.payments.find((p) => p.status === "Paid");
        $("printReceipt").innerHTML = `<h1>${tableBill ? "TẠM TÍNH THEO BÀN" : paid ? "PHIẾU THANH TOÁN" : "PHIẾU TẠM TÍNH"}</h1>
            <p>Restaurant Food Manager · Bàn ${esc(tableBill ? order.tableNumber : tableName(order.tableId))}</p>
            <p>${tableBill ? order.orderIds.map((id) => `ORD-${id}`).join(", ") : `ORD-${order.orderId}`} · In lúc ${time(new Date().toISOString())}</p>
            ${tableBill ? '<p>Tổng hợp đơn đang mở. Thu tiền theo từng đơn.</p>' : `<p>${esc(labels[order.status] || order.status)}</p>`}
            ${itemsHtml(order.items, tableBill)}<div class="payment-total">${totals(order)}</div>
            ${(tableBill ? order.notes : [order.note]).filter(Boolean).map((note) => `<p>Ghi chú: ${esc(note)}</p>`).join("")}
            ${paid ? `<p>Phiếu #${paid.paymentId} · ${esc(methods[paid.paymentMethod] || paid.paymentMethod)} · ${money(paid.amount)}<br>Mã ${esc(paid.transactionCode)}<br>Thu lúc ${time(paid.paidAt)}</p>` : ""}`;
        $("receiptPreview").innerHTML = $("printReceipt").innerHTML;
        state.printing = true; $("receiptDialog").showModal();
    }
    $("billSearchInput").addEventListener("input", renderQueue); $("queueStatus").addEventListener("change", renderQueue);
    document.addEventListener("click", (event) => {
        const selectButton = event.target.closest("[data-select-order]");
        if (selectButton && !locked()) run(() => select(Number(selectButton.dataset.selectOrder)));
        const method = event.target.closest("[data-method]"); if (method && !locked()) { state.method = method.dataset.method; renderDetail(); }
        const refund = event.target.closest("[data-refund]"); if (refund) openConfirm("refund", { paymentId: Number(refund.dataset.refund) });
    });
    $("confirmPaymentButton").addEventListener("click", () => openConfirm("direct", { method: state.method, transactionCode: $("transactionCode").value }));
    $("createMomoButton").addEventListener("click", () => openConfirm("momo"));
    $("mockSuccessButton").addEventListener("click", () => openConfirm("mock", { success: true }));
    $("mockFailButton").addEventListener("click", () => openConfirm("mock", { success: false }));
    $("cancelConfirmButton").addEventListener("click", () => $("confirmDialog").close());
    $("confirmForm").addEventListener("submit", (event) => {
        event.preventDefault(); const modal = state.modal; if (!modal || locked()) return;
        modal.data.reason = $("refundReason").value; $("confirmDialog").close(); state.modal = null;
        run(async () => {
            const result = await session.command(modal.kind, modal.data, modal.expected);
            if (modal.kind === "direct") notice(`Đã ghi nhận phiếu #${result.paymentId} · ${money(result.amount)}.`);
            else if (modal.kind === "refund") notice("Đã ghi nhận hoàn tiền và mở lại đơn chờ thu.");
            else notice(modal.kind === "momo" ? "Đã tải giao dịch MoMo. Đang chờ xác nhận kết quả." : "Đã tải kết quả giao dịch giả lập.");
            await refresh();
        });
    });
    $("refreshPaymentsButton").addEventListener("click", () => run(refresh));
    $("reviewPaymentButton").addEventListener("click", () => run(async () => {
        const result = await session.reviewPending(); await refresh();
        notice(result?.resolved ? "Đã tìm thấy kết quả yêu cầu trong lịch sử." : "Đã tải phiếu thu. Hãy đối chiếu kết quả trước khi tiếp tục.");
    }));
    $("ackPaymentButton").addEventListener("click", () => run(() => { session.acknowledgeReview(); notice("Đã xác nhận đối chiếu. Kiểm tra khoản thu trước khi tạo yêu cầu mới."); }));
    $("paymentFilters").addEventListener("submit", (event) => {
        event.preventDefault(); if (locked()) return;
        const filters = Object.fromEntries(new FormData(event.currentTarget));
        run(async () => { session.loading = true; try { await session.loadLedger(filters, 1); state.filters = filters; } finally { session.loading = false; } });
    });
    for (const [id, delta] of [["ledgerPrev", -1], ["ledgerNext", 1]]) $(id).addEventListener("click", () => run(async () => {
        session.loading = true; try { await session.loadLedger(state.filters, session.ledger.page + delta); } finally { session.loading = false; }
    }));
    $("printBillButton").addEventListener("click", () => run(async () => { await session.select(session.order.orderId); render(); receipt(session.order); }));
    $("tableBillButton").addEventListener("click", () => run(async () => {
        const bill = await window.restaurantApi.get(`/orders/table/${session.order.tableId}/bill`); receipt(bill, true);
    }));
    $("closeReceiptButton").addEventListener("click", () => $("receiptDialog").close());
    $("receiptDialog").addEventListener("close", () => { state.printing = false; });
    $("printReceiptButton").addEventListener("click", () => window.print());
    let poll;
    function startPoll() {
        window.clearInterval(poll);
        poll = window.setInterval(() => {
            if (!session.hasAccess() || !auth.getToken() || document.hidden || locked() || $("confirmDialog").open || state.printing) return;
            if (Date.now() - state.lastRefresh < (session.pendingMomo() ? 2500 : 9500)) return;
            state.lastRefresh = Date.now(); run(refresh);
        }, 3000);
    }
    window.addEventListener("pagehide", () => window.clearInterval(poll)); window.addEventListener("pageshow", startPoll);
    auth.ready.then(() => run(async () => {
        session.assertAccess(); await refresh();
        const params = new URLSearchParams(window.location.search);
        const id = session.pending?.orderId || Number(params.get("orderId")) || session.orders.find((o) => o.tableId === Number(params.get("tableId")))?.orderId || session.orders[0]?.orderId;
        if (id) await select(id); render(); startPoll();
    }));
})();
