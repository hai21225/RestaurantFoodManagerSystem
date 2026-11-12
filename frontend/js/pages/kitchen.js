(function () {
    const $ = (selector) => document.querySelector(selector);
    const escapeHtml = (value) => String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char]));
    const auth = window.restaurantAuth;
    const kitchen = window.restaurantKitchen;
    const timing = window.restaurantPreparationTiming;
    const session = new kitchen.KitchenSession({ api: window.restaurantApi, auth, storage: window.sessionStorage });
    const stages = [{ value: "pending", label: "Chờ nhận" }, { value: "preparing", label: "Đang chế biến" }, { value: "ready", label: "Đã xong" }];
    const actionNames = { receive: "Nhận món", prepared: "Báo đã làm", shortage: "Báo thiếu / hỏng", cancel: "Hủy món", reopen: "Mở lại món" };
    const statusName = (status) => ({ Pending: "Chờ nhận", SentToKitchen: "Đã gửi bếp", Accepted: "Phiếu đã nhận", Preparing: "Đang chế biến", Ready: "Sẵn sàng phục vụ", Completed: "Đã xong", Cancelled: "Đã hủy", Paid: "Đã thanh toán" }[status] || status);
    const stage = (ticket) => ["Ready", "Completed"].includes(ticket.status) ? "ready" : ticket.status === "Preparing" ? "preparing" : "pending";
    const view = { status: "pending", search: "", priority: "all", sort: "oldest", action: null, version: "", loadFailed: false };
    const dialog = $("#kitchenActionDialog");
    const printView = window.restaurantKitchenPrintController.create({ session, auth, refresh: (ticketId) => refresh(true, ticketId), onStateChange: () => renderControls() });
    const locked = () => !session.hasAccess() || !session.loaded || session.loading || session.busy || Boolean(session.pending);
    function announce(text, error = false) { $("#kitchenAnnouncement").textContent = text; $("#kitchenAnnouncement").classList.toggle("is-error", error); }
    function dateLabel(value) {
        const epoch = timing.parseUtc(value);
        return epoch === null ? "Chưa có mốc giờ" : new Date(epoch).toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" });
    }
    function filtered() {
        if (!session.hasAccess()) return [];
        return session.tickets.filter((ticket) => (view.priority === "all" || (view.priority === "high" ? ticket.priority > 0 : ticket.priority === 0))
            && [ticket.tableNumber, ticket.waiterName, ...ticket.items.map((item) => item.foodName)].join(" ").toLocaleLowerCase("vi-VN").includes(view.search))
            .sort((a, b) => timing.compareTickets(a, b, view.sort));
    }
    function laneItems(ticket, lane) {
        if (kitchen.orderClosed(ticket.order.status)) return lane === "ready" ? ticket.items : [];
        return ticket.items.filter((item) => (kitchen.isDone(item.status) || item.status === "Cancelled" ? "ready" : item.status === "Preparing" ? "preparing" : "pending") === lane);
    }
    function pendingItems() {
        if (!session.hasAccess()) return [];
        return session.tickets.filter((ticket) => !kitchen.orderClosed(ticket.order.status))
            .flatMap((ticket) => ticket.items.filter((item) => item.status === "Pending").map((item) => ({ ticket, item })));
    }
    function renderPendingAlert() {
        const rows = pendingItems(), notice = $("#kitchenWaitingAlert");
        notice.hidden = !rows.length;
        const oldest = rows.map((row) => row.ticket).sort((a, b) => timing.compareTickets(a, b))[0];
        const summary = session.loaded ? `${rows.length} món chưa nhận · ${rows.reduce((sum, row) => sum + row.item.quantity, 0)} phần`
            : "Đang kiểm tra lại món chưa nhận…";
        if ($("#kitchenWaitingCount").textContent !== summary) $("#kitchenWaitingCount").textContent = summary;
        $("#kitchenWaitingOldest").textContent = oldest ? `Phiếu gửi sớm nhất: ${dateLabel(oldest.sentAt)}` : "";
        $("#kitchenShowWaiting").disabled = !session.hasAccess();
    }
    function clock(start, scope) {
        const elapsed = timing.label(start);
        return `<span class="preparation-clock${elapsed ? "" : " is-unavailable"}" data-preparation-start="${escapeHtml(start || "")}" data-preparation-scope="${scope}" aria-live="off">${scope} · ${elapsed || "Chưa có mốc giờ"}</span>`;
    }
    function orderedDetails(item, orderIsClosed) {
        const ordered = timing.orderedForItem(item, []);
        if (!ordered || timing.label(ordered) === null) return "";
        return `<span class="ordered-time">Gọi lúc ${escapeHtml(dateLabel(ordered))}</span>${item.status === "Pending" && !orderIsClosed ? clock(ordered, "Chờ bếp nhận") : ""}`;
    }
    function renderPreparationTimes() {
        document.querySelectorAll("[data-preparation-start]").forEach((node) => {
            const elapsed = timing.label(node.dataset.preparationStart);
            node.textContent = !session.loaded ? "Thời gian chưa cập nhật" : `${node.dataset.preparationScope} · ${elapsed || "Chưa có mốc giờ"}`;
            node.classList.toggle("is-unavailable", !session.loaded || !elapsed);
        });
    }
    function renderControls() {
        const isLocked = locked();
        $("#refreshKitchenButton").disabled = session.busy || session.loading;
        $("#refreshKitchenButton").textContent = session.loading ? "Đang tải…" : "Làm mới";
        document.querySelectorAll("[data-kitchen-action]").forEach((button) => { button.disabled = isLocked || printView.isOpen() || printView.isBusy(); });
        document.querySelectorAll("[data-kitchen-print]").forEach((button) => { button.disabled = isLocked || printView.isOpen() || printView.isBusy(); });
        printView.syncControls();
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
    function renderTicket(ticket, readOnly = false, items = ticket.items, position = null) {
        const orderIsClosed = kitchen.orderClosed(ticket.order.status);
        const doneCount = ticket.items.filter((item) => kitchen.isDone(item.status)).reduce((sum, item) => sum + item.quantity, 0);
        const cancelledCount = ticket.items.filter((item) => item.status === "Cancelled").reduce((sum, item) => sum + item.quantity, 0);
        return `<article class="kitchen-ticket" data-kitchen-ticket="${ticket.kitchenOrderId}">
            <div class="ticket-topline"><div class="ticket-identity">${position ? `<span class="queue-position" title="Thứ tự phiếu trong cột này">${position}</span>` : ""}<div class="ticket-code"><strong>Bàn ${escapeHtml(ticket.tableNumber)}</strong><span>Phiếu #${ticket.kitchenOrderId} · Đơn #${ticket.orderId}</span></div></div><span class="status priority-${ticket.priority > 0 ? "high" : "normal"}">${ticket.priority > 0 ? `Ưu tiên ${ticket.priority}` : "Thường"}</span></div>
            <div class="ticket-meta"><span>Phục vụ: ${escapeHtml(ticket.waiterName || "Chưa có tên")}</span><span class="ticket-sent-at">Gửi phiếu lúc ${escapeHtml(dateLabel(ticket.sentAt))}</span></div>
            ${!orderIsClosed && items.some((item) => item.status === "Preparing") ? `<div class="ticket-timing" title="Thời gian từ lúc phiếu bắt đầu lần đầu; mỗi món có thể được nhận ở thời điểm khác nhau.">${clock(ticket.startedAt, "Phiếu đã bắt đầu")}</div>` : ""}
            ${!orderIsClosed && items.some((item) => ["Pending", "Preparing"].includes(item.status)) ? `<div class="ticket-timing ticket-sent-age" title="Tính từ giờ gửi phiếu, không phải giờ gọi riêng từng dòng được thêm vào phiếu cũ.">${clock(ticket.sentAt, "Phiếu gửi cách đây")}</div>` : ""}
            <p class="ticket-parent-status">Phiếu: ${escapeHtml(statusName(ticket.status))} · Đơn: ${escapeHtml(statusName(ticket.order.status))}</p>
            ${orderIsClosed ? '<p class="field-hint">Đơn đã kết thúc; các món chỉ được xem.</p>' : ""}
            <ul class="ticket-items">${items.map((item) => `<li class="kitchen-dish${kitchen.isDone(item.status) ? " is-complete" : ""}${item.status === "Cancelled" ? " is-cancelled" : ""}" data-kitchen-line="${item.id}">
                <div class="ticket-item-content"><strong>${escapeHtml(item.foodName || "Món đã ngừng bán")}</strong><span>${escapeHtml(item.sizeName)} · ${item.quantity} phần</span><span class="ticket-item-status">${escapeHtml(statusName(item.status))}</span>
                ${orderedDetails(item, orderIsClosed)}
                ${!orderIsClosed && timing.startedForItem(item, []) ? clock(timing.startedForItem(item, []), "Đang làm") : ""}
                ${item.note ? `<span>Ghi chú: ${escapeHtml(item.note)}</span>` : ""}
                ${item.toppings.length ? `<span>Topping mỗi phần: ${item.toppings.map((t) => `${escapeHtml(t.toppingName)} × ${t.quantity}`).join(", ")}</span>` : ""}
                ${item.cancelReason ? `<span class="kitchen-cancel-reason">Lý do hủy: ${escapeHtml(item.cancelReason)}</span>` : ""}</div>
                ${readOnly ? "" : `<div class="kitchen-item-actions">${Object.entries(actionNames).filter(([action]) => session.allowed(action, ticket, item)).map(([action, name]) => `<button type="button" class="ticket-action${["receive", "prepared"].includes(action) ? " primary" : ""}" data-kitchen-action="${action}" data-ticket-id="${ticket.kitchenOrderId}" data-item-id="${item.id}" ${locked() ? "disabled" : ""}>${name}</button>`).join("")}</div>`}
            </li>`).join("") || '<li class="empty-column">Chưa tải được dòng món của phiếu.</li>'}</ul>
            <div class="ticket-progress">Trong toàn phiếu: ${doneCount} phần đã xong · ${cancelledCount} phần đã hủy</div>
            <button class="secondary-action kitchen-ticket-print" type="button" data-kitchen-print="${ticket.kitchenOrderId}" ${locked() ? "disabled" : ""}>In phiếu bếp</button>
        </article>`;
    }
    function render() {
        $("#kitchenAccessNote").textContent = session.hasAccess() ? "Nhận riêng từng món. POS chỉ được giảm/hủy phần chưa được bếp nhận." : "Chỉ nhân viên bếp và quản trị viên được xử lý phiếu.";
        const tickets = filtered();
        const lanes = Object.fromEntries(stages.map((s) => [s.value, tickets.map((ticket) => ({ ticket, items: laneItems(ticket, s.value) })).filter((row) => row.items.length)]));
        const laneCount = (key) => lanes[key].reduce((sum, row) => sum + row.items.length, 0);
        const countLabel = (count) => session.loaded ? count : "—";
        const emptyMessage = session.loading ? "Đang tải phiếu bếp…" : !session.loaded
            ? "Chưa tải được phiếu. Bấm Làm mới để thử lại." : (view.search || view.priority !== "all") ? "Không có món phù hợp bộ lọc." : "Chưa có phiếu nào.";
        $("#kitchenSummary").innerHTML = [{ label: "Phiếu đang xem", count: tickets.length }, ...stages.map((s) => ({ label: s.label, count: laneCount(s.value) }))]
            .map((s) => `<div class="kitchen-summary-card"><span>${s.label}</span><strong>${countLabel(s.count)}</strong></div>`).join("");
        $("#priorityFilters").innerHTML = [{ value: "all", label: "Tất cả" }, { value: "high", label: "Ưu tiên" }, { value: "normal", label: "Thường" }].map((p) => `<button type="button" class="segment-button${p.value === view.priority ? " is-active" : ""}" data-kitchen-priority="${p.value}">${p.label}</button>`).join("");
        $("#kitchenStatusTabs").innerHTML = stages.map((s) => `<button type="button" class="status-tab${s.value === view.status ? " is-active" : ""}" aria-pressed="${s.value === view.status}" data-kitchen-stage="${s.value}"><strong>${s.label}</strong><span>${countLabel(laneCount(s.value))} món</span></button>`).join("");
        $("#kitchenQueueHint").textContent = (view.sort === "priority" ? "Phiếu ưu tiên trước; trong mỗi nhóm, phiếu gửi trước đứng trước. " : "Phiếu gửi trước đứng trước. ")
            + "Từng món nằm ở cột đúng trạng thái; một phiếu có thể xuất hiện ở nhiều cột.";
        renderPendingAlert();
        $("#kitchenBoard").setAttribute("aria-busy", String(session.loading));
        const version = JSON.stringify([tickets, session.receipt, view.status, view.sort, auth.getUser()?.roles, session.loaded, session.loading]);
        if (view.version !== version) {
            view.version = version;
            $("#kitchenBoard").innerHTML = stages.map((s) => `<section class="kitchen-column${s.value === view.status ? " is-active-status" : ""}" aria-labelledby="kitchen-column-${s.value}"><div class="kitchen-column-header"><h3 id="kitchen-column-${s.value}">${s.label}</h3><span>${countLabel(laneCount(s.value))} món · ${countLabel(lanes[s.value].reduce((sum, row) => sum + row.items.reduce((n, item) => n + item.quantity, 0), 0))} phần</span></div><div class="ticket-list">${lanes[s.value].map((row, index) => renderTicket(row.ticket, false, row.items, index + 1)).join("") || `<p class="empty-column">${emptyMessage}</p>`}</div></section>`).join("");
            const receiptVisible = session.hasAccess() && session.receipt && !session.tickets.some((ticket) => ticket.kitchenOrderId === session.receipt.kitchenOrderId);
            $("#kitchenReceipt").hidden = !receiptVisible;
            $("#kitchenReceiptContent").innerHTML = receiptVisible ? renderTicket(session.receipt, true) : "";
        }
        renderControls();
        renderPreparationTimes();
    }
    async function refresh(quiet = false, focusId = session.pending?.ticketId || session.receipt?.kitchenOrderId) {
        if (session.busy || session.loading) return;
        const recoveringLoad = view.loadFailed;
        const task = session.refresh(focusId);
        render();
        try { await task; view.loadFailed = false; if (!quiet || recoveringLoad) announce("Đã tải phiếu bếp mới nhất từ máy chủ."); }
        catch (error) {
            view.loadFailed = true;
            announce(error.message + (session.tickets.length ? " Phiếu đang hiển thị là dữ liệu từ lần tải trước, chỉ được xem. Bấm Làm mới để tải lại trước khi xử lý." : ""), true);
        }
        render();
    }
    async function run(action, ticketId, itemId, data, expected) {
        const task = session.perform(action, ticketId, itemId, data, expected);
        renderControls();
        try {
            const result = await task;
            dialog.close();
            if (action !== "receive" && result.ticket && !kitchen.ticketClosed(result.ticket.status)) view.status = stage(result.ticket);
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
        if (locked() || printView.isOpen() || printView.isBusy()) return;
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
        $("#kitchenActionHint").textContent = action.action === "prepared" ? "Chỉ nhập số phần VỪA làm thêm trong đợt này. Không cộng lại phần đã báo xong trước đó; phần còn lại tiếp tục chế biến."
            : action.action === "shortage" ? "Phần làm được chuyển thành đã xong; phần thiếu bị hủy và ghi hao hụt, không hoàn kho. Ngừng bán sẽ gỡ món khỏi menu POS."
            : action.action === "reopen" ? "Chỉ mở lại khi đơn chưa thanh toán/hủy và phiếu chưa đóng. Máy chủ đưa món về đang chế biến; không trừ kho lần nữa."
            : action.status === "Pending" && $("#kitchenCancelReason").value === "CustomerChange" ? "Khách thay đổi khi món chưa nhận: máy chủ hoàn kho cho phần bị hủy."
            : "Phần đã bắt đầu chế biến hoặc hủy vì sự cố không hoàn kho; máy chủ quyết định việc ghi nhận hao hụt.";
    }
    $("#kitchenBoard").addEventListener("click", (event) => {
        const printButton = event.target.closest("[data-kitchen-print]");
        if (printButton) { printView.open(Number(printButton.dataset.kitchenPrint)); return; }
        const button = event.target.closest("[data-kitchen-action]");
        if (button) openAction(button.dataset.kitchenAction, Number(button.dataset.ticketId), Number(button.dataset.itemId));
    });
    $("#kitchenReceiptContent").addEventListener("click", (event) => {
        const button = event.target.closest("[data-kitchen-print]");
        if (button) printView.open(Number(button.dataset.kitchenPrint));
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
    $("#kitchenSort").addEventListener("change", (event) => { view.sort = event.target.value === "priority" ? "priority" : "oldest"; render(); });
    $("#kitchenShowWaiting").addEventListener("click", () => {
        view.status = "pending"; view.search = ""; view.priority = "all";
        $("#kitchenSearch").value = ""; render();
        $("#kitchenBoard").scrollIntoView({ block: "start", behavior: "smooth" });
    });
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
    let poll, preparationClock;
    function startPolling() {
        window.clearInterval(poll);
        window.clearInterval(preparationClock);
        // Bộ lọc nằm ngoài dialog: giữ focus tìm kiếm vẫn phải nhận được các món mới.
        poll = window.setInterval(() => { if (!document.hidden && !dialog.open && !printView.isOpen() && !printView.isBusy() && !session.pending) refresh(true, null); }, 5000);
        preparationClock = window.setInterval(() => { if (!document.hidden) renderPreparationTimes(); }, 1000);
    }
    startPolling();
    window.addEventListener("pageshow", (event) => { startPolling(); if (event.persisted) refresh(true); });
    window.addEventListener("pagehide", () => { window.clearInterval(poll); window.clearInterval(preparationClock); });
})();
