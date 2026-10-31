(function () {
    const auth = window.restaurantAuth;
    const session = new window.restaurantReporting.ReportingSession({ api: window.restaurantApi, auth });
    const $ = (id) => document.getElementById(id);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const money = (value) => `${value.toLocaleString("vi-VN", { maximumFractionDigits: 2 })}đ`;
    const time = (value) => new Date(/[zZ]$|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`).toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" });
    const labels = { Pending: "Chờ xử lý", SentToKitchen: "Đã gửi bếp", Preparing: "Đang làm", Ready: "Sẵn sàng", Completed: "Hoàn tất" };
    let filter = "all";
    function renderAccess() {
        const allowed = session.hasAccess();
        $("dashboard-screen").hidden = !allowed; $("dashboardAccess").hidden = allowed;
        if (allowed) return true;
        const links = [];
        if (auth.hasRole("Waiter")) links.push(["pages/tables.html", "Bàn ăn / Đặt món"]);
        if (auth.hasRole("Chef")) links.push(["pages/kitchen.html", "Mở màn Bếp"]);
        if (auth.hasRole("Cashier")) links.push(["pages/payment.html", "Mở Thu ngân"]);
        if (auth.hasRole("WarehouseManager")) links.push(["pages/inventory.html", "Kho hàng"], ["pages/purchases.html", "Nhập hàng"]);
        $("dashboardAccess").innerHTML = `<h2>Chọn nghiệp vụ của bạn</h2><p>Tổng quan doanh thu dành cho Quản trị viên. Bạn có thể tiếp tục công việc theo vai trò đã được cấp.</p>
            <div class="reporting-actions">${links.map(([url, label]) => `<a class="secondary-action" href="${url}">${label}</a>`).join("")}</div>`;
        return false;
    }
    function render() {
        if (!renderAccess()) return;
        const data = session.summary;
        $("refreshDashboard").disabled = session.dashboardLoading;
        $("dashboardUpdated").textContent = data ? `Ngày kinh doanh ${data.businessDate} · Cập nhật ${time(data.generatedAt)} · Giờ Việt Nam` : "";
        if (!data) {
            $("metricGrid").innerHTML = `<p class="empty-state">${session.dashboardLoading ? "Đang tải tổng quan…" : "Chưa tải được số liệu tổng quan. Bấm Làm mới để thử lại."}</p>`;
            for (const id of ["tableMap", "orderList"]) $(id).innerHTML = '<p class="empty-state">Chưa có dữ liệu được tải.</p>';
            for (const id of ["tableSummary", "kitchenSummary"]) $(id).innerHTML = "";
            $("dashboardTableCount").textContent = "—"; $("queueCount").textContent = "—"; return;
        }
        $("metricGrid").innerHTML = [["Doanh thu đã thu hôm nay", money(data.revenueToday), "Chỉ khoản đã thu; không gồm đã hoàn tiền."],
            ["Đơn đã thu hôm nay", data.paidOrderCountToday, "Số đơn theo thời điểm thu tiền."], ["Đơn đang mở", data.openOrderCount, "Các đơn chưa thanh toán hoặc hủy."],
            ["Nguyên liệu dưới ngưỡng", data.lowStockCount, "Xem thêm tình trạng hỏng/hết tại Kho."]]
            .map(([label, value, note]) => `<article class="metric-card"><span>${label}</span><strong>${esc(value)}</strong><small>${note}</small></article>`).join("");
        $("tableSummary").innerHTML = [["Trống", data.tables.available], ["Có khách", data.tables.occupied], ["Bảo trì / Ngừng dùng", data.tables.maintenance]]
            .map(([label, value]) => `<span>${label}: <strong>${value}</strong></span>`).join("");
        $("kitchenSummary").innerHTML = [["Tổng phiếu mở", data.kitchen.open], ["Chờ nhận", data.kitchen.pending], ["Đã nhận / Đang làm", data.kitchen.preparing], ["Đã xong", data.kitchen.ready]]
            .map(([label, value]) => `<span>${label}: <strong>${value}</strong></span>`).join("");
        $("dashboardTableCount").textContent = `${data.tables.total} bàn`;
        $("tableMap").innerHTML = session.tableError ? `<p class="empty-state">${esc(session.tableError)} Bấm Làm mới để tải lại sơ đồ bàn.</p>`
            : session.tables.length ? session.tables.map((table) => {
                const maintenance = !table.isActive || table.status === "Maintenance";
                const label = !table.isActive ? "Ngừng sử dụng" : maintenance ? "Bảo trì" : table.status === "Occupied" ? "Có khách" : "Trống";
                const content = `<strong>${esc(table.tableNumber)}</strong><span>${table.capacity} chỗ · ${esc(table.areaName || "Chưa phân khu")}</span><span>${label}</span>`;
                return maintenance ? `<div class="table-tile is-unavailable">${content}</div>` : `<a class="table-tile ${table.status === "Occupied" ? "is-occupied" : ""}" href="pages/order.html?tableId=${table.tableId}">${content}</a>`;
            }).join("") : '<p class="empty-state">Chưa có bàn được khai báo.</p>';
        const rows = data.recentOrders.filter((o) => filter === "all" || (filter === "waiting" && ["Pending", "SentToKitchen"].includes(o.status))
            || (filter === "preparing" && o.status === "Preparing") || (filter === "ready" && ["Ready", "Completed"].includes(o.status)));
        $("queueCount").textContent = `${rows.length} đơn hiển thị`;
        $("orderList").innerHTML = rows.length ? rows.map((order) => `<article class="order-item"><div class="order-topline"><strong>ORD-${order.orderId}</strong><span>${esc(labels[order.status] || order.status)}</span></div>
            <div class="order-meta"><span>Bàn ${esc(order.tableNumber || order.tableId)}</span><strong>${money(order.totalAmount)}</strong></div><span class="reporting-caption">${time(order.createdAt)}</span>
            <div class="reporting-actions"><a href="pages/order.html?tableId=${order.tableId}">Xem đơn tại POS</a>${["Ready", "Completed"].includes(order.status) ? `<a href="pages/payment.html?orderId=${order.orderId}">Đến Thu ngân</a>` : ""}</div></article>`).join("")
            : '<p class="empty-state">Không có đơn trong nhóm đang chọn của danh sách gần nhất.</p>';
    }
    async function load() {
        $("dashboardNotice").textContent = "";
        $("dashboardNotice").classList.remove("is-error");
        try { const promise = session.loadDashboard(); render(); await promise; }
        catch (error) { $("dashboardNotice").textContent = error.message; $("dashboardNotice").classList.add("is-error"); }
        finally { render(); }
    }
    $("refreshDashboard").addEventListener("click", load);
    $("createOrderButton").addEventListener("click", () => { window.location.href = "pages/tables.html"; });
    document.querySelectorAll("[data-filter]").forEach((button) => button.addEventListener("click", () => {
        filter = button.dataset.filter; document.querySelectorAll("[data-filter]").forEach((b) => b.classList.toggle("is-active", b === button)); render();
    }));
    auth.ready.then(() => { if (renderAccess()) load(); });
})();
