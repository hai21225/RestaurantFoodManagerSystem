(function () {
    const $ = (selector) => document.querySelector(selector);
    const escapeHtml = (value) => String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char]));
    const auth = window.restaurantAuth;
    const kitchen = window.restaurantKitchen;
    const session = new kitchen.KitchenSession({ api: window.restaurantApi, auth, storage: window.sessionStorage });
    const stages = [{ value: "pending", label: "Chờ nhận" }, { value: "preparing", label: "Đang chế biến" }, { value: "ready", label: "Đã xong" }];
    const actionNames = { receive: "Nhận món", prepared: "Báo đã làm", shortage: "Báo thiếu / hỏng", cancel: "Hủy món", reopen: "Mở lại món" };
    const statusName = (status) => ({ Pending: "Chờ nhận", SentToKitchen: "Đã gửi bếp", Accepted: "Phiếu đã nhận", Preparing: "Đang chế biến", Ready: "Sẵn sàng phục vụ", Completed: "Đã xong", Cancelled: "Đã hủy", Paid: "Đã thanh toán" }[status] || status);
    const stage = (ticket) => ["Ready", "Completed"].includes(ticket.status) ? "ready" : ticket.status === "Preparing" ? "preparing" : "pending";
    const view = { status: "pending", search: "", priority: "all", action: null, version: "" };
    const dialog = $("#kitchenActionDialog");
    const locked = () => !session.hasAccess() || !session.loaded || session.loading || session.busy || Boolean(session.pending);
    function announce(text, error = false) { $("#kitchenAnnouncement").textContent = text; $("#kitchenAnnouncement").classList.toggle("is-error", error); }
    function dateLabel(value) {
        if (!value) return "";
        const utc = /(?:Z|[+-]\d\d:\d\d)$/i.test(value) ? value : `${value}Z`;
        return new Date(utc).toLocaleString("vi-VN");
    }
    function filtered() {
        if (!session.hasAccess()) return [];
        return session.tickets.filter((ticket) => (view.priority === "all" || (view.priority === "high" ? ticket.priority > 0 : ticket.priority === 0))
            && [ticket.tableNumber, ticket.waiterName, ...ticket.items.map((item) => item.foodName)].join(" ").toLocaleLowerCase("vi-VN").includes(view.search));
    }
    function renderControls() {
        const isLocked = locked();
        $("#refreshKitchenButton").disabled = session.busy || session.loading;
        $("#refreshKitchenButton").textContent = session.loading ? "Đang tải…" : "Làm mới";
        document.querySelectorAll("[data-kitchen-action]").forEach((button) => { button.disabled = isLocked; });
        $("#kitchenActionForm").querySelectorAll("input, textarea, select, button").forEach((input) => { input.disabled = session.busy || session.loading || Boolean(session.pending); });
        document.querySelectorAll("[data-close-kitchen-action]").forEach((button) => { button.disabled = session.busy; });
        $("#kitchenActionConfirm").disabled = isLocked;
        if (view.action) {
            const current = session.find(view.action.ticketId, view.action.itemId);
            $("#kitchenActionConfirm").disabled = isLocked || !session.allowed(view.action.action, current.ticket, current.item)
                || (current.item && view.action.expected !== kitchen.snapshot(current.item));
        }
        $("#kitchenActionConfirm").textContent = session.busy ? "Đang xử lý…" : "Xác nhận";
        $("#kitchenPendingNotice").hidden = !session.pending;
        $("#reviewKitchenRequest").disabled = session.busy || session.loading;
        $("#acknowledgeKitchenRequest").hidden = !session.pending?.reviewed;
        $("#acknowledgeKitchenRequest").disabled = !session.loaded || session.busy || session.loading;
        if (session.pending) {
            const request = session.pending;
            const amount = ["prepared", "shortage"].includes(request.action) ? ` · báo làm được ${request.body.preparedQuantity}/${request.before.quantity} phần` : "";
            $("#pendingKitchenDescription").textContent = `Phiếu #${request.ticketId} · ${actionNames[request.action] || "Xử lý món"}: ${request.before.name}${amount}. `
                + (request.accepted ? "Máy chủ đã chấp nhận, nhưng chưa tải lại được kết quả. Nút dưới chỉ tải lại, không gửi món lần nữa."
                    : "Chưa biết máy chủ đã thực hiện hay chưa. Không tự gửi lại. Tải phiếu, đối chiếu số phần và trạng thái rồi mới tiếp tục.");
        }
    }
    function renderTicket(ticket, readOnly = false) {
        const orderIsClosed = kitchen.orderClosed(ticket.order.status);
        const doneCount = ticket.items.filter((item) => kitchen.isDone(item.status)).reduce((sum, item) => sum + item.quantity, 0);
        const cancelledCount = ticket.items.filter((item) => item.status === "Cancelled").reduce((sum, item) => sum + item.quantity, 0);
        return `<article class="kitchen-ticket" data-kitchen-ticket="${ticket.kitchenOrderId}">
            <div class="ticket-topline"><div class="ticket-code"><strong>Phiếu #${ticket.kitchenOrderId}</strong><span>Đơn #${ticket.orderId} · Bàn ${escapeHtml(ticket.tableNumber)}</span></div><span class="status priority-${ticket.priority > 0 ? "high" : "normal"}">${ticket.priority > 0 ? `Ưu tiên ${ticket.priority}` : "Thường"}</span></div>
            <div class="ticket-meta"><span>Phục vụ: ${escapeHtml(ticket.waiterName || "Chưa có tên")}</span><span>${escapeHtml(dateLabel(ticket.sentAt))}</span></div>
            <p class="ticket-parent-status">Phiếu: ${escapeHtml(statusName(ticket.status))} · Đơn: ${escapeHtml(statusName(ticket.order.status))}</p>
            ${orderIsClosed ? '<p class="field-hint">Đơn đã kết thúc; các món chỉ được xem.</p>' : ""}
            <ul class="ticket-items">${ticket.items.map((item) => `<li class="kitchen-dish${kitchen.isDone(item.status) ? " is-complete" : ""}${item.status === "Cancelled" ? " is-cancelled" : ""}">
                <div class="ticket-item-content"><strong>${escapeHtml(item.foodName || "Món đã ngừng bán")}</strong><span>${escapeHtml(item.sizeName)} · ${item.quantity} phần</span><span class="ticket-item-status">${escapeHtml(statusName(item.status))}</span>
                ${item.note ? `<span>Ghi chú: ${escapeHtml(item.note)}</span>` : ""}
                ${item.toppings.length ? `<span>Topping mỗi phần: ${item.toppings.map((t) => `${escapeHtml(t.toppingName)} × ${t.quantity}`).join(", ")}</span>` : ""}
                ${item.cancelReason ? `<span class="kitchen-cancel-reason">Lý do hủy: ${escapeHtml(item.cancelReason)}</span>` : ""}</div>
                ${readOnly ? "" : `<div class="kitchen-item-actions">${Object.entries(actionNames).filter(([action]) => session.allowed(action, ticket, item)).map(([action, name]) => `<button type="button" class="ticket-action${action === "receive" ? " primary" : ""}" data-kitchen-action="${action}" data-ticket-id="${ticket.kitchenOrderId}" data-item-id="${item.id}" ${locked() ? "disabled" : ""}>${name}</button>`).join("")}</div>`}
            </li>`).join("") || '<li class="empty-column">Chưa tải được dòng món của phiếu.</li>'}</ul>
            <div class="ticket-progress">${doneCount} phần đã xong · ${cancelledCount} phần đã hủy</div>
        </article>`;
    }
    function render() {
        $("#kitchenAccessNote").textContent = session.hasAccess() ? "Nhận riêng từng món. POS chỉ được giảm/hủy phần chưa được bếp nhận." : "Chỉ nhân viên bếp và quản trị viên được xử lý phiếu.";
        const tickets = filtered();
        $("#kitchenSummary").innerHTML = [{ label: "Phiếu mở", count: tickets.length }, ...stages.map((s) => ({ label: s.label, count: tickets.filter((t) => stage(t) === s.value).length }))]
            .map((s) => `<div class="kitchen-summary-card"><span>${s.label}</span><strong>${s.count}</strong></div>`).join("");
        $("#priorityFilters").innerHTML = [{ value: "all", label: "Tất cả" }, { value: "high", label: "Ưu tiên" }, { value: "normal", label: "Thường" }].map((p) => `<button type="button" class="segment-button${p.value === view.priority ? " is-active" : ""}" data-kitchen-priority="${p.value}">${p.label}</button>`).join("");
        $("#kitchenStatusTabs").innerHTML = stages.map((s) => `<button type="button" class="status-tab${s.value === view.status ? " is-active" : ""}" aria-pressed="${s.value === view.status}" data-kitchen-stage="${s.value}"><strong>${s.label}</strong><span>${tickets.filter((t) => stage(t) === s.value).length}</span></button>`).join("");
        const version = JSON.stringify([tickets, session.receipt, view.status, auth.getUser()?.roles]);
        if (view.version !== version) {
            view.version = version;
            $("#kitchenBoard").innerHTML = stages.map((s) => `<section class="kitchen-column${s.value === view.status ? " is-active-status" : ""}" aria-labelledby="kitchen-column-${s.value}"><div class="kitchen-column-header"><h3 id="kitchen-column-${s.value}">${s.label}</h3><span>${tickets.filter((t) => stage(t) === s.value).length} phiếu</span></div><div class="ticket-list">${tickets.filter((t) => stage(t) === s.value).map((ticket) => renderTicket(ticket)).join("") || '<p class="empty-column">Chưa có phiếu nào.</p>'}</div></section>`).join("");
            const receiptVisible = session.hasAccess() && session.receipt && !session.tickets.some((ticket) => ticket.kitchenOrderId === session.receipt.kitchenOrderId);
            $("#kitchenReceipt").hidden = !receiptVisible;
            $("#kitchenReceiptContent").innerHTML = receiptVisible ? renderTicket(session.receipt, true) : "";
        }
        renderControls();
    }
    async function refresh(quiet = false, focusId = session.pending?.ticketId || session.receipt?.kitchenOrderId) {
        if (session.busy || session.loading) return;
        const task = session.refresh(focusId);
        renderControls();
        try { await task; if (!quiet) announce("Đã tải phiếu bếp mới nhất từ máy chủ."); }
        catch (error) { announce(error.message, true); }
        render();
    }
    async function run(action, ticketId, itemId, data, expected) {
        const task = session.perform(action, ticketId, itemId, data, expected);
        renderControls();
        try {
            const result = await task;
            dialog.close();
            if (result.ticket && !kitchen.ticketClosed(result.ticket.status)) view.status = stage(result.ticket);
            announce(`${actionNames[action]}: đã tải kết quả từ máy chủ. Đơn và POS dùng cùng trạng thái món.`);
        } catch (error) {
            const errorMessage = error.message;
            if (session.pending) { dialog.close(); announce(`${errorMessage} Hãy đối chiếu yêu cầu bên dưới trước khi thao tác tiếp.`, true); }
            else {
                if ([403, 404, 409].includes(error.status)) await refresh(true, ticketId);
                $("#kitchenActionMessage").textContent = errorMessage;
                announce(errorMessage, true);
            }
        }
        render();
    }
    async function openAction(action, ticketId, itemId) {
        if (locked()) return;
        const before = session.find(ticketId, itemId);
        if (!before.item) return;
        if (action === "receive") { run(action, ticketId, itemId, {}, kitchen.snapshot(before.item)); return; }
        await refresh(true, ticketId);
        const { ticket, item } = session.find(ticketId, itemId);
        if (locked() || !session.allowed(action, ticket, item)) { announce("Món/phiếu đã đổi trạng thái. Hãy kiểm tra dữ liệu mới nhất.", true); return; }
        view.action = { action, ticketId, itemId, expected: kitchen.snapshot(item), quantity: item.quantity, status: item.status };
        $("#kitchenActionForm").reset();
        $("#kitchenActionTitle").textContent = actionNames[action];
        $("#kitchenActionDescription").textContent = `${item.foodName} · ${item.sizeName} · ${item.quantity} phần · Bàn ${ticket.tableNumber}`;
        $("#kitchenQuantityField").hidden = !["prepared", "shortage"].includes(action);
        $("#kitchenPreparedQuantity").required = ["prepared", "shortage"].includes(action);
        $("#kitchenPreparedQuantity").min = action === "shortage" ? "0" : "1";
        $("#kitchenPreparedQuantity").max = String(action === "shortage" ? item.quantity - 1 : item.quantity);
        $("#kitchenPreparedQuantity").value = action === "shortage" ? "0" : String(item.quantity);
        $("#kitchenShortageReasonField").hidden = action !== "shortage";
        $("#kitchenCancelReasonField").hidden = action !== "cancel";
        $("#kitchenDeactivateField").hidden = action !== "shortage";
        $("#kitchenActionReason").required = ["shortage", "cancel", "reopen"].includes(action);
        $("#kitchenActionMessage").textContent = "";
        updateHint(); renderControls();
        dialog.showModal();
        (["prepared", "shortage"].includes(action) ? $("#kitchenPreparedQuantity") : $("#kitchenActionReason")).focus();
    }
    function updateHint() {
        const action = view.action;
        if (!action) return;
        $("#kitchenActionHint").textContent = action.action === "prepared" ? "Chỉ nhập số phần vừa làm thêm của dòng này. Phần đã báo xong trước đó nằm ở dòng riêng. Máy chủ tách phần xong và giữ phần còn lại."
            : action.action === "shortage" ? "Phần làm được chuyển thành đã xong; phần thiếu bị hủy và ghi hao hụt, không hoàn kho. Ngừng bán sẽ gỡ món khỏi menu POS."
            : action.action === "reopen" ? "Chỉ mở lại khi đơn chưa thanh toán/hủy và phiếu chưa đóng. Máy chủ đưa món về đang chế biến; không trừ kho lần nữa."
            : action.status === "Pending" && $("#kitchenCancelReason").value === "CustomerChange" ? "Khách thay đổi khi món chưa nhận: máy chủ hoàn kho cho phần bị hủy."
            : "Phần đã bắt đầu chế biến hoặc hủy vì sự cố không hoàn kho; máy chủ quyết định việc ghi nhận hao hụt.";
    }
    $("#kitchenBoard").addEventListener("click", (event) => {
        const button = event.target.closest("[data-kitchen-action]");
        if (button) openAction(button.dataset.kitchenAction, Number(button.dataset.ticketId), Number(button.dataset.itemId));
    });
    $("#kitchenActionForm").addEventListener("submit", (event) => {
        event.preventDefault();
        if (locked() || !view.action || !event.target.reportValidity()) return;
        const a = view.action;
        run(a.action, a.ticketId, a.itemId, { preparedQuantity: Number($("#kitchenPreparedQuantity").value), reason: $("#kitchenActionReason").value,
            reasonCode: a.action === "shortage" ? $("#kitchenShortageReason").value : $("#kitchenCancelReason").value,
            deactivateDish: $("#kitchenDeactivateDish").checked }, a.expected);
    });
    document.querySelectorAll("[data-close-kitchen-action]").forEach((button) => button.addEventListener("click", () => { if (!session.busy) dialog.close(); }));
    dialog.addEventListener("cancel", (event) => { if (session.busy) event.preventDefault(); });
    $("#kitchenCancelReason").addEventListener("change", updateHint);
    $("#refreshKitchenButton").addEventListener("click", () => refresh());
    $("#kitchenSearch").addEventListener("input", (event) => { view.search = event.target.value.trim().toLocaleLowerCase("vi-VN"); render(); });
    $("#priorityFilters").addEventListener("click", (event) => { const button = event.target.closest("[data-kitchen-priority]"); if (button) { view.priority = button.dataset.kitchenPriority; render(); } });
    $("#kitchenStatusTabs").addEventListener("click", (event) => { const button = event.target.closest("[data-kitchen-stage]"); if (button) { view.status = button.dataset.kitchenStage; render(); } });
    $("#reviewKitchenRequest").addEventListener("click", async () => {
        const task = session.reviewPending(); renderControls();
        try {
            await task;
            if (session.receipt && !kitchen.ticketClosed(session.receipt.status)) view.status = stage(session.receipt);
            announce(session.pending ? "Đã tải phiếu. Đối chiếu số phần và trạng thái trên phiếu rồi xác nhận tiếp tục." : "Đã tải kết quả; không gửi lại lệnh.");
        }
        catch (error) { announce(error.message, true); }
        render();
    });
    $("#acknowledgeKitchenRequest").addEventListener("click", () => {
        try { session.acknowledgeReview(); announce("Đã đối chiếu. Các thao tác tiếp theo sẽ dùng số phần và trạng thái mới nhất."); }
        catch (error) { announce(error.message, true); }
        render();
    });
    window.addEventListener("restaurant:auth-changed", render);
    render();
    Promise.resolve(auth.ready).then(() => refresh());
    let poll;
    function startPolling() {
        window.clearInterval(poll);
        poll = window.setInterval(() => { if (!document.hidden && !dialog.open && !session.pending && !document.activeElement?.closest("input, textarea, select")) refresh(true, null); }, 5000);
    }
    startPolling();
    window.addEventListener("pageshow", (event) => { startPolling(); if (event.persisted) refresh(true); });
    window.addEventListener("pagehide", () => window.clearInterval(poll));
})();
