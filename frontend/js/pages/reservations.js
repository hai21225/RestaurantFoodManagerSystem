(function () {
    const $ = (selector) => document.querySelector(selector);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const money = (value) => typeof value === "number" && Number.isFinite(value) ? `${value.toLocaleString("vi-VN")}đ` : "0đ";
    const api = window.restaurantApi;
    const auth = window.restaurantAuth;

    const statusLabels = {
        Pending: { text: "Chờ xác nhận", cls: "is-pending" },
        Confirmed: { text: "Đã xác nhận", cls: "is-confirmed" },
        Seated: { text: "Đã nhận bàn", cls: "is-seated" },
        Completed: { text: "Hoàn tất", cls: "is-available" },
        NoShow: { text: "Không đến", cls: "is-noshow" },
        Cancelled: { text: "Đã hủy", cls: "is-cancelled" }
    };

    const depositLabels = {
        None: "Không cần cọc",
        Pending: "Chờ đặt cọc",
        Paid: "Đã đặt cọc",
        AppliedToOrder: "Đã trừ vào hóa đơn",
        Refunded: "Đã hoàn cọc",
        Forfeited: "Mất cọc"
    };

    const state = {
        tables: [],
        areas: [],
        myReservations: [],
        staffReservations: [],
        busy: false,
        momoIntent: null
    };

    function showNotice(text, type = "") {
        const el = $("#reservationNotice");
        if (!text) {
            el.hidden = true;
            el.textContent = "";
            return;
        }
        el.textContent = text;
        el.className = `v2-notice${type ? ` is-${type}` : ""}`;
        el.hidden = false;
    }

    function formatTime(value) {
        if (!value) return "—";
        try {
            return new Date(value).toLocaleString("vi-VN", {
                day: "2-digit", month: "2-digit", year: "numeric",
                hour: "2-digit", minute: "2-digit"
            });
        } catch (_) {
            return String(value);
        }
    }

    function toLocalInputValue(date) {
        const pad = (n) => String(n).padStart(2, "0");
        return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
    }

    function initDefaultBookingTimes() {
        const now = new Date();
        const from = new Date(now.getTime() + 60 * 60 * 1000);
        from.setMinutes(0, 0, 0);
        const to = new Date(from.getTime() + 2 * 60 * 60 * 1000);
        $("#bookFrom").value = toLocalInputValue(from);
        $("#bookTo").value = toLocalInputValue(to);
    }

    function isStaff() {
        return auth.hasRole("Admin", "Waiter", "Cashier");
    }

    function isCustomerView() {
        return auth.hasRole("Customer") && !isStaff();
    }

    function renderTableOptions() {
        const areaId = $("#bookArea").value;
        const filtered = state.tables.filter((t) =>
            t.status !== "Maintenance" && (areaId === "all" || String(t.areaId ?? "") === String(areaId))
        );
        const select = $("#bookTable");
        if (!filtered.length) {
            select.innerHTML = '<option value="">Không có bàn khả dụng</option>';
            return;
        }
        select.innerHTML = filtered.map((t) =>
            `<option value="${t.tableId}">Bàn ${esc(t.tableNumber)} (${esc(t.areaName || "Khu chung")} · ${t.capacity} chỗ)</option>`
        ).join("");
    }

    function renderMyReservations() {
        const list = state.myReservations;
        $("#myReservationCount").textContent = `${list.length} lịch đặt`;
        if (!list.length) {
            $("#myReservationsList").innerHTML = '<p class="v2-notice">Bạn chưa có lịch đặt bàn nào.</p>';
            return;
        }
        $("#myReservationsList").innerHTML = list.map((r) => {
            const st = statusLabels[r.status] || { text: r.status, cls: "" };
            const depText = depositLabels[r.depositStatus] || r.depositStatus;
            const canPayMomo = r.depositAmount > 0 && r.depositStatus === "Pending" && ["Pending", "Confirmed"].includes(r.status);
            const canCancel = ["Pending", "Confirmed"].includes(r.status);
            return `<article class="v2-res-card">
                <div class="v2-res-head">
                    <div>
                        <strong>Lịch #${r.reservationId} · Bàn ${esc(r.tableNumber || r.tableId)}</strong>
                        <div class="v2-res-meta">
                            <span>${formatTime(r.reservedFrom)} → ${formatTime(r.reservedTo)}</span>
                            <span>${r.partySize} khách</span>
                        </div>
                    </div>
                    <span class="v2-badge ${st.cls}">${esc(st.text)}</span>
                </div>
                <div class="v2-res-meta">
                    <span>Cọc: <strong>${money(r.depositAmount)}</strong> (${esc(depText)})</span>
                    ${r.note ? `<span>Ghi chú: ${esc(r.note)}</span>` : ""}
                    ${r.cancelReason ? `<span>Lý do hủy: ${esc(r.cancelReason)}</span>` : ""}
                </div>
                <div class="v2-actions-row">
                    ${canPayMomo ? `<button class="primary-action" type="button" data-action="momo" data-id="${r.reservationId}" ${state.busy ? "disabled" : ""}>Đặt cọc MoMo</button>` : ""}
                    ${canCancel ? `<button class="secondary-action" type="button" data-action="cancel" data-id="${r.reservationId}" ${state.busy ? "disabled" : ""}>Hủy lịch</button>` : ""}
                </div>
            </article>`;
        }).join("");
    }

    function renderStaffReservations() {
        const list = state.staffReservations;
        $("#staffReservationCount").textContent = `${list.length} lịch đặt`;
        if (!list.length) {
            $("#staffReservationsList").innerHTML = '<p class="v2-notice">Không có lịch đặt bàn nào phù hợp bộ lọc.</p>';
            return;
        }
        const canOperate = auth.hasRole("Admin", "Waiter");
        const canCashier = auth.hasRole("Admin", "Cashier");
        $("#staffReservationsList").innerHTML = list.map((r) => {
            const st = statusLabels[r.status] || { text: r.status, cls: "" };
            const depText = depositLabels[r.depositStatus] || r.depositStatus;
            const actions = [];
            if (canOperate && r.status === "Pending") {
                actions.push(`<button class="primary-action" type="button" data-action="confirm" data-id="${r.reservationId}" ${state.busy ? "disabled" : ""}>Xác nhận</button>`);
            }
            if (canOperate && ["Pending", "Confirmed"].includes(r.status)) {
                const isOverdue = Boolean(r.isOverdue) || (r.reservedTo ? Date.now() > new Date(r.reservedTo).getTime() : false);
                const noShowDisabled = state.busy || !isOverdue;
                const noShowTitle = !isOverdue
                    ? "Chưa quá giờ hẹn, chưa thể đánh dấu không đến / trả bàn"
                    : "Đánh dấu khách không đến và trả bàn";
                actions.push(`<button class="primary-action" type="button" data-action="seat" data-id="${r.reservationId}" ${state.busy ? "disabled" : ""}>Nhận bàn</button>`);
                actions.push(`<button class="secondary-action" type="button" data-action="no-show" data-id="${r.reservationId}" ${noShowDisabled ? "disabled" : ""} title="${esc(noShowTitle)}">Khách không đến / Trả bàn</button>`);
                actions.push(`<button class="secondary-action" type="button" data-action="cancel" data-id="${r.reservationId}" ${state.busy ? "disabled" : ""}>Hủy</button>`);
            }
            if (canCashier && r.depositAmount > 0 && r.depositStatus === "Pending" && ["Pending", "Confirmed"].includes(r.status)) {
                actions.push(`<button class="secondary-action" type="button" data-action="deposit-confirm" data-id="${r.reservationId}" ${state.busy ? "disabled" : ""}>Xác nhận đã thu cọc</button>`);
            }
            const notOverdueNote = canOperate && ["Pending", "Confirmed"].includes(r.status) && !(Boolean(r.isOverdue) || (r.reservedTo ? Date.now() > new Date(r.reservedTo).getTime() : false))
                ? `<div class="v2-res-meta" style="font-size:12px;">Nút "Khách không đến / Trả bàn" chỉ mở sau khi quá giờ hẹn (${formatTime(r.reservedTo)}).</div>`
                : "";
            return `<article class="v2-res-card">
                <div class="v2-res-head">
                    <div>
                        <strong>#${r.reservationId} · ${esc(r.customerName)} (${esc(r.customerPhone)})</strong>
                        <div class="v2-res-meta">
                            <span>Bàn ${esc(r.tableNumber || r.tableId)}</span>
                            <span>${formatTime(r.reservedFrom)} → ${formatTime(r.reservedTo)}</span>
                            <span>${r.partySize} khách</span>
                        </div>
                    </div>
                    <span class="v2-badge ${st.cls}">${esc(st.text)}</span>
                </div>
                <div class="v2-res-meta">
                    <span>Cọc: <strong>${money(r.depositAmount)}</strong> (${esc(depText)})</span>
                    ${r.note ? `<span>Ghi chú: ${esc(r.note)}</span>` : ""}
                </div>
                ${actions.length ? `<div class="v2-actions-row">${actions.join("")}</div>` : ""}
                ${notOverdueNote}
            </article>`;
        }).join("");
    }

    async function loadCustomerData() {
        const [areas, tables, myRes] = await Promise.all([
            api.get("/public/table-areas").catch(() => []),
            api.get("/public/tables"),
            api.get("/reservations/my")
        ]);
        state.areas = Array.isArray(areas) ? areas : [];
        state.tables = Array.isArray(tables) ? tables : [];
        state.myReservations = Array.isArray(myRes) ? myRes : [];

        $("#bookArea").innerHTML = ['<option value="all">Tất cả khu vực</option>']
            .concat(state.areas.map((a) => `<option value="${esc(a.tableAreaId ?? a.areaId)}">${esc(a.name || a.areaName)}</option>`)).join("");
        renderTableOptions();
        renderMyReservations();
    }

    async function loadStaffData() {
        const params = new URLSearchParams();
        const dateVal = $("#filterDate").value;
        const statusVal = $("#filterStatus").value;
        if (dateVal) params.set("date", dateVal);
        if (statusVal) params.set("status", statusVal);
        const query = params.toString() ? `?${params.toString()}` : "";
        const data = await api.get(`/reservations${query}`);
        state.staffReservations = Array.isArray(data) ? data : [];
        renderStaffReservations();
    }

    async function refresh() {
        try {
            if (isStaff()) {
                await loadStaffData();
            } else {
                await loadCustomerData();
            }
        } catch (error) {
            showNotice(error.message || "Không thể tải danh sách đặt bàn.", "error");
        }
    }

    function safeUrl(value) {
        try {
            const u = new URL(value);
            return ["http:", "https:"].includes(u.protocol) ? u.href : "";
        } catch (_) {
            return "";
        }
    }

    function openMomoDialog(intent) {
        state.momoIntent = intent;
        const body = $("#momoDialogBody");
        if (intent.isMock) {
            body.innerHTML = `
                <p class="v2-notice">CHẾ ĐỘ GIẢ LẬP — Giao dịch thử cho lịch #${intent.reservationId} · Số tiền: <strong>${money(intent.amount)}</strong>.</p>
                <div class="v2-actions-row">
                    <button id="momoMockSuccessBtn" class="primary-action" type="button">Giả lập thanh toán thành công</button>
                    <button id="momoMockFailBtn" class="secondary-action" type="button">Giả lập thất bại</button>
                </div>`;
        } else {
            const redirect = safeUrl(intent.redirectUrl);
            const qr = safeUrl(intent.qrCodeUrl);
            body.innerHTML = `
                <p>Số tiền cần đặt cọc: <strong>${money(intent.amount)}</strong></p>
                ${qr ? `<img src="${esc(qr)}" alt="QR MoMo" style="max-width:220px;border-radius:10px;" referrerpolicy="no-referrer">` : ""}
                ${redirect ? `<a class="primary-action" href="${esc(redirect)}" target="_blank" rel="noopener noreferrer">Mở cổng thanh toán MoMo</a>` : '<p class="v2-notice is-error">Chưa nhận được liên kết thanh toán MoMo.</p>'}`;
        }
        $("#momoDepositDialog").showModal();
    }

    async function runAction(task) {
        state.busy = true;
        if (isStaff()) renderStaffReservations();
        else renderMyReservations();
        try {
            await task();
        } catch (error) {
            showNotice(error.message || "Thao tác không thành công.", "error");
        } finally {
            state.busy = false;
            await refresh();
        }
    }

    $("#bookArea").addEventListener("change", renderTableOptions);
    $("#refreshMyBtn").addEventListener("click", () => refresh());
    $("#refreshStaffBtn").addEventListener("click", () => refresh());
    $("#filterDate").addEventListener("change", () => refresh());
    $("#filterStatus").addEventListener("change", () => refresh());
    $("#closeMomoDialogBtn").addEventListener("click", () => $("#momoDepositDialog").close());

    $("#bookingForm").addEventListener("submit", (event) => {
        event.preventDefault();
        const tableId = Number($("#bookTable").value);
        const customerName = $("#bookName").value.trim();
        const customerPhone = $("#bookPhone").value.trim();
        const reservedFrom = $("#bookFrom").value;
        const reservedTo = $("#bookTo").value;
        const partySize = Number($("#bookPartySize").value);
        const depositAmount = Number($("#bookDeposit").value || 0);
        const note = $("#bookNote").value.trim();

        if (!tableId || !customerName || !customerPhone || !reservedFrom || !reservedTo) {
            showNotice("Vui lòng điền đầy đủ bàn, họ tên, số điện thoại và thời gian.", "error");
            return;
        }
        if (new Date(reservedTo) <= new Date(reservedFrom)) {
            showNotice("Thời gian kết thúc phải sau thời gian bắt đầu.", "error");
            return;
        }

        runAction(async () => {
            await api.post("/reservations", {
                tableId,
                customerName,
                customerPhone,
                partySize,
                reservedFrom: new Date(reservedFrom).toISOString(),
                reservedTo: new Date(reservedTo).toISOString(),
                depositAmount,
                note: note || null
            });
            $("#bookNote").value = "";
            showNotice("Đặt bàn thành công!", "success");
        });
    });

    async function handleListClick(event) {
        const btn = event.target.closest("[data-action]");
        if (!btn || state.busy) return;
        const id = Number(btn.dataset.id);
        const action = btn.dataset.action;

        if (action === "momo") {
            await runAction(async () => {
                const intent = await api.post(`/reservations/${id}/deposit/momo`);
                openMomoDialog(intent);
            });
            return;
        }
        if (action === "cancel") {
            const reason = window.prompt("Lý do hủy lịch (có thể bỏ trống):", "") ?? null;
            if (reason === null) return;
            await runAction(async () => {
                await api.post(`/reservations/${id}/cancel`, { reason: reason.trim() || null });
                showNotice(`Đã hủy lịch #${id}.`, "success");
            });
            return;
        }
        if (action === "confirm") {
            await runAction(async () => {
                await api.post(`/reservations/${id}/confirm`);
                showNotice(`Đã xác nhận lịch #${id}.`, "success");
            });
            return;
        }
        if (action === "seat") {
            await runAction(async () => {
                await api.post(`/reservations/${id}/seat`);
                showNotice(`Đã nhận bàn cho lịch #${id}.`, "success");
            });
            return;
        }
        if (action === "no-show") {
            await runAction(async () => {
                await api.post(`/reservations/${id}/no-show`);
                showNotice(`Đã đánh dấu không đến cho lịch #${id}.`, "success");
            });
            return;
        }
        if (action === "deposit-confirm") {
            const code = window.prompt("Mã giao dịch thu cọc (tùy chọn):", "") ?? null;
            if (code === null) return;
            await runAction(async () => {
                await api.post(`/reservations/${id}/deposit/confirm`, { transactionCode: code.trim() || null });
                showNotice(`Đã xác nhận thu cọc cho lịch #${id}.`, "success");
            });
        }
    }

    $("#myReservationsList").addEventListener("click", handleListClick);
    $("#staffReservationsList").addEventListener("click", handleListClick);

    $("#momoDialogBody").addEventListener("click", (event) => {
        const successBtn = event.target.closest("#momoMockSuccessBtn");
        const failBtn = event.target.closest("#momoMockFailBtn");
        if (!successBtn && !failBtn) return;
        const intent = state.momoIntent;
        if (!intent) return;
        const isSuccess = Boolean(successBtn);
        $("#momoDepositDialog").close();
        runAction(async () => {
            await api.post("/reservations/deposit/momo/notify", {
                orderId: `RES-${intent.reservationId}`,
                requestId: intent.requestId,
                amount: String(intent.amount),
                resultCode: isSuccess ? "0" : "1006",
                message: isSuccess ? "Mock success" : "Mock failed",
                transId: `MOCK-${Date.now()}`
            });
            showNotice(isSuccess ? "Đã ghi nhận đặt cọc MoMo giả lập thành công." : "Giao dịch giả lập thất bại.", isSuccess ? "success" : "error");
        });
    });

    window.addEventListener("restaurant:realtime", (event) => {
        const evtName = String(event.detail?.event || "");
        if (/reservation|table/i.test(evtName)) {
            refresh();
        }
    });

    window.addEventListener("restaurant:realtime-connected", () => {
        refresh();
    });

    auth.ready.then((user) => {
        if (!user) return;
        if (isStaff()) {
            $("#staffSection").hidden = false;
            $("#customerSection").hidden = true;
        } else {
            $("#customerSection").hidden = false;
            $("#staffSection").hidden = true;
            $("#bookName").value = user.fullName || user.userName || "";
            if (user.phone) $("#bookPhone").value = user.phone;
            initDefaultBookingTimes();
        }
        refresh();
    });
})();
