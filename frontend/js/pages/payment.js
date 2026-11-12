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
    const state = { method: "Cash", filters: {}, modal: null, lastRefresh: 0, printing: false, readFailureNotice: false, workspace: "checkout" };
    const methodIcons = {
        Cash: '<rect x="3" y="5" width="18" height="14" rx="2"/><circle cx="12" cy="12" r="3"/><path d="M6 9h.01M18 15h.01"/>',
        Card: '<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M3 10h18M7 15h4"/>',
        Bank: '<path d="m3 9 9-6 9 6H3Zm2 0v10m5-10v10m4-10v10m5-10v10M2 21h20"/>',
        EWallet: '<path d="M20 8V6a2 2 0 0 0-2-2H6a3 3 0 0 0 0 6h14v10H6a3 3 0 0 1-3-3V7"/><path d="M16 13h5v4h-5z"/>',
        Transfer: '<path d="M4 7h16m-4-4 4 4-4 4M20 17H4m4-4-4 4 4 4"/>',
        MoMo: '<rect x="3" y="3" width="18" height="18" rx="5"/><path d="M7 16V8l5 5 5-5v8"/>'
    };
    // Queue/ledger success is independent of session.loaded, which describes the selected order.
    const reads = { queue: { loaded: false, loading: false, failed: false }, ledger: { loaded: false, loading: false, failed: false } };
    const htmlCache = new Map();
    function html(id, value) { if (htmlCache.get(id) !== value) { $(id).innerHTML = value; htmlCache.set(id, value); } }
    const tableName = (id) => session.tables.find((t) => t.tableId === id)?.tableNumber || `#${id}`;
    const fetching = () => session.busy || session.loading || reads.queue.loading || reads.ledger.loading;
    const locked = () => fetching() || Boolean(session.pending);
    const ready = (key) => reads[key].loaded && !reads[key].loading && !reads[key].failed;
    const stale = () => reads.queue.failed || reads.ledger.failed;
    const paymentLocked = () => locked() || stale();
    function setWorkspace(workspace, { focus = false, updateHash = false } = {}) {
        state.workspace = workspace === "ledger" ? "ledger" : "checkout";
        const ledger = state.workspace === "ledger";
        $("paymentDesk").hidden = ledger;
        $("paymentLedger").hidden = !ledger;
        for (const [id, selected] of [["checkoutTab", !ledger], ["ledgerTab", ledger]]) {
            $(id).setAttribute("aria-selected", String(selected));
            $(id).tabIndex = selected ? 0 : -1;
            if (focus && selected) $(id).focus();
        }
        if (updateHash) {
            const url = new URL(window.location.href);
            url.hash = ledger ? "paymentLedger" : "paymentCheckout";
            window.history.replaceState(null, "", url);
        }
    }
    function readMessage(key) {
        const read = reads[key], label = key === "queue" ? "danh sách đơn" : "danh sách phiếu thu";
        if (read.loading) return `Đang tải ${label}…${read.loaded ? " Dữ liệu lần trước chỉ để tham khảo." : ""}`;
        if (read.failed) return `Không tải được ${label}.${read.loaded ? " Dữ liệu lần trước chỉ để tham khảo." : ""} Bấm Làm mới để thử lại.`;
        return `Chưa tải ${label}. Bấm Làm mới để tải dữ liệu.`;
    }
    function notice(message = "", error = false) {
        state.readFailureNotice = false;
        $("paymentNotice").textContent = message;
        $("paymentNotice").classList.toggle("is-error", error);
        $("paymentNotice").hidden = !message;
    }
    function totals(order) {
        return [["Tạm tính", order?.subtotal], ["Giảm giá", order?.discountAmount], ["Phí dịch vụ", order?.serviceChargeAmount], ["Thuế", order?.taxAmount], ["Tổng đơn", order?.totalAmount]]
            .map(([label, value], i) => `<div${i === 4 ? ' class="grand-total"' : ""}><span>${label}</span><strong>${order ? money(value) : "—"}</strong></div>`).join("");
    }
    function itemsHtml(items, tableBill = false) {
        return `<div class="invoice-item-head" aria-hidden="true"><span>Món / Kích cỡ</span><span>SL</span><span>${tableBill ? "Thành tiền" : "Đơn giá"}</span></div><ul class="bill-items">${items.map((i) => `<li${i.status === "Cancelled" ? ' class="cancelled-item"' : ""}>
            <div class="bill-item-description"><strong>${esc(i.foodName)}</strong><span>${esc(i.sizeName)} · Đã gồm topping</span>
            ${(i.toppings || []).length ? `<span>${i.toppings.map((t) => `${esc(t.toppingName)} × ${esc(t.quantity)}`).join(", ")}</span>` : ""}
            ${i.note ? `<span>Ghi chú: ${esc(i.note)}</span>` : ""}${i.cancelReason ? `<span>Lý do hủy: ${esc(i.cancelReason)}</span>` : ""}</div>
            <div class="bill-item-quantity"><strong>× ${esc(i.quantity)}</strong></div><div class="bill-item-amount"><strong>${money(tableBill ? i.lineTotal : i.unitPrice)}</strong><span>${tableBill ? "Thành tiền" : "Đơn giá"}</span></div>
            ${tableBill ? "" : `<span class="bill-item-state">${esc(labels[i.status] || i.status)}</span>`}</li>`).join("")}</ul>`;
    }
    function renderQueue() {
        const term = $("billSearchInput").value.trim().toLocaleLowerCase("vi"), filter = $("queueStatus").value;
        const orders = session.orders.filter((o) => (filter !== "ready" || ["Ready", "Completed"].includes(o.status))
            && (filter !== "kitchen" || !["Ready", "Completed"].includes(o.status))
            && `${tableName(o.tableId)} ORD-${o.orderId}`.toLocaleLowerCase("vi").includes(term));
        $("billCount").textContent = ready("queue") ? `${orders.length} đơn` : "— đơn";
        const queueStatus = ready("queue") ? "" : `<p class="empty-state${reads.queue.failed ? " is-error" : ""}" role="status">${readMessage("queue")}</p>`;
        html("billList", queueStatus + (orders.length ? orders.map((o) => `<button class="bill-button${o.orderId === session.order?.orderId ? " is-selected" : ""}"
            type="button" data-select-order="${o.orderId}"${locked() || !ready("queue") ? " disabled" : ""}>
            <div class="bill-button-top"><strong>Bàn ${esc(tableName(o.tableId))}</strong><span>${esc(labels[o.status] || o.status)}</span></div>
            <span>ORD-${o.orderId}</span><div class="bill-button-bottom"><span>${time(o.createdAt)}</span><strong>${money(o.totalAmount)}</strong></div></button>`).join("")
            : ready("queue") ? '<p class="empty-state">Không có đơn phù hợp. Mở tab Phiếu thu để xem đơn đã thanh toán.</p>' : ""));
        const done = session.orders.filter((o) => ["Ready", "Completed"].includes(o.status)).length;
        html("paymentSummary", [["Đơn đã xong bếp", ready("queue") ? done : "—"], ["Đơn chờ bếp", ready("queue") ? session.orders.length - done : "—"], ["Phiếu thu theo bộ lọc", ready("ledger") ? session.ledger.total : "—"], ["Bàn đang chọn", session.order ? tableName(session.order.tableId) : "—"]]
            .map(([label, value]) => `<article class="payment-summary-card"><span>${label}</span><strong>${esc(value)}</strong></article>`).join(""));
    }
    function renderDetail() {
        const order = session.order;
        $("selectedBillStatus").textContent = order ? labels[order.status] || order.status : "Chưa chọn";
        $("selectedBillStatus").setAttribute("data-status", order?.status || "");
        for (const id of ["invoiceTotalsSection", "invoicePrintActions", "paymentMethodSection", "orderPaymentHistory"]) $(id).hidden = !order;
        const tableStatuses = { Available: "Trống", Occupied: "Có khách", Maintenance: "Bảo trì", Reserved: "Đã đặt" };
        const tableStatus = session.tables.find((t) => t.tableId === order?.tableId)?.status;
        html("billDetail", order ? `<div class="bill-heading"><strong>Bàn ${esc(tableName(order.tableId))} · ORD-${order.orderId}</strong>
            <span>Mở lúc ${time(order.createdAt)}</span><span>Trạng thái bàn: ${esc(tableStatuses[tableStatus] || tableStatus || "—")}</span>
            ${order.note ? `<p>Ghi chú hóa đơn: ${esc(order.note)}</p>` : ""}${!session.loaded ? '<p class="is-error">Chi tiết lần trước chỉ để tham khảo. Bấm Làm mới để tải lại trước khi thu tiền.</p>' : ""}</div>${itemsHtml(order.items)}` : `<p class="empty-state">${reads.queue.failed ? "Không tải được danh sách đơn. Bấm Làm mới để thử lại." : reads.queue.loading ? "Đang tải danh sách đơn…" : "Chọn đơn để xem món và lịch sử thanh toán."}</p>`);
        html("paymentTotals", totals(order)); $("paymentBlock").textContent = stale() ? "Dữ liệu chưa được làm mới đầy đủ. Bấm Làm mới trước khi ghi nhận khoản thu." : session.block();
        $("confirmPaymentButton").disabled = paymentLocked() || Boolean(session.block()) || state.method === "MoMo";
        $("confirmPaymentButton").textContent = session.busy ? "Đang xử lý…" : "Xác nhận đã nhận tiền";
        $("transactionCode").disabled = locked() || state.method === "MoMo";
        $("printBillButton").disabled = paymentLocked() || !session.loaded;
        $("tableBillButton").disabled = paymentLocked() || !session.loaded || ["Paid", "Cancelled"].includes(order?.status);
        html("methodList", Object.entries(methods).map(([value, label]) => `<button class="method-button${value === state.method ? " is-active" : ""}"
            aria-pressed="${value === state.method}" type="button" data-method="${value}"${locked() ? " disabled" : ""}><svg viewBox="0 0 24 24" aria-hidden="true">${methodIcons[value]}</svg><span>${label}</span><span class="method-check" aria-hidden="true">✓</span></button>`).join(""));
        $("paymentMethodLabel").textContent = methods[state.method];
        $("methodHint").textContent = state.method === "MoMo" ? "Tạo giao dịch và chờ xác nhận kết quả. Tạo mã chưa có nghĩa đã thu tiền."
            : "Chỉ xác nhận sau khi đã nhận tiền hoặc kiểm tra giao dịch bên ngoài. Phần mềm ghi nhận khoản thu.";
        html("orderPayments", session.payments.length ? session.payments.map((p) => `<article class="payment-record">
            <div><strong>Phiếu #${p.paymentId} · ${esc(labels[p.status] || p.status)}</strong><span>${esc(methods[p.paymentMethod] || p.paymentMethod)} · ${money(p.amount)}</span>
            <span>Mã: ${esc(p.transactionCode)}</span><span>Tạo: ${time(p.createdAt)} · Thu: ${time(p.paidAt)} · Người thu #${p.createdBy}</span>
            ${p.refundedAt ? `<span>Hoàn: ${time(p.refundedAt)}</span>` : ""}${p.note ? `<span>${esc(p.note)}</span>` : ""}</div>
            ${p.status === "Paid" && order?.status === "Paid" ? `<button class="secondary-action" type="button" data-refund="${p.paymentId}"${paymentLocked() || !session.loaded ? " disabled" : ""}>Hoàn toàn bộ phiếu #${p.paymentId}</button>` : ""}</article>`).join("") : `<p class="empty-state">${!order ? "Chọn đơn để xem phiếu thu." : !session.loaded ? "Chưa đọc được phiếu thu mới nhất. Bấm Làm mới để thử lại." : "Đơn chưa có phiếu thu."}</p>`);
        renderMomo();
    }
    function safeUrl(value) {
        try { const url = new URL(value); return ["http:", "https:"].includes(url.protocol) ? url.href : ""; } catch (_) { return ""; }
    }
    function renderMomo() {
        const pending = session.pendingMomo(), intent = session.intent();
        $("momoPanel").hidden = state.method !== "MoMo" && !pending;
        $("createMomoButton").disabled = paymentLocked() || !session.loaded || (pending ? !["Ready", "Completed"].includes(session.order?.status) : Boolean(session.block()));
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
        $("mockSuccessButton").disabled = paymentLocked() || !session.loaded || !["Ready", "Completed"].includes(session.order?.status) || pending?.amount !== session.order?.totalAmount;
        $("mockFailButton").disabled = paymentLocked() || !session.loaded;
    }
    function renderLedger() {
        const ledgerStatus = ready("ledger") ? "" : `<tr role="row" class="payment-empty-row"><td role="cell" colspan="7"><span class="${reads.ledger.failed ? "is-error" : ""}" role="status">${readMessage("ledger")}</span></td></tr>`;
        html("paymentRows", ledgerStatus + (session.ledger.items.map((p) => `<tr role="row"><td role="cell" data-label="Phiếu">#${p.paymentId}</td><td role="cell" data-label="Đơn"><button class="link-button" data-select-order="${p.orderId}" type="button"${locked() || !ready("ledger") ? " disabled" : ""}>ORD-${p.orderId}</button></td>
            <td role="cell" data-label="Phương thức">${esc(methods[p.paymentMethod] || p.paymentMethod)}</td><td role="cell" data-label="Trạng thái">${esc(labels[p.status] || p.status)}</td><td role="cell" data-label="Số tiền">${money(p.amount)}</td><td role="cell" data-label="Mã giao dịch">${esc(p.transactionCode)}</td><td role="cell" data-label="Thời điểm thu">${time(p.paidAt)}</td></tr>`).join("") || (ready("ledger") ? '<tr role="row" class="payment-empty-row"><td role="cell" colspan="7">Không có phiếu thu theo bộ lọc.</td></tr>' : "")));
        $("ledgerPage").textContent = ready("ledger") ? `Trang ${session.ledger.page} · ${session.ledger.total} phiếu` : "— phiếu";
        $("ledgerPrev").disabled = locked() || !ready("ledger") || session.ledger.page <= 1;
        $("ledgerNext").disabled = locked() || !ready("ledger") || session.ledger.page * session.ledger.pageSize >= session.ledger.total;
    }
    function render() {
        renderQueue(); renderDetail(); renderLedger();
        $("pendingPayment").hidden = !session.pending;
        $("pendingDescription").textContent = session.pending ? `Yêu cầu ${session.pending.kind === "refund" ? "hoàn tiền" : "thanh toán"} của ORD-${session.pending.orderId} chưa được đối chiếu xong. Không gửi lại lệnh. Hãy tải phiếu thu mới nhất.` : "";
        $("reviewPaymentButton").disabled = fetching();
        $("ackPaymentButton").hidden = !session.pending?.reviewed;
        $("ackPaymentButton").disabled = fetching() || !session.loaded;
        $("refreshPaymentsButton").disabled = fetching();
    }
    // Observe existing GET loaders without changing PaymentSession's API or business rules.
    for (const [key, method] of [["queue", "loadQueue"], ["ledger", "loadLedger"]]) {
        const load = session[method].bind(session);
        session[method] = async (...args) => {
            const read = reads[key]; read.loading = true; read.failed = false;
            try { const result = await load(...args); read.loaded = true; return result; }
            catch (error) { read.failed = true; throw error; }
            finally { read.loading = false; render(); }
        };
    }
    async function run(action, message = "") {
        try {
            const promise = action(); render(); const result = await promise;
            if (message) notice(message);
            else if (state.readFailureNotice && ready("queue") && ready("ledger")) notice();
            return result;
        }
        catch (error) {
            notice(error.message, true);
            state.readFailureNotice = stale();
            if ([403, 409].includes(error.status) && !session.pending && session.hasAccess()) {
                try { await refresh(); } catch (_) { /* Giữ thông báo của thao tác vừa bị từ chối. */ }
            }
        }
        finally { render(); }
    }
    async function refresh() { await session.refresh(state.filters); state.lastRefresh = Date.now(); }
    async function select(id, focusInvoice = false) {
        await session.select(id);
        if (session.order) {
            const url = new URL(window.location.href); url.searchParams.set("orderId", session.order.orderId); window.history.replaceState(null, "", url);
            $("transactionCode").value = "";
            $("orderPaymentHistory").open = session.order.status === "Paid";
            if (focusInvoice) setWorkspace("checkout", { updateHash: state.workspace === "ledger" });
            if (window.matchMedia?.("(max-width: 760px)").matches && (focusInvoice || url.hash !== "#paymentQueue")) $("paymentQueue").open = false;
            if (focusInvoice) $("paymentCheckout").scrollIntoView({ block: "start" });
        }
    }
    function openConfirm(kind, data = {}) {
        if (paymentLocked() || !session.loaded) return;
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
    async function receipt(order, tableBill = false) {
        const paid = tableBill ? null : session.payments.find((p) => p.status === "Paid");
        const settings = auth.hasRole("Admin") ? await window.restaurantApi.get("/management/settings") : null;
        $("printReceipt").innerHTML = `<h1>${tableBill ? "TẠM TÍNH THEO BÀN" : paid ? "PHIẾU THANH TOÁN" : "PHIẾU TẠM TÍNH"}</h1>
            ${settings ? `<h2>${esc(settings.restaurantName)}</h2><p>${esc(settings.address)}<br>${esc(settings.phone)}${settings.taxCode ? `<br>MST: ${esc(settings.taxCode)}` : ""}</p>` : ""}
            <p>Bàn ${esc(tableBill ? order.tableNumber : tableName(order.tableId))}</p>
            <p>${tableBill ? order.orderIds.map((id) => `ORD-${id}`).join(", ") : `ORD-${order.orderId}`} · In lúc ${time(new Date().toISOString())}</p>
            ${tableBill ? '<p>Tổng hợp đơn đang mở. Thu tiền theo từng đơn.</p>' : `<p>${esc(labels[order.status] || order.status)}</p>`}
            ${itemsHtml(order.items, tableBill)}<div class="payment-total">${totals(order)}</div>
            ${(tableBill ? order.notes : [order.note]).filter(Boolean).map((note) => `<p>Ghi chú: ${esc(note)}</p>`).join("")}
            ${paid ? `<p>Phiếu #${paid.paymentId} · ${esc(methods[paid.paymentMethod] || paid.paymentMethod)} · ${money(paid.amount)}<br>Mã ${esc(paid.transactionCode)}<br>Thu lúc ${time(paid.paidAt)}</p>` : ""}`;
        $("receiptPreview").innerHTML = $("printReceipt").innerHTML;
        state.printing = true; $("receiptDialog").showModal();
    }
    $("billSearchInput").addEventListener("input", renderQueue); $("queueStatus").addEventListener("change", renderQueue);
    for (const [id, workspace] of [["checkoutTab", "checkout"], ["ledgerTab", "ledger"]]) {
        $(id).addEventListener("click", () => setWorkspace(workspace, { updateHash: true }));
        $(id).addEventListener("keydown", (event) => {
            if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
            event.preventDefault();
            const next = event.key === "Home" ? "checkout" : event.key === "End" ? "ledger" : state.workspace === "ledger" ? "checkout" : "ledger";
            setWorkspace(next, { focus: true, updateHash: true });
        });
    }
    function followWorkspaceHash() {
        const hash = new URL(window.location.href).hash;
        setWorkspace(hash === "#paymentLedger" ? "ledger" : "checkout");
        if (hash === "#paymentQueue") $("paymentQueue").open = true;
    }
    window.addEventListener("hashchange", followWorkspaceHash);
    followWorkspaceHash();
    document.addEventListener("click", (event) => {
        const selectButton = event.target.closest("[data-select-order]");
        if (selectButton && !locked()) run(() => select(Number(selectButton.dataset.selectOrder), true));
        const method = event.target.closest("[data-method]"); if (method && !locked()) { state.method = method.dataset.method; renderDetail(); }
        const refund = event.target.closest("[data-refund]"); if (refund) openConfirm("refund", { paymentId: Number(refund.dataset.refund) });
    });
    $("confirmPaymentButton").addEventListener("click", () => openConfirm("direct", { method: state.method, transactionCode: $("transactionCode").value }));
    $("createMomoButton").addEventListener("click", () => openConfirm("momo"));
    $("mockSuccessButton").addEventListener("click", () => openConfirm("mock", { success: true }));
    $("mockFailButton").addEventListener("click", () => openConfirm("mock", { success: false }));
    $("cancelConfirmButton").addEventListener("click", () => $("confirmDialog").close());
    $("confirmForm").addEventListener("submit", (event) => {
        event.preventDefault(); const modal = state.modal; if (!modal || paymentLocked()) return;
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
    $("printBillButton").addEventListener("click", () => run(async () => { await session.select(session.order.orderId); render(); await receipt(session.order); }));
    $("tableBillButton").addEventListener("click", () => run(async () => {
        const bill = await window.restaurantApi.get(`/orders/table/${session.order.tableId}/bill`); await receipt(bill, true);
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
