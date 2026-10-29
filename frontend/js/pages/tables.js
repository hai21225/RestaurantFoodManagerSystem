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
        if (!isActive) return { value: "inactive", label: "Bảo trì" };
        const value = String(status || "").toLowerCase();
        if (value === "occupied") return { value: "serving", label: "Đang sử dụng" };
        if (value === "maintenance") return { value: "inactive", label: "Bảo trì" };
        return { value: "empty", label: "Trống" };
    }

    function populateAreaOptions() {
        const select = tableForm.elements.areaId;
        if (!select) {
            return;
        }

        const areas = tablesData.areas.filter((area) =>
            area.value !== "all" && area.isActive !== false && /^\d+$/.test(area.value));
        select.innerHTML = areas.length
            ? areas.map((area) => `<option value="${area.value}">${area.label}</option>`).join("")
            : '<option value="">Chưa có khu vực</option>';
    }

    async function loadTables() {
        refreshTablesButton.disabled = true;
        refreshTablesButton.textContent = "Đang tải...";
        try {
            const [result, areaResult] = await Promise.all([
                window.restaurantApi.get("/restaurant-tables"),
                window.restaurantApi.get("/table-areas?includeInactive=true").catch(() => [])
            ]);

            const areas = [{ value: "all", label: "Tất cả", isActive: true }];
            for (const area of areaResult) {
                areas.push({
                    value: String(area.areaId),
                    label: area.areaName,
                    isActive: area.isActive
                });
            }
            for (const table of result) {
                if (!table.areaId) {
                    if (!areas.some((area) => area.value === "unassigned")) {
                        areas.push({ value: "unassigned", label: "Chưa phân khu", isActive: false });
                    }
                    continue;
                }

                const value = String(table.areaId);
                if (!areas.some((area) => area.value === value)) {
                    areas.push({
                        value,
                        label: table.areaName || `Khu vực ${value}`,
                        isActive: true
                    });
                }
            }
            tablesData.areas = areas;

            tablesData.tables = result.map((table) => {
                const status = normalizeStatus(table.status, table.isActive);
                return {
                    id: table.tableId,
                    name: `Bàn ${table.tableNumber}`,
                    area: table.areaId ? String(table.areaId) : "unassigned",
                    areaLabel: table.areaName || "Chưa phân khu",
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

            if (!tablesData.areas.some((area) => area.value === state.area)) {
                state.area = "all";
            }

            const selectionStillExists = tablesData.tables.some((table) => table.id === state.selectedTableId);
            if (!selectionStillExists) {
                state.selectedTableId = tablesData.tables.find((table) => table.isActive)?.id
                    || tablesData.tables[0]?.id
                    || null;
            }

            populateAreaOptions();
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
            { label: "Đang sử dụng", value: tablesData.tables.filter((table) => table.status === "serving").length },
            { label: "Bảo trì", value: tablesData.tables.filter((table) => table.status === "inactive").length }
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
        const isActive = table.isActive;
        const isMaintenance = table.status === "inactive";
        const canOrder = isActive && !isMaintenance;
        const canChangeState = isActive && table.status !== "serving";
        const orderAction = canOrder
            ? `<button class="primary-action" type="button" data-action="order-table">
                    ${table.status === "empty" ? "Đặt món cho bàn này" : "Xem / thêm món"}
               </button>`
            : "";
        const maintenanceAction = isActive && table.status === "empty"
            ? `<button class="secondary-action" type="button" data-action="maintenance-table">Bảo trì</button>`
            : isActive && isMaintenance
                ? `<button class="secondary-action" type="button" data-action="end-maintenance">Kết thúc bảo trì</button>`
                : "";
        const lifecycleAction = !isActive
            ? `<button class="primary-action" type="button" data-action="restore-table">Khôi phục bàn</button>`
            : `<button class="danger-action" type="button" data-action="delete-table"
                    ${canChangeState ? "" : "disabled title=\"Bàn đang có đơn chưa thanh toán\""}>
                    Xóa bàn
               </button>`;
        const actionHint = !isActive
            ? '<p class="table-action-hint">Bàn đang ngừng sử dụng. Khôi phục để phục vụ lại.</p>'
            : table.status === "serving"
                ? '<p class="table-action-hint">Bàn chỉ trở về trống sau khi thanh toán hoặc hủy đơn.</p>'
                : isMaintenance
                    ? '<p class="table-action-hint">Bàn đang bảo trì và không nhận đơn mới.</p>'
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
                    ${maintenanceAction}
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

    let lifecycleMode = "delete";

    function openTableLifecycleDialog(mode) {
        const table = tablesData.tables.find((item) => item.id === state.selectedTableId);
        if (!table) {
            return;
        }

        const config = {
            delete: {
                title: "Xóa bàn khỏi danh sách sử dụng",
                notice: "Bàn sẽ chuyển sang Bảo trì và vẫn hiển thị trên sơ đồ. Lịch sử đơn hàng và thông tin bàn vẫn được giữ lại.",
                confirm: "Xóa bàn",
                className: "danger-action"
            },
            restore: {
                title: "Khôi phục bàn",
                notice: "Bàn sẽ trở lại trạng thái trống và có thể được nhân viên chọn để đặt món.",
                confirm: "Khôi phục bàn",
                className: "primary-action"
            },
            maintenance: {
                title: "Đưa bàn vào bảo trì",
                notice: "Bàn sẽ tạm ngừng nhận đơn nhưng vẫn hiển thị trên sơ đồ với trạng thái Bảo trì.",
                confirm: "Bảo trì",
                className: "secondary-action"
            },
            "end-maintenance": {
                title: "Kết thúc bảo trì",
                notice: "Bàn sẽ trở lại trạng thái trống và có thể nhận đơn mới.",
                confirm: "Kết thúc bảo trì",
                className: "primary-action"
            }
        }[mode];

        if (!config) {
            return;
        }

        lifecycleMode = mode;
        tableLifecycleTitle.textContent = config.title;
        tableLifecycleName.textContent = table.name;
        tableLifecycleNotice.textContent = config.notice;
        tableLifecycleMessage.textContent = "";
        tableLifecycleMessage.className = "form-message";
        tableLifecycleConfirm.textContent = config.confirm;
        tableLifecycleConfirm.className = config.className;
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
        populateAreaOptions();
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

        const areaValue = data.get("areaId");

        try {
            await window.restaurantApi.post("/restaurant-tables", {
                tableNumber: data.get("tableNumber").trim(),
                capacity: Number(data.get("capacity")),
                areaId: areaValue ? Number(areaValue) : null
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

        const action = event.target.closest("[data-action]")?.dataset.action;
        if (action === "delete-table") {
            openTableLifecycleDialog("delete");
        } else if (action === "restore-table") {
            openTableLifecycleDialog("restore");
        } else if (action === "maintenance-table") {
            openTableLifecycleDialog("maintenance");
        } else if (action === "end-maintenance") {
            openTableLifecycleDialog("end-maintenance");
        }
    });

    tableLifecycleConfirm.addEventListener("click", async () => {
        const table = tablesData.tables.find((item) => item.id === state.selectedTableId);
        if (!table) {
            return;
        }

        const progressLabels = {
            delete: "Đang xóa...",
            restore: "Đang khôi phục...",
            maintenance: "Đang cập nhật...",
            "end-maintenance": "Đang cập nhật..."
        };
        const doneLabels = {
            delete: "Xóa bàn",
            restore: "Khôi phục bàn",
            maintenance: "Bảo trì",
            "end-maintenance": "Kết thúc bảo trì"
        };

        tableLifecycleConfirm.disabled = true;
        tableLifecycleConfirm.textContent = progressLabels[lifecycleMode] || "Đang xử lý...";

        try {
            if (lifecycleMode === "delete") {
                await window.restaurantApi.delete(`/restaurant-tables/${table.id}`);
            } else if (lifecycleMode === "maintenance") {
                await window.restaurantApi.patch(`/restaurant-tables/${table.id}/maintenance?enabled=true`);
            } else {
                await window.restaurantApi.patch(`/restaurant-tables/${table.id}/maintenance?enabled=false`);
            }
            tableLifecycleDialog.close();
            await loadTables();
        } catch (error) {
            tableLifecycleMessage.textContent = error.message;
            tableLifecycleMessage.className = "form-message is-error";
        } finally {
            tableLifecycleConfirm.disabled = false;
            tableLifecycleConfirm.textContent = doneLabels[lifecycleMode] || "Xử lý";
        }
    });

    render();
    loadTables();
})();
