(function () {
    "use strict";

    const escapeHtml = (value) => String(value ?? "").replace(/[&<>"']/g,
        (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[character]));
    const text = (value) => value === null || value === undefined || String(value).trim() === "" ? "—" : escapeHtml(value);
    const note = (value) => text(value).replace(/\r\n|\r|\n/g, "<br>");
    const normalizeStatus = (value) => typeof value === "string" ? value.trim().toLowerCase() : "";
    const statuses = {
        pending: { label: "", className: "is-pending" },
        preparing: { label: "", className: "is-preparing" },
        ready: { label: "ĐÃ XONG", className: "is-done" },
        completed: { label: "ĐÃ XONG", className: "is-done" },
        cancelled: { label: "ĐÃ HỦY — KHÔNG LÀM", className: "is-cancelled" }
    };
    function quantity(value) {
        if ((typeof value !== "number" && typeof value !== "string") || (typeof value === "string" && !/^\d+$/.test(value.trim()))) return null;
        const number = Number(value);
        return Number.isSafeInteger(number) && number >= 0 ? number : null;
    }
    const quantityText = (value) => {
        const number = quantity(value);
        return number === null ? "—" : number.toLocaleString("vi-VN");
    };
    function utcEpoch(value) {
        const epoch = window.restaurantPreparationTiming?.parseUtc(value);
        return typeof epoch === "number" && Number.isFinite(epoch) ? epoch : null;
    }
    function dateText(epoch) {
        if (epoch === null || !Number.isFinite(new Date(epoch).getTime())) return "—";
        return escapeHtml(new Date(epoch).toLocaleString("vi-VN", {
            timeZone: "Asia/Ho_Chi_Minh", day: "2-digit", month: "2-digit", year: "numeric",
            hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23"
        }));
    }

    function renderSlip(ticket, { scope = "active", isReprint = false, generatedAt = Date.now() } = {}) {
        const source = ticket && typeof ticket === "object" ? ticket : {};
        const full = scope === "all";
        const orderIsClosed = ["paid", "cancelled"].includes(normalizeStatus(source.order?.status));
        const rows = (Array.isArray(source.items) ? source.items : [])
            .filter((item) => item && typeof item === "object" && (full || (!orderIsClosed && ["pending", "preparing"].includes(normalizeStatus(item.status)))));
        const amounts = rows.map((item) => quantity(item.quantity ?? item.quanlity));
        const sum = amounts.every((value) => value !== null) ? amounts.reduce((total, value) => total + value, 0) : null;
        const total = Number.isSafeInteger(sum) ? sum.toLocaleString("vi-VN") : "—";
        const generatedEpoch = typeof generatedAt === "number" && Number.isFinite(generatedAt) ? generatedAt : utcEpoch(generatedAt);
        const sentAt = utcEpoch(source.sentAt);
        const closedWarning = '<p class="kitchen-slip-closed">ĐƠN ĐÃ KẾT THÚC — CHỈ ĐỐI CHIẾU</p>';
        // Mỗi dòng món giữ nguyên số phần, trạng thái và ghi chú; không gộp theo tên hoặc tách mỗi phần thành một phiếu.
        return `<section class="kitchen-slip-batch">
            <p class="kitchen-slip-summary"><strong>${rows.length} phiếu món riêng</strong> · ${total} phần trong bản in</p>
            ${rows.length ? rows.map((item, index) => {
                const itemStatus = normalizeStatus(item.status);
                const state = statuses[itemStatus] || { label: "CHƯA RÕ TRẠNG THÁI — ĐỐI CHIẾU MÀN HÌNH", className: "is-unknown" };
                const toppings = (Array.isArray(item.toppings) ? item.toppings : []).filter((topping) => topping && typeof topping === "object");
                return `<article class="kitchen-slip${isReprint === true ? " is-reprint" : ""}">
                    <header class="kitchen-slip-header">
                        ${isReprint === true ? '<p class="kitchen-slip-reprint">BẢN IN LẠI</p>' : ""}
                        <h1 class="kitchen-slip-title">PHIẾU BẾP</h1>
                        ${orderIsClosed ? closedWarning : ""}
                        <p class="kitchen-slip-table">Bàn ${text(source.tableNumber)}</p>
                        <p class="kitchen-slip-position">Tờ ${index + 1}/${rows.length} · mỗi món một phiếu</p>
                        ${full ? '<p class="kitchen-slip-scope">BẢN ĐỐI CHIẾU</p>' : ""}
                    </header>
                    <ol class="kitchen-slip-items"><li class="kitchen-slip-item ${state.className}">
                        <div class="kitchen-slip-item-heading"><span class="kitchen-slip-quantity">${quantityText(item.quantity ?? item.quanlity)} <small>phần</small></span><strong class="kitchen-slip-item-name">${text(item.foodName)}</strong></div>
                        <p class="kitchen-slip-item-size">Cỡ: ${text(item.sizeName)}</p>
                        <p class="kitchen-slip-sent-at"><strong>Gửi phiếu lúc:</strong> ${sentAt === null ? "Chưa có dữ liệu" : dateText(sentAt)}${sentAt === null ? "" : " <small>(giờ Việt Nam)</small>"}</p>
                        ${state.label ? `<p class="kitchen-slip-item-flag">${state.label}</p>` : ""}
                        ${toppings.length ? `<div class="kitchen-slip-toppings"><strong>Topping mỗi phần</strong><ul>${toppings.map((topping) => `<li>${text(topping.toppingName)} × ${quantityText(topping.quantity ?? topping.quanlity)}</li>`).join("")}</ul></div>` : ""}
                        ${item.note ? `<p class="kitchen-slip-item-note"><strong>Ghi chú món:</strong> ${note(item.note)}</p>` : ""}
                        ${itemStatus === "cancelled" ? `<p class="kitchen-slip-cancel-reason"><strong>Lý do hủy:</strong> ${note(item.cancelReason)}</p>` : ""}
                    </li></ol>
                    <dl class="kitchen-slip-meta">
                        <div><dt>Phiếu</dt><dd>#${text(source.kitchenOrderId)}</dd></div>
                        <div><dt>Đơn</dt><dd>#${text(source.orderId ?? source.order?.orderId)}</dd></div>
                        <div><dt>Dòng món</dt><dd>#${text(item.orderItemId ?? item.id)}</dd></div>
                        <div><dt>Phục vụ</dt><dd>${text(source.waiterName)}</dd></div>
                    </dl>
                    ${source.order?.note ? `<div class="kitchen-slip-order-note"><strong>Ghi chú đơn</strong><p>${note(source.order.note)}</p></div>` : ""}
                    <footer class="kitchen-slip-footer"><p>Lập bản in lúc: ${dateText(generatedEpoch)} <small>(giờ Việt Nam trên thiết bị)</small></p>
                        <p><strong>Đối chiếu màn hình trước khi làm; phiếu giấy không tự cập nhật.</strong></p>
                    </footer>
                </article>`;
            }).join("") : `${orderIsClosed ? closedWarning : ""}<p class="kitchen-slip-empty">${full ? "Phiếu chưa có dòng món." : "Không có món cần làm trong phiếu này."}</p>`}
        </section>`;
    }

    window.restaurantKitchenPrinting = Object.freeze({ renderSlip });
})();
