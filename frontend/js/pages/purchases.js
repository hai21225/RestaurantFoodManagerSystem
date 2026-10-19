(async function () {
    const list = document.querySelector("#purchaseList");
    const count = document.querySelector("#purchaseCount");
    const summaryValues = document.querySelectorAll(".management-summary strong");

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    const monthFormatter = new Intl.DateTimeFormat("en-CA", {
        timeZone: "Asia/Ho_Chi_Minh",
        year: "numeric",
        month: "2-digit"
    });

    function monthOf(value) {
        if (!value) return "";
        const timestamp = String(value);
        const date = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(timestamp) ? timestamp : `${timestamp}Z`);
        return Number.isNaN(date.getTime()) ? "" : monthFormatter.format(date);
    }

    try {
        const purchases = await window.restaurantApi.get("/management/purchase-orders");
        const statusOf = (item) => String(item.status || "").toLowerCase();
        count.textContent = `${purchases.length} phiếu`;
        summaryValues[0].textContent = String(purchases.filter((item) => ["draft", "pending"].includes(statusOf(item))).length);
        summaryValues[1].textContent = String(purchases.filter((item) => ["approved", "ordered"].includes(statusOf(item))).length);
        summaryValues[2].textContent = String(purchases.filter((item) => statusOf(item) === "received").length);
        const currentMonth = monthOf(new Date().toISOString());
        const monthTotal = purchases
            .filter((item) => statusOf(item) === "received" && monthOf(item.receivedAt) === currentMonth)
            .reduce((sum, item) => sum + (Number(item.totalAmount) || 0), 0);
        summaryValues[3].textContent = `${monthTotal.toLocaleString("vi-VN")}đ`;

        list.className = "management-rows";
        list.innerHTML = purchases.length ? purchases.map((item) => `
            <article class="management-row">
                <div><strong>Phiếu #${escapeHtml(item.id)}</strong><span>Nhà cung cấp #${escapeHtml(item.supplierId)}</span></div>
                <div><span>Trạng thái</span><strong>${escapeHtml(item.status)}</strong></div>
                <div><span>Tổng tiền</span><strong>${(Number(item.totalAmount) || 0).toLocaleString("vi-VN")}đ</strong></div>
            </article>
        `).join("") : '<div class="empty-workspace"><div><strong>Chưa có phiếu nhập hàng</strong></div></div>';
    } catch (error) {
        list.innerHTML = `<div><strong>Không tải được phiếu nhập</strong><span>${escapeHtml(error.message)}</span></div>`;
    }
})();
