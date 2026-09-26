(function () {
    const orderData = window.orderData || {
        table: {},
        categories: [],
        foods: []
    };

    const state = {
        categoryId: "all",
        searchTerm: "",
        cart: [],
        selectedTable: getSelectedTable()
    };

    const orderTitle = document.querySelector("#order-title");
    const orderContext = document.querySelector("#orderContext");
    const categoryList = document.querySelector("#categoryList");
    const categoryCount = document.querySelector("#categoryCount");
    const foodGrid = document.querySelector("#foodGrid");
    const foodCount = document.querySelector("#foodCount");
    const foodSearchInput = document.querySelector("#foodSearchInput");
    const cartList = document.querySelector("#cartList");
    const cartItemCount = document.querySelector("#cartItemCount");
    const subtotalAmount = document.querySelector("#subtotalAmount");
    const serviceAmount = document.querySelector("#serviceAmount");
    const totalAmount = document.querySelector("#totalAmount");
    const cartDock = document.querySelector("#cartDock");
    const cartDockCount = document.querySelector("#cartDockCount");
    const cartDockTotal = document.querySelector("#cartDockTotal");
    const cartPanel = document.querySelector(".cart-panel");
    const closeCartButton = document.querySelector("#closeCartButton");
    const chooseTableButton = document.querySelector("#chooseTableButton");
    const clearOrderButton = document.querySelector("#clearOrderButton");
    const sendKitchenButton = document.querySelector("#sendKitchenButton");
    const navButtons = document.querySelectorAll("[data-page]");

    function getSelectedTable() {
        const params = new URLSearchParams(window.location.search);
        const tableId = params.get("tableId");
        const savedTable = window.localStorage.getItem("restaurant.selectedTable");

        if (savedTable) {
            try {
                const table = JSON.parse(savedTable);

                if (!tableId || String(table.id) === tableId) {
                    return table;
                }
            } catch (error) {
                window.localStorage.removeItem("restaurant.selectedTable");
            }
        }

        if (orderData.table?.id) {
            return orderData.table;
        }

        if (tableId) {
            return {
                id: tableId,
                name: `Bàn ${tableId}`,
                areaLabel: "",
                guestCount: 0,
                orderCode: ""
            };
        }

        return null;
    }

    function formatCurrency(value) {
        return `${value.toLocaleString("vi-VN")}đ`;
    }

    function escapeHtml(value) {
        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/"/g, "&quot;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;");
    }

    function getFoodById(foodId) {
        return orderData.foods.find((food) => food.id === foodId);
    }

    function getFilteredFoods() {
        return orderData.foods.filter((food) => {
            const matchCategory = state.categoryId === "all" || food.categoryId === state.categoryId;
            const matchSearch = food.name.toLowerCase().includes(state.searchTerm.toLowerCase());

            return matchCategory && matchSearch;
        });
    }

    function renderCategories() {
        categoryCount.textContent = `${orderData.categories.length} mục`;

        categoryList.innerHTML = orderData.categories.map((category) => {
            const count = category.id === "all"
                ? orderData.foods.length
                : orderData.foods.filter((food) => food.categoryId === category.id).length;

            return `
                <button
                    class="category-button${category.id === state.categoryId ? " is-active" : ""}"
                    type="button"
                    data-category-id="${category.id}"
                >
                    <strong>${category.name}</strong>
                    <span>${count}</span>
                </button>
            `;
        }).join("");

        categoryList.querySelectorAll("[data-category-id]").forEach((button) => {
            button.addEventListener("click", () => {
                state.categoryId = button.dataset.categoryId;
                renderCategories();
                renderFoods();
            });
        });
    }

    function renderFoods() {
        const filteredFoods = getFilteredFoods();

        foodCount.textContent = `${filteredFoods.length} món`;

        if (!filteredFoods.length) {
            foodGrid.innerHTML = `
                <div class="empty-state">
                    <strong>Chưa có dữ liệu món</strong>
                    <span>Danh sách món sẽ hiển thị tại đây sau khi kết nối nguồn dữ liệu thật.</span>
                </div>
            `;
            return;
        }

        foodGrid.innerHTML = filteredFoods.map((food) => {
            const isDisabled = food.status !== "available" || !state.selectedTable;

            return `
                <button
                    class="food-card${isDisabled ? " is-disabled" : ""}"
                    type="button"
                    data-food-id="${food.id}"
                    ${isDisabled ? "disabled" : ""}
                >
                    <div class="food-card-top">
                        <h4>${food.name}</h4>
                        <span class="food-price">${formatCurrency(food.price)}</span>
                    </div>
                    <p>${food.description}</p>
                    <div class="food-meta">
                        <span>${food.prepTime} phút</span>
                        <span>${!state.selectedTable ? "Chọn bàn trước" : food.status !== "available" ? "Tạm hết" : "Đang bán"}</span>
                    </div>
                </button>
            `;
        }).join("");

        foodGrid.querySelectorAll("[data-food-id]").forEach((button) => {
            button.addEventListener("click", () => addFoodToCart(Number(button.dataset.foodId)));
        });
    }

    function addFoodToCart(foodId) {
        const food = getFoodById(foodId);

        if (!state.selectedTable || !food || food.status !== "available") {
            return;
        }

        const existingItem = state.cart.find((item) => item.foodId === foodId);

        if (existingItem) {
            existingItem.quantity += 1;
        } else {
            state.cart.push({
                foodId,
                quantity: 1,
                note: ""
            });
        }

        renderCart();
    }

    function updateQuantity(foodId, amount) {
        const item = state.cart.find((cartItem) => cartItem.foodId === foodId);

        if (!item) {
            return;
        }

        item.quantity += amount;

        if (item.quantity <= 0) {
            state.cart = state.cart.filter((cartItem) => cartItem.foodId !== foodId);
        }

        renderCart();
    }

    function updateItemNote(foodId, value) {
        const item = state.cart.find((cartItem) => cartItem.foodId === foodId);

        if (item) {
            item.note = value;
        }
    }

    function getSubtotal() {
        return state.cart.reduce((sum, item) => {
            const food = getFoodById(item.foodId);
            return sum + (food ? food.price * item.quantity : 0);
        }, 0);
    }

    function renderCart() {
        const totalQuantity = state.cart.reduce((sum, item) => sum + item.quantity, 0);

        cartItemCount.textContent = `${totalQuantity} món`;
        cartDockCount.textContent = `${totalQuantity} món`;

        if (state.cart.length === 0) {
            cartList.innerHTML = '<p class="cart-empty">Chưa có món nào trong đơn tạm.</p>';
        } else {
            cartList.innerHTML = state.cart.map((item) => {
                const food = getFoodById(item.foodId);

                if (!food) {
                    return "";
                }

                return `
                    <article class="cart-item">
                        <div class="cart-item-top">
                            <div class="cart-item-name">
                                <strong>${food.name}</strong>
                                <span>${formatCurrency(food.price)} / phần</span>
                            </div>
                            <strong>${formatCurrency(food.price * item.quantity)}</strong>
                        </div>

                        <div class="quantity-control">
                            <button class="quantity-button" type="button" data-action="decrease" data-food-id="${food.id}">-</button>
                            <strong>${item.quantity}</strong>
                            <button class="quantity-button" type="button" data-action="increase" data-food-id="${food.id}">+</button>
                        </div>

                        <input
                            class="cart-item-note"
                            type="text"
                            value="${escapeHtml(item.note)}"
                            placeholder="Ghi chú riêng cho món"
                            data-note-food-id="${food.id}"
                        >
                    </article>
                `;
            }).join("");
        }

        cartList.querySelectorAll("[data-action]").forEach((button) => {
            const amount = button.dataset.action === "increase" ? 1 : -1;
            button.addEventListener("click", () => updateQuantity(Number(button.dataset.foodId), amount));
        });

        cartList.querySelectorAll("[data-note-food-id]").forEach((input) => {
            input.addEventListener("input", () => updateItemNote(Number(input.dataset.noteFoodId), input.value));
        });

        renderTotals();
        sendKitchenButton.disabled = !state.selectedTable || state.cart.length === 0;
        clearOrderButton.disabled = state.cart.length === 0;
    }

    function renderTotals() {
        const subtotal = getSubtotal();
        const service = subtotal > 0 ? Math.round(subtotal * 0.05) : 0;
        const total = subtotal + service;

        subtotalAmount.textContent = formatCurrency(subtotal);
        serviceAmount.textContent = formatCurrency(service);
        totalAmount.textContent = formatCurrency(total);
        cartDockTotal.textContent = formatCurrency(total);
    }

    function setupNavigation() {
        navButtons.forEach((button) => {
            button.addEventListener("click", () => {
                window.location.href = button.dataset.page;
            });
        });
    }

    function renderSelectedTable() {
        if (!state.selectedTable) {
            orderTitle.textContent = "Chưa chọn bàn";
            orderContext.textContent = "Hãy chọn bàn trước khi đặt món.";
            sendKitchenButton.disabled = true;
            return;
        }

        const details = [
            state.selectedTable.areaLabel,
            state.selectedTable.guestCount ? `${state.selectedTable.guestCount} khách` : "",
            state.selectedTable.orderCode ? `Đơn ${state.selectedTable.orderCode}` : ""
        ].filter(Boolean).join(" · ");

        orderTitle.textContent = state.selectedTable.name || `Bàn ${state.selectedTable.id}`;
        orderContext.textContent = details || "Sẵn sàng nhận món cho bàn này.";
    }

    foodSearchInput.addEventListener("input", () => {
        state.searchTerm = foodSearchInput.value.trim();
        renderFoods();
    });

    chooseTableButton.addEventListener("click", () => {
        window.location.href = "tables.html";
    });

    clearOrderButton.addEventListener("click", () => {
        state.cart = [];
        renderCart();
    });

    cartDock.addEventListener("click", () => {
        cartPanel.classList.add("is-open");
    });

    closeCartButton.addEventListener("click", () => {
        cartPanel.classList.remove("is-open");
    });

    sendKitchenButton.addEventListener("click", () => {
        if (state.cart.length === 0) {
            return;
        }

        sendKitchenButton.textContent = "Đã gửi bếp";
        window.setTimeout(() => {
            sendKitchenButton.textContent = "Gửi bếp";
        }, 1400);
    });

    setupNavigation();
    renderSelectedTable();
    renderCategories();
    renderFoods();
    renderCart();
})();
