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
    const addTableButton = document.querySelector("#addTableButton");
    const tableDialog = document.querySelector("#tableDialog");
    const tableForm = document.querySelector("#tableForm");
    const tableSubmit = document.querySelector("#tableSubmit");
    const tableFormMessage = document.querySelector("#tableFormMessage");
    const tableLifecycleDialog = document.querySelector("#tableLifecycleDialog");
    const tableLifecycleTitle = document.querySelector("#tableLifecycleTitle");
    const tableLifecycleName = document.querySelector("#tableLifecycleName");
    const tableLifecycleNotice = document.querySelector("#tableLifecycleNotice");
    const tableLifecycleMessage = document.querySelector("#tableLifecycleMessage");
    const tableLifecycleConfirm = document.querySelector("#tableLifecycleConfirm");

    function normalizeStatus(status, isActive) {
        if (!isActive) return { value: "inactive", label: "Ngừng sử dụng" };
        const value = String(status || "").toLowerCase();
        if (value === "occupied") return { value: "serving", label: "Đang phục vụ" };
        if (value === "maintenance") return { value: "inactive", label: "Ngừng sử dụng" };
        return { value: "empty", label: "Trống" };
    }

    async function loadTables() {
        refreshTablesButton.disabled = true;
        refreshTablesButton.textContent = "Đang tải...";
        try {
            const result = await window.restaurantApi.get("/restaurant-tables");
            tablesData.tables = result.map((table) => {
                const status = normalizeStatus(table.status, table.isActive);
                return {
                    id: table.tableId,
                    name: `Bàn ${table.tableNumber}`,
                    area: "main",
                    areaLabel: "Khu vực chung",
                    seats: table.capacity,
                    guestCount: 0,
                    orderCode: "",
                    totalAmount: 0,
                    updatedAt: "",
                    isActive: table.isActive,
                    status: status.value,
                    statusLabel: status.label
                };
            });
            const selectionStillExists = tablesData.tables.some((table) => table.id === state.selectedTableId);
            if (!selectionStillExists) {
                state.selectedTableId = tablesData.tables.find((table) => table.isActive)?.id
                    || tablesData.tables[0]?.id
                    || null;
            }
            render();
        } catch (error) {
            tablesData.tables = [];
            render();
            restaurantMap.innerHTML = `<div class="empty-state"><strong>Không tải được dữ liệu bàn</strong><span>${error.message}</span></div>`;
        } finally {
            refreshTablesButton.disabled = false;
            refreshTablesButton.textContent = "Làm mới";
        }
    }

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
            { label: "Ngừng sử dụng", value: tablesData.tables.filter((table) => !table.isActive).length }
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
        const canDeactivate = table.isActive && table.status === "empty";
        const orderAction = table.isActive
            ? `<button class="primary-action" type="button" data-action="order-table">
                    ${table.status === "empty" ? "Đặt món cho bàn này" : "Xem / thêm món"}
               </button>`
            : "";
        const lifecycleAction = table.isActive
            ? `<button class="danger-action" type="button" data-action="toggle-active"
                    ${canDeactivate ? "" : "disabled title=\"Bàn đang có đơn chưa hoàn tất\""}>
                    Xóa khỏi danh sách bàn
               </button>`
            : `<button class="secondary-action" type="button" data-action="toggle-active">Khôi phục bàn</button>`;
        const actionHint = table.isActive && !canDeactivate
            ? '<p class="table-action-hint">Hoàn tất hoặc hủy đơn hiện tại trước khi xóa bàn.</p>'
            : "";

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
                    ${orderAction}
                    ${lifecycleAction}
                </div>
                ${actionHint}
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

    function openTableLifecycleDialog() {
        const table = tablesData.tables.find((item) => item.id === state.selectedTableId);
        if (!table) {
            return;
        }

        const restoring = !table.isActive;
        tableLifecycleTitle.textContent = restoring ? "Khôi phục bàn" : "Xóa bàn khỏi danh sách sử dụng";
        tableLifecycleName.textContent = table.name;
        tableLifecycleNotice.textContent = restoring
            ? "Bàn sẽ trở lại trạng thái trống và có thể được nhân viên chọn để đặt món."
            : "Bàn sẽ chuyển sang Ngừng sử dụng. Lịch sử đơn hàng và thông tin bàn vẫn được giữ lại.";
        tableLifecycleMessage.textContent = "";
        tableLifecycleMessage.className = "form-message";
        tableLifecycleConfirm.textContent = restoring ? "Khôi phục bàn" : "Xóa bàn";
        tableLifecycleConfirm.className = restoring ? "primary-action" : "danger-action";
        tableLifecycleDialog.showModal();
    }

    function render() {
        renderSummary();
        renderFilters();
        renderTableCards();
        renderSelectedTable();
    }

    refreshTablesButton.addEventListener("click", loadTables);

    addTableButton.addEventListener("click", () => {
        tableForm.reset();
        tableForm.elements.capacity.value = "4";
        tableFormMessage.textContent = "";
        tableFormMessage.className = "form-message";
        tableDialog.showModal();
        tableForm.elements.tableNumber.focus();
    });

    document.querySelectorAll("[data-close-table-dialog]").forEach((button) => {
        button.addEventListener("click", () => {
            tableDialog.close();
            addTableButton.focus();
        });
    });

    document.querySelectorAll("[data-close-table-lifecycle]").forEach((button) => {
        button.addEventListener("click", () => {
            tableLifecycleDialog.close();
        });
    });

    tableForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        tableFormMessage.textContent = "";

        if (!tableForm.checkValidity()) {
            tableForm.reportValidity();
            return;
        }

        const data = new FormData(tableForm);
        tableSubmit.disabled = true;
        tableSubmit.textContent = "Đang tạo...";

        try {
            await window.restaurantApi.post("/restaurant-tables", {
                tableNumber: data.get("tableNumber").trim(),
                capacity: Number(data.get("capacity"))
            });
            tableDialog.close();
            await loadTables();
        } catch (error) {
            tableFormMessage.textContent = error.message;
            tableFormMessage.className = "form-message is-error";
        } finally {
            tableSubmit.disabled = false;
            tableSubmit.textContent = "Tạo bàn";
        }
    });

    closeTableDetailButton.addEventListener("click", closeTableDetail);

    selectedTable.addEventListener("click", (event) => {
        if (event.target.closest('[data-action="order-table"]')) {
            saveSelectedTableForOrder();
        }

        if (event.target.closest('[data-action="toggle-active"]')) {
            openTableLifecycleDialog();
        }
    });

    tableLifecycleConfirm.addEventListener("click", async () => {
        const table = tablesData.tables.find((item) => item.id === state.selectedTableId);
        if (!table) {
            return;
        }

        const restoring = !table.isActive;
        tableLifecycleConfirm.disabled = true;
        tableLifecycleConfirm.textContent = restoring ? "Đang khôi phục..." : "Đang xóa...";

        try {
            if (restoring) {
                await window.restaurantApi.patch(`/restaurant-tables/${table.id}/maintenance?enabled=false`);
            } else {
                await window.restaurantApi.delete(`/restaurant-tables/${table.id}`);
            }
            tableLifecycleDialog.close();
            await loadTables();
        } catch (error) {
            tableLifecycleMessage.textContent = error.message;
            tableLifecycleMessage.className = "form-message is-error";
        } finally {
            tableLifecycleConfirm.disabled = false;
            tableLifecycleConfirm.textContent = restoring ? "Khôi phục bàn" : "Xóa bàn";
        }
    });

    render();
    loadTables();
})();
