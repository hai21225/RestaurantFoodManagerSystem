(function () {
    const dashboardData = window.dashboardData || {
        metrics: [],
        tables: [],
        orders: []
    };

    const metricGrid = document.querySelector("#metricGrid");
    const tableMap = document.querySelector("#tableMap");
    const dashboardTableCount = document.querySelector("#dashboardTableCount");
    const orderList = document.querySelector("#orderList");
    const queueCount = document.querySelector("#queueCount");
    const filterButtons = document.querySelectorAll(".filter-button");
    const navButtons = document.querySelectorAll("[data-page]");

    function renderMetrics() {
        if (!metricGrid) {
            return;
        }

        if (!dashboardData.metrics.length) {
            metricGrid.innerHTML = `
                <article class="metric-card empty-state">
                    <strong>Chưa có dữ liệu tổng quan</strong>
                    <small>Các chỉ số trong ca sẽ hiển thị sau khi kết nối nguồn dữ liệu thật.</small>
                </article>
            `;
            return;
        }

        metricGrid.innerHTML = dashboardData.metrics.map((metric) => `
            <article class="metric-card">
                <span>${metric.label}</span>
                <strong>${metric.value}</strong>
                <small>${metric.note}</small>
            </article>
        `).join("");
    }

    function renderTables() {
        if (!tableMap) {
            return;
        }

        if (dashboardTableCount) {
            dashboardTableCount.textContent = `${dashboardData.tables.length} bàn`;
        }

        if (!dashboardData.tables.length) {
            tableMap.innerHTML = '<p class="empty-state">Chưa có dữ liệu bàn.</p>';
            return;
        }

        tableMap.innerHTML = dashboardData.tables.map((table) => `
            <button class="table-tile" type="button">
                <strong>${table.name}</strong>
                <span>${table.seats} chỗ</span>
                <span class="status status-${table.status}">${table.label}</span>
            </button>
        `).join("");
    }

    function renderOrders(filter = "all") {
        if (!orderList || !queueCount) {
            return;
        }

        const visibleOrders = filter === "all"
            ? dashboardData.orders
            : dashboardData.orders.filter((order) => order.status === filter);

        queueCount.textContent = `${visibleOrders.length} đơn`;

        if (!visibleOrders.length) {
            orderList.innerHTML = '<p class="empty-state">Chưa có đơn đang xử lý.</p>';
            return;
        }

        orderList.innerHTML = visibleOrders.map((order) => `
            <article class="order-item">
                <div class="order-topline">
                    <strong>${order.code}</strong>
                    <span class="status status-${order.status === "ready" ? "empty" : "waiting"}">${order.label}</span>
                </div>
                <div class="order-meta">
                    <span>${order.table}</span>
                    <span>${order.items} món</span>
                    <span>${order.time}</span>
                </div>
            </article>
        `).join("");
    }

    filterButtons.forEach((button) => {
        button.addEventListener("click", () => {
            filterButtons.forEach((item) => item.classList.remove("is-active"));
            button.classList.add("is-active");
            renderOrders(button.dataset.filter);
        });
    });

    navButtons.forEach((button) => {
        button.addEventListener("click", () => {
            window.location.href = button.dataset.page;
        });
    });

    renderMetrics();
    renderTables();
    renderOrders();
})();
