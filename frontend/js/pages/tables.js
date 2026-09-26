(function () {
    const tablesData = window.tablesData || {
        areas: [],
        statuses: [],
        tables: []
    };

    const state = {
        area: "all",
        status: "all",
        selectedTableId: tablesData.tables[0]?.id || null
    };

    const tablesSummary = document.querySelector("#tablesSummary");
    const areaFilters = document.querySelector("#areaFilters");
    const statusFilters = document.querySelector("#statusFilters");
    const restaurantMap = document.querySelector("#restaurantMap");
    const visibleTableCount = document.querySelector("#visibleTableCount");
    const selectedTable = document.querySelector("#selectedTable");
    const tableDetail = document.querySelector(".table-detail");
    const selectedTableStatus = document.querySelector("#selectedTableStatus");
    const closeTableDetailButton = document.querySelector("#closeTableDetailButton");
    const refreshTablesButton = document.querySelector("#refreshTablesButton");
    const navButtons = document.querySelectorAll("[data-page]");

    function formatCurrency(value) {
        return `${value.toLocaleString("vi-VN")}đ`;
    }

    function getFilteredTables() {
        return tablesData.tables.filter((table) => {
            const matchArea = state.area === "all" || table.area === state.area;
            const matchStatus = state.status === "all" || table.status === state.status;

            return matchArea && matchStatus;
        });
    }

    function renderSummary() {
        const summary = [
            { label: "Tổng số bàn", value: tablesData.tables.length },
            { label: "Bàn trống", value: tablesData.tables.filter((table) => table.status === "empty").length },
            { label: "Đang phục vụ", value: tablesData.tables.filter((table) => table.status === "serving").length },
            { label: "Cần xử lý", value: tablesData.tables.filter((table) => table.status === "waiting" || table.status === "payment").length }
        ];

        tablesSummary.innerHTML = summary.map((item) => `
            <article class="table-summary-card">
                <span>${item.label}</span>
                <strong>${item.value}</strong>
            </article>
        `).join("");
    }

    function renderFilterButtons(container, items, activeValue, onClick) {
        container.innerHTML = items.map((item) => `
            <button
                class="segment-button${item.value === activeValue ? " is-active" : ""}"
                type="button"
                data-value="${item.value}"
            >
                ${item.label}
            </button>
        `).join("");

        container.querySelectorAll(".segment-button").forEach((button) => {
            button.addEventListener("click", () => onClick(button.dataset.value));
        });
    }

    function renderFilters() {
        renderFilterButtons(areaFilters, tablesData.areas, state.area, (value) => {
            state.area = value;
            render();
        });

        renderFilterButtons(statusFilters, tablesData.statuses, state.status, (value) => {
            state.status = value;
            render();
        });
    }

    function renderTableCards() {
        const filteredTables = getFilteredTables();

        visibleTableCount.textContent = `${filteredTables.length} bàn`;

        if (!filteredTables.length) {
            restaurantMap.innerHTML = `
                <div class="empty-state">
                    <strong>Chưa có dữ liệu bàn</strong>
                    <span>Dữ liệu bàn sẽ hiển thị tại đây sau khi kết nối nguồn dữ liệu thật.</span>
                </div>
            `;
            return;
        }

        restaurantMap.innerHTML = filteredTables.map((table) => `
            <button
                class="restaurant-table-card status-${table.status}${table.id === state.selectedTableId ? " is-selected" : ""}"
                type="button"
                data-table-id="${table.id}"
            >
                <div class="table-card-header">
                    <strong>${table.name}</strong>
                    <span class="status status-${table.status}">${table.statusLabel}</span>
                </div>
                <span class="table-area">${table.areaLabel}</span>
                <span class="table-seats">${table.seats} chỗ - ${table.guestCount || 0} khách</span>
                <div class="table-card-footer">
                    <span class="table-order-code">${table.orderCode || "Chưa có đơn"}</span>
                    <span class="table-time">${table.updatedAt}</span>
                </div>
            </button>
        `).join("");

        restaurantMap.querySelectorAll("[data-table-id]").forEach((button) => {
            button.addEventListener("click", () => {
                state.selectedTableId = Number(button.dataset.tableId);
                renderTableCards();
                renderSelectedTable();
                openTableDetail();
            });
        });
    }

    function renderSelectedTable() {
        const table = tablesData.tables.find((item) => item.id === state.selectedTableId);

        if (!table) {
            selectedTableStatus.textContent = "Chưa chọn";
            selectedTable.innerHTML = "<p>Chọn một bàn để xem chi tiết và thao tác tiếp theo.</p>";
            return;
        }

        selectedTableStatus.textContent = table.statusLabel;
        selectedTable.innerHTML = `
            <div class="table-detail-body">
                <div class="detail-title">
                    <strong>${table.name}</strong>
                    <span>${table.areaLabel}</span>
                </div>

                <div class="detail-grid">
                    <div class="detail-item">
                        <span>Số ghế</span>
                        <strong>${table.seats}</strong>
                    </div>
                    <div class="detail-item">
                        <span>Khách hiện tại</span>
                        <strong>${table.guestCount}</strong>
                    </div>
                    <div class="detail-item">
                        <span>Mã đơn</span>
                        <strong>${table.orderCode || "Chưa có"}</strong>
                    </div>
                    <div class="detail-item">
                        <span>Tạm tính</span>
                        <strong>${formatCurrency(table.totalAmount)}</strong>
                    </div>
                </div>

                <div class="detail-actions">
                    <button class="primary-action" type="button" data-action="order-table">
                        ${table.status === "empty" ? "Đặt món cho bàn này" : "Xem / thêm món"}
                    </button>
                    <button class="secondary-action" type="button">Chuyển bàn</button>
                    <button class="secondary-action" type="button">Cập nhật trạng thái</button>
                </div>
            </div>
        `;
    }

    function saveSelectedTableForOrder() {
        const table = tablesData.tables.find((item) => item.id === state.selectedTableId);

        if (!table) {
            return;
        }

        window.localStorage.setItem("restaurant.selectedTable", JSON.stringify({
            id: table.id,
            name: table.name,
            areaLabel: table.areaLabel,
            guestCount: table.guestCount || 0,
            orderCode: table.orderCode || "",
            status: table.status,
            statusLabel: table.statusLabel
        }));

        window.location.href = `order.html?tableId=${encodeURIComponent(table.id)}`;
    }

    function openTableDetail() {
        tableDetail.classList.add("is-open");
    }

    function closeTableDetail() {
        tableDetail.classList.remove("is-open");
    }

    function setupNavigation() {
        navButtons.forEach((button) => {
            button.addEventListener("click", () => {
                window.location.href = button.dataset.page;
            });
        });
    }

    function render() {
        renderSummary();
        renderFilters();
        renderTableCards();
        renderSelectedTable();
    }

    refreshTablesButton.addEventListener("click", render);

    closeTableDetailButton.addEventListener("click", closeTableDetail);

    selectedTable.addEventListener("click", (event) => {
        if (event.target.closest('[data-action="order-table"]')) {
            saveSelectedTableForOrder();
        }
    });

    setupNavigation();
    render();
})();
