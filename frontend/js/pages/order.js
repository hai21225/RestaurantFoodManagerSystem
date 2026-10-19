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
        selectedTable: getSelectedTable(),
        sizeFoodId: null,
        weightFoodId: null,
        weightSizeId: null
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
    const sizeDialog = document.querySelector("#sizeDialog");
    const sizeDialogTitle = document.querySelector("#sizeDialogTitle");
    const sizeDialogDescription = document.querySelector("#sizeDialogDescription");
    const sizeOptions = document.querySelector("#sizeOptions");
    const sizeDialogMessage = document.querySelector("#sizeDialogMessage");
    const closeSizeDialog = document.querySelector("#closeSizeDialog");
    const cancelSizeButton = document.querySelector("#cancelSizeButton");
    const weightDialog = document.querySelector("#weightDialog");
    const weightForm = document.querySelector("#weightForm");
    const weightDialogTitle = document.querySelector("#weightDialogTitle");
    const weightDialogDescription = document.querySelector("#weightDialogDescription");
    const weightQuantity = document.querySelector("#weightQuantity");
    const weightPreviewTotal = document.querySelector("#weightPreviewTotal");
    const weightFormMessage = document.querySelector("#weightFormMessage");
    const closeWeightDialog = document.querySelector("#closeWeightDialog");
    const cancelWeightButton = document.querySelector("#cancelWeightButton");
    const navButtons = document.querySelectorAll("[data-page]");

    async function loadMenu() {
        foodGrid.innerHTML = '<div class="empty-state"><strong>Đang tải thực đơn...</strong></div>';
        try {
            const menu = await window.restaurantApi.get("/menu");
            orderData.categories = [{ id: "all", name: "Tất cả" }, ...menu.map((category) => ({
                id: String(category.categoryId),
                name: category.categoryName
            }))];
            orderData.foods = menu.flatMap((category) => category.foods.map((food) => {
                const supportedSizes = window.foodSizes.sort(
                    food.sizes.filter((size) => window.foodSizes.isSupported(size.sizeName))
                );
                const sizes = supportedSizes.map((size) => ({
                    id: size.sizeId,
                    name: window.foodSizes.normalize(size.sizeName) || size.sizeName,
                    price: Number(size.sizePrice) || 0,
                    isDefault: Boolean(size.isDefault)
                }));
                const defaultSize = sizes.find((size) => size.isDefault) || sizes[0];
                return {
                    id: food.foodId,
                    sizeId: defaultSize?.id || null,
                    sizes,
                    categoryId: String(category.categoryId),
                    name: food.foodName,
                    description: food.foodDescription || "Chưa có mô tả",
                    price: Number(defaultSize?.price ?? food.foodPrice) || 0,
                    unit: window.foodUnits.normalize(food.saleUnit),
                    allowsFractionalQuantity: Boolean(food.allowsFractionalQuantity)
                        || window.foodUnits.allowsFractionalQuantity(food.saleUnit),
                    prepTime: 0,
                    status: defaultSize ? "available" : "out"
                };
            }));
            renderCategories();
            renderFoods();
            renderCart();
        } catch (error) {
            orderData.foods = [];
            renderCategories();
            foodGrid.innerHTML = `<div class="empty-state"><strong>Không tải được thực đơn</strong><span>${error.message}</span></div>`;
        }
    }

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

    function roundQuantity(value) {
        return Math.round((Number(value) + Number.EPSILON) * 1000) / 1000;
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

    function getFoodSize(food, sizeId) {
        return food?.sizes.find((size) => size.id === sizeId) || null;
    }

    function getCartItem(foodId, sizeId) {
        return state.cart.find((item) => item.foodId === foodId && item.sizeId === sizeId);
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
            const minimumPrice = food.sizes.length
                ? Math.min(...food.sizes.map((size) => size.price))
                : 0;
            const priceLabel = food.sizes.length > 1
                ? `Từ ${formatCurrency(minimumPrice)}`
                : food.sizes.length === 1
                    ? `${formatCurrency(food.price)} / ${window.foodUnits.getShortLabel(food.unit)}`
                    : "Chưa có kích cỡ";

            return `
                <button
                    class="food-card${isDisabled ? " is-disabled" : ""}"
                    type="button"
                    data-food-id="${food.id}"
                    ${isDisabled ? "disabled" : ""}
                >
                    <div class="food-card-top">
                        <h4>${escapeHtml(food.name)}</h4>
                        <span class="food-price">${priceLabel}</span>
                    </div>
                    <p>${escapeHtml(food.description)}</p>
                    <div class="food-meta">
                        <span class="food-size-count">${food.sizes.length} kích cỡ</span>
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

        if (food.sizes.length > 1) {
            openSizeSelection(food);
            return;
        }

        const size = food.sizes[0];
        if (size) {
            addFoodSizeToCart(food, size);
        }
    }

    function openSizeSelection(food) {
        state.sizeFoodId = food.id;
        sizeDialogTitle.textContent = food.name;
        sizeDialogDescription.textContent = "Chọn kích cỡ và giá bán.";
        sizeDialogMessage.textContent = "";
        sizeOptions.innerHTML = food.sizes.map((size) => `
            <button class="size-option" type="button" data-select-size-id="${size.id}">
                <strong>${escapeHtml(size.name)}</strong>
                <span>${formatCurrency(size.price)}</span>
            </button>
        `).join("");
        sizeDialog.showModal();
        sizeOptions.querySelector("[data-select-size-id]")?.focus();
    }

    function closeSizeSelection() {
        state.sizeFoodId = null;
        if (sizeDialog.open) {
            sizeDialog.close();
        }
    }

    function addFoodSizeToCart(food, size) {
        if (food.allowsFractionalQuantity) {
            if (sizeDialog.open) {
                closeSizeSelection();
            }
            openWeightDialog(food.id, size.id);
            return;
        }

        const existingItem = getCartItem(food.id, size.id);

        if (existingItem) {
            existingItem.quantity += 1;
        } else {
            state.cart.push({
                foodId: food.id,
                sizeId: size.id,
                quantity: 1,
                note: ""
            });
        }

        if (sizeDialog.open) {
            closeSizeSelection();
        }
        renderCart();
    }

    function updateQuantity(foodId, sizeId, amount) {
        const item = getCartItem(foodId, sizeId);

        if (!item) {
            return;
        }

        item.quantity = roundQuantity(item.quantity + amount);

        if (item.quantity <= 0) {
            state.cart = state.cart.filter((cartItem) => (
                cartItem.foodId !== foodId || cartItem.sizeId !== sizeId
            ));
        }

        renderCart();
    }

    function updateWeightPreview() {
        const food = getFoodById(state.weightFoodId);
        const size = getFoodSize(food, state.weightSizeId);
        const quantity = roundQuantity(Number(String(weightQuantity.value).replace(",", ".")));
        const isValid = food && size && quantity >= 0.001;

        weightPreviewTotal.textContent = formatCurrency(isValid ? size.price * quantity : 0);
        weightFormMessage.textContent = isValid ? "" : "Khối lượng phải lớn hơn 0 kg.";
        weightFormMessage.className = isValid ? "form-message" : "form-message is-error";
    }

    function openWeightDialog(foodId, sizeId) {
        const food = getFoodById(foodId);
        const size = getFoodSize(food, sizeId);
        if (!food || !size) return;

        const existingItem = getCartItem(foodId, sizeId);
        state.weightFoodId = foodId;
        state.weightSizeId = sizeId;
        weightDialogTitle.textContent = food.name;
        weightDialogDescription.textContent = `${size.name} · ${formatCurrency(size.price)} / ${window.foodUnits.getShortLabel(food.unit)}`;
        weightQuantity.value = existingItem?.quantity || 1;
        updateWeightPreview();
        weightDialog.showModal();
        weightQuantity.focus();
        weightQuantity.select();
    }

    function closeWeightEntry() {
        state.weightFoodId = null;
        state.weightSizeId = null;
        if (weightDialog.open) {
            weightDialog.close();
        }
    }

    function updateItemNote(foodId, sizeId, value) {
        const item = getCartItem(foodId, sizeId);

        if (item) {
            item.note = value;
        }
    }

    function getSubtotal() {
        return state.cart.reduce((sum, item) => {
            const food = getFoodById(item.foodId);
            const size = getFoodSize(food, item.sizeId);
            return sum + (size ? size.price * item.quantity : 0);
        }, 0);
    }

    function renderCart() {
        const totalQuantity = state.cart.length;

        cartItemCount.textContent = `${totalQuantity} món`;
        cartDockCount.textContent = `${totalQuantity} món`;

        if (state.cart.length === 0) {
            cartList.innerHTML = '<p class="cart-empty">Chưa có món nào trong đơn tạm.</p>';
        } else {
            cartList.innerHTML = state.cart.map((item) => {
                const food = getFoodById(item.foodId);
                const size = getFoodSize(food, item.sizeId);

                if (!food || !size) {
                    return "";
                }

                return `
                    <article class="cart-item">
                        <div class="cart-item-top">
                            <div class="cart-item-name">
                                <strong>${escapeHtml(food.name)}</strong>
                                <span class="cart-item-size">${escapeHtml(size.name)} · ${formatCurrency(size.price)} / ${window.foodUnits.getShortLabel(food.unit)}</span>
                            </div>
                            <strong>${formatCurrency(size.price * item.quantity)}</strong>
                        </div>

                        <div class="quantity-control">
                            <button class="quantity-button" type="button" data-quantity-change="-${food.allowsFractionalQuantity ? "0.1" : "1"}" data-food-id="${food.id}" data-size-id="${size.id}" aria-label="Giảm số lượng ${escapeHtml(food.name)} cỡ ${escapeHtml(size.name)}">-</button>
                            ${food.allowsFractionalQuantity
                                ? `<button class="quantity-value" type="button" data-edit-weight="${food.id}" data-size-id="${size.id}" aria-label="Sửa khối lượng ${escapeHtml(food.name)} cỡ ${escapeHtml(size.name)}">${window.foodUnits.formatQuantity(item.quantity, food.unit)}</button>`
                                : `<strong>${window.foodUnits.formatQuantity(item.quantity, food.unit)}</strong>`}
                            <button class="quantity-button" type="button" data-quantity-change="${food.allowsFractionalQuantity ? "0.1" : "1"}" data-food-id="${food.id}" data-size-id="${size.id}" aria-label="Tăng số lượng ${escapeHtml(food.name)} cỡ ${escapeHtml(size.name)}">+</button>
                        </div>

                        <input
                            class="cart-item-note"
                            type="text"
                            value="${escapeHtml(item.note)}"
                            placeholder="Ghi chú riêng cho món"
                            data-note-food-id="${food.id}"
                            data-note-size-id="${size.id}"
                        >
                    </article>
                `;
            }).join("");
        }

        cartList.querySelectorAll("[data-quantity-change]").forEach((button) => {
            button.addEventListener("click", () => updateQuantity(
                Number(button.dataset.foodId),
                Number(button.dataset.sizeId),
                Number(button.dataset.quantityChange)
            ));
        });

        cartList.querySelectorAll("[data-edit-weight]").forEach((button) => {
            button.addEventListener("click", () => openWeightDialog(
                Number(button.dataset.editWeight),
                Number(button.dataset.sizeId)
            ));
        });

        cartList.querySelectorAll("[data-note-food-id]").forEach((input) => {
            input.addEventListener("input", () => updateItemNote(
                Number(input.dataset.noteFoodId),
                Number(input.dataset.noteSizeId),
                input.value
            ));
        });

        renderTotals();
        sendKitchenButton.disabled = !state.selectedTable || state.cart.length === 0;
        clearOrderButton.disabled = state.cart.length === 0;
    }

    function renderTotals() {
        const subtotal = getSubtotal();
        const service = 0;
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

    sizeOptions.addEventListener("click", (event) => {
        const button = event.target.closest("[data-select-size-id]");
        if (!button) return;

        const food = getFoodById(state.sizeFoodId);
        const size = getFoodSize(food, Number(button.dataset.selectSizeId));
        if (food && size) {
            addFoodSizeToCart(food, size);
        }
    });

    closeSizeDialog.addEventListener("click", closeSizeSelection);
    cancelSizeButton.addEventListener("click", closeSizeSelection);
    sizeDialog.addEventListener("close", () => {
        state.sizeFoodId = null;
    });

    weightQuantity.addEventListener("input", updateWeightPreview);

    document.querySelectorAll("[data-weight-value]").forEach((button) => {
        button.addEventListener("click", () => {
            weightQuantity.value = button.dataset.weightValue;
            updateWeightPreview();
            weightQuantity.focus();
        });
    });

    closeWeightDialog.addEventListener("click", closeWeightEntry);
    cancelWeightButton.addEventListener("click", closeWeightEntry);
    weightDialog.addEventListener("close", () => {
        state.weightFoodId = null;
        state.weightSizeId = null;
    });

    weightForm.addEventListener("submit", (event) => {
        event.preventDefault();
        const food = getFoodById(state.weightFoodId);
        const size = getFoodSize(food, state.weightSizeId);
        const quantity = roundQuantity(Number(String(weightQuantity.value).replace(",", ".")));

        if (!food || !size || quantity < 0.001 || quantity > 999999999) {
            weightFormMessage.textContent = "Nhập khối lượng hợp lệ lớn hơn 0 kg.";
            weightFormMessage.className = "form-message is-error";
            weightQuantity.focus();
            return;
        }

        const existingItem = getCartItem(food.id, size.id);
        if (existingItem) {
            existingItem.quantity = quantity;
        } else {
            state.cart.push({ foodId: food.id, sizeId: size.id, quantity, note: "" });
        }

        closeWeightEntry();
        renderCart();
    });

    sendKitchenButton.addEventListener("click", async () => {
        if (state.cart.length === 0) {
            return;
        }

        const user = JSON.parse(window.sessionStorage.getItem("restaurant.currentUser") || "null");
        if (!user?.userId) {
            window.alert("Bạn cần đăng nhập trước khi gửi đơn.");
            return;
        }

        sendKitchenButton.disabled = true;
        sendKitchenButton.textContent = "Đang gửi...";
        try {
            const order = await window.restaurantApi.post("/orders", {
                tableId: Number(state.selectedTable.id),
                createdBy: user.userId,
                items: state.cart.map((item) => ({
                    sizeId: item.sizeId,
                    quantity: item.quantity,
                    note: item.note,
                    toppings: []
                }))
            });
            window.localStorage.setItem("restaurant.lastOrder", JSON.stringify(order));
            state.cart = [];
            renderCart();
            sendKitchenButton.textContent = "Đã gửi bếp";
        } catch (error) {
            window.alert(error.message);
        } finally {
            window.setTimeout(() => {
                sendKitchenButton.disabled = state.cart.length === 0;
                sendKitchenButton.textContent = "Gửi bếp";
            }, 900);
        }
    });

    setupNavigation();
    renderSelectedTable();
    renderCategories();
    renderFoods();
    renderCart();
    loadMenu();
})();
