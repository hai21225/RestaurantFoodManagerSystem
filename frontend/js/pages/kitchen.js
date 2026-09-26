(function () {
    const kitchenData = window.kitchenData || {
        stations: [],
        priorities: [],
        statuses: [],
        tickets: []
    };
    const currentRole = window.localStorage.getItem("restaurant.currentRole") || "staff";
    const canUpdateKitchen = currentRole === "kitchen" || currentRole === "admin";

    const state = {
        station: "all",
        priority: "all",
        status: kitchenData.statuses[0]?.value || "pending",
        tickets: kitchenData.tickets.map((ticket) => {
            const items = ticket.items.map((item) => ({
                ...item,
                completed: item.completed ?? ticket.status === "ready"
            }));
            const status = items.length && items.every((item) => item.completed)
                ? "ready"
                : items.some((item) => item.completed) || ticket.status === "ready"
                    ? "preparing"
                    : ticket.status;

            return { ...ticket, items, status };
        })
    };

    const kitchenSummary = document.querySelector("#kitchenSummary");
    const stationFilters = document.querySelector("#stationFilters");
    const priorityFilters = document.querySelector("#priorityFilters");
    const kitchenStatusTabs = document.querySelector("#kitchenStatusTabs");
    const kitchenBoard = document.querySelector("#kitchenBoard");
    const kitchenAnnouncement = document.querySelector("#kitchenAnnouncement");
    const refreshKitchenButton = document.querySelector("#refreshKitchenButton");
    const kitchenAccessNote = document.querySelector("#kitchenAccessNote");
    const kitchenRoleLabel = document.querySelector("#kitchenRoleLabel");
    const navButtons = document.querySelectorAll("[data-page]");

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/"/g, "&quot;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;");
    }

    function getFilteredTickets() {
        return state.tickets.filter((ticket) => {
            const matchStation = state.station === "all" || ticket.station === state.station;
            const matchPriority = state.priority === "all" || ticket.priority === state.priority;

            return matchStation && matchPriority;
        });
    }

    function renderSummary() {
        const summary = [
            { label: "Tổng phiếu bếp", value: state.tickets.length },
            { label: "Chờ nhận", value: state.tickets.filter((ticket) => ticket.status === "pending").length },
            { label: "Đang chế biến", value: state.tickets.filter((ticket) => ticket.status === "preparing").length },
            { label: "Đã xong", value: state.tickets.filter((ticket) => ticket.status === "ready").length }
        ];

        kitchenSummary.innerHTML = summary.map((item) => `
            <article class="kitchen-summary-card">
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
        renderFilterButtons(stationFilters, kitchenData.stations, state.station, (value) => {
            state.station = value;
            render();
        });

        renderFilterButtons(priorityFilters, kitchenData.priorities, state.priority, (value) => {
            state.priority = value;
            render();
        });
    }

    function renderStatusTabs() {
        const filteredTickets = getFilteredTickets();

        kitchenStatusTabs.innerHTML = kitchenData.statuses.map((status) => {
            const count = filteredTickets.filter((ticket) => ticket.status === status.value).length;

            return `
                <button
                    class="status-tab${status.value === state.status ? " is-active" : ""}"
                    type="button"
                    data-status-value="${status.value}"
                    aria-pressed="${status.value === state.status}"
                >
                    <strong>${status.label}</strong>
                    <span>${count}</span>
                </button>
            `;
        }).join("");

        kitchenStatusTabs.querySelectorAll("[data-status-value]").forEach((button) => {
            button.addEventListener("click", () => {
                state.status = button.dataset.statusValue;
                renderStatusTabs();
                renderBoard();
            });
        });
    }

    function renderTicket(ticket) {
        const completedCount = ticket.items.filter((item) => item.completed).length;
        const disableCompletion = !canUpdateKitchen || ticket.status === "pending";
        const disableStart = !canUpdateKitchen || !ticket.items.length;

        return `
            <article class="kitchen-ticket">
                <div class="ticket-topline">
                    <div class="ticket-code">
                        <strong>${escapeHtml(ticket.code)}</strong>
                        <span>${escapeHtml(ticket.orderCode)} - ${escapeHtml(ticket.tableName)}</span>
                    </div>
                    <span class="status priority-${escapeHtml(ticket.priority)}">${escapeHtml(ticket.priorityLabel)}</span>
                </div>

                <div class="ticket-meta">
                    <span>${escapeHtml(ticket.stationLabel)}</span>
                    <span>${escapeHtml(ticket.sentAt)}</span>
                </div>

                <ul class="ticket-items">
                    ${ticket.items.map((item, index) => `
                        <li class="ticket-item${item.completed ? " is-complete" : ""}">
                            <label class="ticket-item-check">
                                <input
                                    type="checkbox"
                                    data-ticket-id="${escapeHtml(ticket.id)}"
                                    data-item-index="${index}"
                                    aria-label="Hoàn thành ${escapeHtml(item.name)}, ${escapeHtml(item.quantity)} phần, ${escapeHtml(ticket.tableName)}"
                                    ${item.completed ? "checked" : ""}
                                    ${disableCompletion ? "disabled" : ""}
                                >
                                <span class="ticket-item-content">
                                    <strong>${escapeHtml(item.name)}</strong>
                                    ${item.note ? `<span>${escapeHtml(item.note)}</span>` : ""}
                                    ${item.completed ? '<span class="ticket-item-status">Đã xong</span>' : ""}
                                </span>
                                <strong class="ticket-item-quantity">x${escapeHtml(item.quantity)}</strong>
                            </label>
                        </li>
                    `).join("")}
                </ul>

                ${ticket.note ? `<p class="ticket-note">${escapeHtml(ticket.note)}</p>` : ""}

                <div class="ticket-actions">
                    <span class="ticket-progress">${completedCount}/${ticket.items.length} món đã xong</span>
                    ${ticket.status === "pending" ? `<button
                        class="ticket-action primary"
                        type="button"
                        data-start-ticket-id="${escapeHtml(ticket.id)}"
                        ${disableStart ? "disabled" : ""}
                    >
                        Bắt đầu làm
                    </button>` : ticket.status === "ready" ? '<span class="ticket-ready">Chờ phục vụ</span>' : ""}
                </div>
            </article>
        `;
    }

    function renderBoard() {
        const filteredTickets = getFilteredTickets();

        kitchenBoard.innerHTML = kitchenData.statuses.map((status) => {
            const tickets = filteredTickets.filter((ticket) => ticket.status === status.value);

            return `
                <section class="panel kitchen-column${status.value === state.status ? " is-active-status" : ""}" aria-labelledby="column-${status.value}">
                    <div class="kitchen-column-header">
                        <h3 id="column-${status.value}">${status.label}</h3>
                        <span>${tickets.length} phiếu</span>
                    </div>
                    <div class="ticket-list">
                        ${tickets.length ? tickets.map(renderTicket).join("") : '<p class="empty-column">Chưa có phiếu nào.</p>'}
                    </div>
                </section>
            `;
        }).join("");

        kitchenBoard.querySelectorAll("[data-start-ticket-id]").forEach((button) => {
            button.addEventListener("click", () => {
                startTicket(Number(button.dataset.startTicketId));
            });
        });

        kitchenBoard.querySelectorAll("[data-item-index]").forEach((input) => {
            input.addEventListener("change", () => {
                updateItemCompletion(Number(input.dataset.ticketId), Number(input.dataset.itemIndex), input.checked);
            });
        });
    }

    function startTicket(ticketId) {
        if (!canUpdateKitchen) {
            kitchenAnnouncement.textContent = "Bạn chỉ có quyền xem phiếu bếp.";
            return;
        }

        const ticket = state.tickets.find((item) => item.id === ticketId);

        if (!ticket || ticket.status !== "pending" || ticket.items.length === 0) {
            return;
        }

        ticket.status = "preparing";
        state.status = "preparing";
        render();
        kitchenAnnouncement.textContent = `${ticket.code} đang chế biến.`;
        restoreItemFocus(ticketId, 0);
    }

    function updateItemCompletion(ticketId, itemIndex, completed) {
        if (!canUpdateKitchen) {
            kitchenAnnouncement.textContent = "Chỉ nhân viên bếp mới được tích món hoàn thành.";
            render();
            return;
        }

        const ticket = state.tickets.find((item) => item.id === ticketId);
        const item = ticket?.items[itemIndex];

        if (!item || ticket.status === "pending") {
            return;
        }

        item.completed = completed;
        ticket.status = ticket.items.every((dish) => dish.completed) ? "ready" : "preparing";
        render();
        kitchenAnnouncement.textContent = ticket.status === "ready"
            ? `${ticket.code}, ${ticket.tableName}: tất cả món đã xong.`
            : `${item.name}, ${ticket.tableName}: ${completed ? "đã xong" : "đang chế biến"}.`;
        restoreItemFocus(ticketId, itemIndex);
    }

    function restoreItemFocus(ticketId, itemIndex) {
        const input = kitchenBoard.querySelector(`[data-ticket-id="${ticketId}"][data-item-index="${itemIndex}"]`);

        if (input?.getClientRects().length) {
            input.focus({ preventScroll: true });
        } else {
            kitchenStatusTabs.querySelector(`[data-status-value="${state.status}"]`)?.focus({ preventScroll: true });
        }
    }

    function setupNavigation() {
        navButtons.forEach((button) => {
            button.addEventListener("click", () => {
                window.location.href = button.dataset.page;
            });
        });
    }

    function render() {
        if (kitchenRoleLabel) {
            kitchenRoleLabel.textContent = canUpdateKitchen ? "Quyền bếp" : "Quyền xem";
        }

        if (kitchenAccessNote) {
            kitchenAccessNote.textContent = canUpdateKitchen
                ? "Bạn có thể nhận phiếu và tích món đã hoàn thành."
                : "Bạn có thể xem phiếu bếp, nhưng chỉ nhân viên bếp mới được tích món hoàn thành.";
        }

        renderSummary();
        renderFilters();
        renderStatusTabs();
        renderBoard();
    }

    refreshKitchenButton.addEventListener("click", render);

    setupNavigation();
    render();
})();
