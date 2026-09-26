(function () {
    const foodsData = window.foodsData || {
        categories: [],
        statuses: [],
        foods: []
    };

    const state = {
        category: "all",
        status: "all",
        searchTerm: "",
        selectedFoodId: foodsData.foods[0]?.id || null,
        foods: foodsData.foods.map((food) => ({ ...food }))
    };

    const foodsSummary = document.querySelector("#foodsSummary");
    const categoryFilters = document.querySelector("#categoryFilters");
    const statusFilters = document.querySelector("#statusFilters");
    const foodSearchInput = document.querySelector("#foodSearchInput");
    const visibleFoodCount = document.querySelector("#visibleFoodCount");
    const foodList = document.querySelector("#foodList");
    const foodDetailPanel = document.querySelector(".food-detail-panel");
    const selectedFoodStatus = document.querySelector("#selectedFoodStatus");
    const foodDetail = document.querySelector("#foodDetail");
    const closeFoodDetailButton = document.querySelector("#closeFoodDetailButton");
    const addFoodButton = document.querySelector("#addFoodButton");
    const navButtons = document.querySelectorAll("[data-page]");

    function formatCurrency(value) {
        return `${value.toLocaleString("vi-VN")}đ`;
    }

    function getStatusLabel(status) {
        return foodsData.statuses.find((item) => item.value === status)?.label || status;
    }

    function getSelectedFood() {
        return state.foods.find((food) => food.id === state.selectedFoodId);
    }

    function getFilteredFoods() {
        const searchTerm = state.searchTerm.toLowerCase();

        return state.foods.filter((food) => {
            const matchCategory = state.category === "all" || food.category === state.category;
            const matchStatus = state.status === "all" || food.status === state.status;
            const matchSearch = food.name.toLowerCase().includes(searchTerm)
                || food.code.toLowerCase().includes(searchTerm);

            return matchCategory && matchStatus && matchSearch;
        });
    }

    function renderSummary() {
        const availableCount = state.foods.filter((food) => food.status === "available").length;
        const outCount = state.foods.filter((food) => food.status === "out").length;
        const featuredCount = state.foods.filter((food) => food.isFeatured).length;
        const newCount = state.foods.filter((food) => food.isNew).length;
        const summary = [
            { label: "Tổng số món", value: state.foods.length },
            { label: "Đang bán", value: availableCount },
            { label: "Tạm hết", value: outCount },
            { label: "Nổi bật / Mới", value: `${featuredCount}/${newCount}` }
        ];

        foodsSummary.innerHTML = summary.map((item) => `
            <article class="foods-summary-card">
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
        renderFilterButtons(categoryFilters, foodsData.categories, state.category, (value) => {
            state.category = value;
            render();
        });

        renderFilterButtons(statusFilters, foodsData.statuses, state.status, (value) => {
            state.status = value;
            render();
        });
    }

    function getFoodInitials(name) {
        return name
            .split(" ")
            .slice(0, 2)
            .map((word) => word.charAt(0))
            .join("")
            .toUpperCase();
    }

    function renderFlags(food) {
        const flags = [];

        if (food.isFeatured) {
            flags.push("Nổi bật");
        }

        if (food.isNew) {
            flags.push("Món mới");
        }

        if (flags.length === 0) {
            return "";
        }

        return `<div class="flag-list">${flags.map((flag) => `<span class="flag">${flag}</span>`).join("")}</div>`;
    }

    function renderFoodList() {
        const foods = getFilteredFoods();

        visibleFoodCount.textContent = `${foods.length} món`;

        if (foods.length === 0) {
            foodList.innerHTML = '<p class="empty-state">Không tìm thấy món phù hợp.</p>';
            return;
        }

        foodList.innerHTML = foods.map((food) => `
            <button
                class="food-row${food.id === state.selectedFoodId ? " is-selected" : ""}"
                type="button"
                data-food-id="${food.id}"
            >
                <span class="food-thumb" aria-hidden="true">${getFoodInitials(food.name)}</span>
                <span class="food-row-main">
                    <strong>${food.name}</strong>
                    <span>${food.code} - ${food.description}</span>
                    ${renderFlags(food)}
                </span>
                <span class="food-category">${food.categoryLabel}</span>
                <span class="food-price">${formatCurrency(food.price)}</span>
                <span class="status status-${food.status === "available" ? "empty" : food.status === "out" ? "waiting" : "payment"}">${food.statusLabel}</span>
            </button>
        `).join("");

        foodList.querySelectorAll("[data-food-id]").forEach((button) => {
            button.addEventListener("click", () => {
                state.selectedFoodId = Number(button.dataset.foodId);
                renderFoodList();
                renderFoodDetail();
                openFoodDetail();
            });
        });
    }

    function renderFoodDetail() {
        const food = getSelectedFood();

        if (!food) {
            selectedFoodStatus.textContent = "Chưa chọn";
            foodDetail.innerHTML = "<p>Chọn một món để xem thông tin và thao tác.</p>";
            return;
        }

        selectedFoodStatus.textContent = food.statusLabel;
        foodDetail.innerHTML = `
            <div class="detail-title">
                <strong>${food.name}</strong>
                <span>${food.code} - ${food.categoryLabel}</span>
            </div>

            <div class="detail-grid">
                <div class="detail-item">
                    <span>Giá bán</span>
                    <strong>${formatCurrency(food.price)}</strong>
                </div>
                <div class="detail-item">
                    <span>Giá vốn</span>
                    <strong>${formatCurrency(food.cost)}</strong>
                </div>
                <div class="detail-item">
                    <span>Thời gian chế biến</span>
                    <strong>${food.prepTime} phút</strong>
                </div>
                <div class="detail-item">
                    <span>Đơn vị tính</span>
                    <strong>${food.unit}</strong>
                </div>
            </div>

            <p>${food.description}</p>

            <p class="info-list-title">Size / giá</p>
            <div class="size-list">
                ${food.sizes.map((size) => `
                    <div class="size-item">
                        <span>${size.name}</span>
                        <strong>${formatCurrency(size.price)}</strong>
                    </div>
                `).join("")}
            </div>

            <p class="info-list-title">Lựa chọn / topping</p>
            <div class="topping-list">
                ${food.toppings.map((topping) => `
                    <div class="topping-item">
                        <span>${topping}</span>
                    </div>
                `).join("")}
            </div>

            <div class="detail-actions">
                <button class="primary-action" type="button" data-action="toggle-status">
                    ${food.status === "available" ? "Chuyển tạm hết" : "Bật đang bán"}
                </button>
                <button class="secondary-action" type="button" data-action="toggle-featured">
                    ${food.isFeatured ? "Bỏ nổi bật" : "Đánh dấu nổi bật"}
                </button>
                <button class="secondary-action" type="button" data-action="toggle-new">
                    ${food.isNew ? "Bỏ món mới" : "Đánh dấu món mới"}
                </button>
            </div>
        `;

        foodDetail.querySelectorAll("[data-action]").forEach((button) => {
            button.addEventListener("click", () => handleDetailAction(button.dataset.action));
        });
    }

    function handleDetailAction(action) {
        const food = getSelectedFood();

        if (!food) {
            return;
        }

        if (action === "toggle-status") {
            food.status = food.status === "available" ? "out" : "available";
            food.statusLabel = getStatusLabel(food.status);
        }

        if (action === "toggle-featured") {
            food.isFeatured = !food.isFeatured;
        }

        if (action === "toggle-new") {
            food.isNew = !food.isNew;
        }

        renderSummary();
        renderFoodList();
        renderFoodDetail();
    }

    function setupNavigation() {
        navButtons.forEach((button) => {
            button.addEventListener("click", () => {
                window.location.href = button.dataset.page;
            });
        });
    }

    function openFoodDetail() {
        foodDetailPanel.classList.add("is-open");
    }

    function closeFoodDetail() {
        foodDetailPanel.classList.remove("is-open");
    }

    function render() {
        renderSummary();
        renderFilters();
        renderFoodList();
        renderFoodDetail();
    }

    foodSearchInput.addEventListener("input", () => {
        state.searchTerm = foodSearchInput.value.trim();
        renderFoodList();
    });

    addFoodButton.addEventListener("click", () => {
        addFoodButton.textContent = "Chưa kết nối form";

        window.setTimeout(() => {
            addFoodButton.textContent = "Thêm món";
        }, 1400);
    });

    closeFoodDetailButton.addEventListener("click", closeFoodDetail);

    setupNavigation();
    render();
})();
