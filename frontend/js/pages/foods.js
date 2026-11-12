(function () {
    const foodsData = window.foodsData || {
        categories: [],
        statuses: [],
        foods: []
    };

    const state = {
        category: "all",
        status: "active",
        searchTerm: "",
        selectedFoodId: foodsData.foods[0]?.id || null,
        editingFoodId: null,
        categories: [],
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
    const addCategoryButton = document.querySelector("#addCategoryButton");
    const addSizeButton = document.querySelector("#addSizeButton");
    const addFoodButton = document.querySelector("#addFoodButton");
    const categoryDialog = document.querySelector("#categoryDialog");
    const foodDialog = document.querySelector("#foodDialog");
    const sizeDialog = document.querySelector("#sizeDialog");
    const foodLifecycleDialog = document.querySelector("#foodLifecycleDialog");
    const categoryForm = document.querySelector("#categoryForm");
    const foodForm = document.querySelector("#foodForm");
    const sizeForm = document.querySelector("#sizeForm");
    const foodCategorySelect = document.querySelector("#foodCategorySelect");
    const foodDialogTitle = document.querySelector("#foodDialogTitle");
    const foodDialogDescription = document.querySelector("#foodDialogDescription");
    const foodSubmit = document.querySelector("#foodSubmit");
    const sizeFoodSelect = document.querySelector("#sizeFoodSelect");
    const sizePriceLabel = document.querySelector("#sizePriceLabel");
    const foodLifecycleTitle = document.querySelector("#foodLifecycleTitle");
    const foodLifecycleDescription = document.querySelector("#foodLifecycleDescription");
    const foodLifecycleNotice = document.querySelector("#foodLifecycleNotice");
    const foodLifecycleMessage = document.querySelector("#foodLifecycleMessage");
    const foodLifecycleConfirm = document.querySelector("#foodLifecycleConfirm");
    const navButtons = document.querySelectorAll("[data-page]");

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    async function loadFoods() {
        foodList.innerHTML = '<p class="empty-state">Đang tải thực đơn...</p>';
        try {
            const [categories, foods] = await Promise.all([
                window.restaurantApi.get("/categories?includeInactive=true"),
                window.restaurantApi.get("/foods?includeInactive=true")
            ]);
            const [sizesByFood, toppingsByFood] = await Promise.all([
                Promise.all(foods.map((food) => window.restaurantApi.get(`/foods/${food.foodId}/sizes`))),
                Promise.all(foods.map((food) => window.restaurantApi.get(`/foods/${food.foodId}/toppings`)))
            ]);
            const categoryNames = new Map(categories.map((category) => [category.id, category.name]));
            state.categories = categories.filter((category) => category.isActive);
            foodsData.categories = [{ value: "all", label: "Tất cả" }, ...categories.map((category) => ({
                value: String(category.id),
                label: category.name
            }))];
            state.foods = foods.map((food, index) => ({
                id: food.foodId,
                code: `FD-${String(food.foodId).padStart(3, "0")}`,
                name: food.foodName,
                description: food.foodDescription || "Chưa có mô tả",
                imageUrl: food.foodImageUrl || "",
                category: String(food.categoryId),
                categoryLabel: categoryNames.get(food.categoryId) || `Danh mục ${food.categoryId}`,
                price: Number(food.foodPrice) || 0,
                cost: null,
                prepTime: null,
                unit: window.foodUnits.normalize(food.saleUnit),
                status: !food.isActive ? "stopped" : food.isAvailable ? "available" : "out",
                statusLabel: !food.isActive ? "Ngừng bán" : food.isAvailable ? "Đang bán" : "Tạm hết",
                sizes: window.foodSizes.sort(sizesByFood[index])
                    .map((size) => ({ name: window.foodSizes.normalize(size.sizeName) || size.sizeName, price: Number(size.sizePrice) || 0 })),
                toppings: toppingsByFood[index].map((t) => ({ name: t.toppingName, price: t.toppingPrice, isAvailable: t.isAvailable }))
            }));
            const visible = getFilteredFoods();
            if (!visible.some((food) => food.id === state.selectedFoodId)) state.selectedFoodId = visible[0]?.id || null;
            updateFormOptions();
            render();
        } catch (error) {
            state.categories = [];
            state.foods = [];
            updateFormOptions();
            render();
            foodList.innerHTML = `<p class="empty-state">${escapeHtml(error.message)}</p>`;
        }
    }

    function formatCurrency(value) {
        return `${value.toLocaleString("vi-VN")}đ`;
    }

    function updateFormOptions() {
        foodCategorySelect.innerHTML = state.categories.length
            ? `<option value="">Chọn danh mục</option>${state.categories.map((category) => `
                <option value="${category.id}">${escapeHtml(category.name)}</option>
            `).join("")}`
            : '<option value="">Chưa có danh mục</option>';

        const usableFoods = state.foods.filter((food) => food.status !== "stopped");
        sizeFoodSelect.innerHTML = usableFoods.length
            ? `<option value="">Chọn món ăn</option>${usableFoods.map((food) => `
                <option value="${food.id}" data-sale-unit="${escapeHtml(food.unit)}">${escapeHtml(food.name)}</option>
            `).join("")}`
            : '<option value="">Chưa có món ăn</option>';

        updateSizePriceLabel();
    }

    function updateSizePriceLabel() {
        const selectedOption = sizeFoodSelect.selectedOptions[0];
        const unit = selectedOption?.dataset.saleUnit || "portion";
        sizePriceLabel.textContent = `Giá bán / ${window.foodUnits.getShortLabel(unit)}`;
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
            const matchStatus = state.status === "active" ? food.status !== "stopped" : state.status === "all" || food.status === state.status;
            const matchSearch = food.name.toLowerCase().includes(searchTerm)
                || food.code.toLowerCase().includes(searchTerm);

            return matchCategory && matchStatus && matchSearch;
        });
    }

    function renderSummary() {
        const availableCount = state.foods.filter((food) => food.status === "available").length;
        const outCount = state.foods.filter((food) => food.status === "out").length;
        const stoppedCount = state.foods.filter((food) => food.status === "stopped").length;
        const summary = [
            { label: "Đang sử dụng", value: state.foods.length - stoppedCount },
            { label: "Đang bán", value: availableCount },
            { label: "Tạm hết", value: outCount },
            { label: "Ngừng bán", value: stoppedCount }
        ];

        foodsSummary.innerHTML = summary.map((item) => `<span><strong>${item.value}</strong> ${item.label.toLocaleLowerCase("vi")}</span>`).join("");
    }

    function renderFilterOptions(container, items, activeValue) {
        container.innerHTML = items.map((item) => `
            <option value="${escapeHtml(item.value)}">${escapeHtml(item.label)}</option>
        `).join("");
        container.value = activeValue;
    }

    function renderFilters() {
        renderFilterOptions(categoryFilters, foodsData.categories, state.category);
        renderFilterOptions(statusFilters, foodsData.statuses, state.status);
    }

    function getFoodInitials(name) {
        return name
            .split(" ")
            .slice(0, 2)
            .map((word) => word.charAt(0))
            .join("")
            .toUpperCase();
    }

    function renderFoodList() {
        const foods = getFilteredFoods();
        if (!foods.some((food) => food.id === state.selectedFoodId)) state.selectedFoodId = foods[0]?.id || null;

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
                <span class="food-thumb" aria-hidden="true">${escapeHtml(getFoodInitials(food.name))}</span>
                <span class="food-row-main">
                    <strong>${escapeHtml(food.name)}</strong>
                    <span class="food-code">${food.code}</span>
                    ${food.description !== "Chưa có mô tả" ? `<span class="food-row-description">${escapeHtml(food.description)}</span>` : ""}
                </span>
                <span class="food-category">${escapeHtml(food.categoryLabel)}</span>
                <span class="food-row-footer">
                    <span class="food-price">${formatCurrency(food.price)} / ${window.foodUnits.getShortLabel(food.unit)}</span>
                    <span class="status status-${food.status === "available" ? "empty" : food.status === "out" ? "waiting" : "payment"}">${food.statusLabel}</span>
                </span>
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
                <strong>${escapeHtml(food.name)}</strong>
                <span>${food.code} - ${escapeHtml(food.categoryLabel)}</span>
            </div>

            <div class="detail-grid">
                <div class="detail-item">
                    <span>Giá bán</span>
                    <strong>${formatCurrency(food.price)} / ${window.foodUnits.getShortLabel(food.unit)}</strong>
                </div>
                <div class="detail-item">
                    <span>Giá vốn</span>
                    <strong>Chưa có dữ liệu</strong>
                </div>
                <div class="detail-item">
                    <span>Thời gian chế biến</span>
                    <strong>Chưa có dữ liệu</strong>
                </div>
                <div class="detail-item">
                    <span>Đơn vị tính</span>
                    <strong>${window.foodUnits.getLabel(food.unit)}</strong>
                </div>
            </div>

            <p>${escapeHtml(food.description)}</p>

            <p class="info-list-title">Size / giá</p>
            <div class="size-list">
                ${food.sizes.map((size) => `
                    <div class="size-item">
                        <span>${escapeHtml(size.name)}</span>
                        <strong>${formatCurrency(size.price)}</strong>
                    </div>
                `).join("")}
            </div>

            <p class="info-list-title">Topping đang hoạt động</p>
            <div class="topping-list">
                ${food.toppings.map((topping) => `
                    <div class="topping-item">
                        <span>${escapeHtml(topping.name)} · ${topping.isAvailable ? "Đang bán" : "Tạm hết"}</span>
                        <strong>${formatCurrency(topping.price)}</strong>
                    </div>
                `).join("") || '<p class="field-hint">Chưa có topping đang hoạt động được gán cho món.</p>'}
            </div>

            <div class="detail-actions"${window.restaurantAuth.hasRole("Admin", "WarehouseManager") ? "" : " hidden"}>
                <button class="secondary-action" type="button" data-action="manage-configuration">Quản lý kích cỡ / Topping</button>
                <button class="secondary-action" type="button" data-action="edit-food">
                    Sửa thông tin
                </button>
                ${food.status !== "stopped" ? `
                    <button class="primary-action" type="button" data-action="toggle-status">
                        ${food.status === "available" ? "Chuyển tạm hết" : "Bật đang bán"}
                    </button>
                ` : ""}
                <button class="${food.status === "stopped" ? "secondary-action" : "danger-action"}" type="button" data-action="toggle-active">
                    ${food.status === "stopped" ? "Khôi phục món" : "Xóa khỏi thực đơn"}
                </button>
            </div>
        `;

        foodDetail.querySelectorAll("[data-action]").forEach((button) => {
            button.addEventListener("click", () => handleDetailAction(button.dataset.action));
        });
    }

    async function handleDetailAction(action) {
        const food = getSelectedFood();

        if (!food) {
            return;
        }
        if (!window.restaurantAuth.hasRole("Admin", "WarehouseManager")) return;
        if (action === "manage-configuration") { window.location.href = `administration.html?tab=links&foodId=${food.id}`; return; }

        if (action === "edit-food") {
            openFoodEditor(food);
            return;
        }

        if (action === "toggle-active") {
            openFoodLifecycleDialog(food);
            return;
        }

        if (action === "toggle-status") {
            const available = food.status !== "available";
            try {
                await window.restaurantApi.patch(`/foods/${food.id}/availability`, { isAvailable: available });
                await loadFoods();
            } catch (error) {
                window.alert(error.message);
            }
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

    function openFormDialog(dialog, form, focusName) {
        form.reset();
        const message = form.querySelector(".form-message");
        message.textContent = "";
        message.className = "form-message";
        dialog.showModal();
        form.elements[focusName].focus();
    }

    function prepareCreateFoodDialog(messageText = "") {
        state.editingFoodId = null;
        foodDialogTitle.textContent = "Thêm món ăn";
        foodDialogDescription.textContent = "Nhập thông tin cơ bản; kích cỡ có thể thêm sau.";
        foodSubmit.textContent = "Tạo món";
        openFormDialog(foodDialog, foodForm, "foodName");
        foodForm.querySelector(".form-message").textContent = messageText;
    }

    function openFoodEditor(food) {
        state.editingFoodId = food.id;
        foodForm.reset();
        foodDialogTitle.textContent = "Sửa món ăn";
        foodDialogDescription.textContent = "Cập nhật giá và thông tin hiển thị của món.";
        foodSubmit.textContent = "Lưu thay đổi";
        foodForm.elements.foodName.value = food.name;
        foodForm.elements.categoryId.value = food.category;
        foodForm.elements.foodPrice.value = food.price;
        foodForm.elements.foodImageUrl.value = food.imageUrl;
        foodForm.elements.foodDescription.value = food.description === "Chưa có mô tả" ? "" : food.description;
        foodForm.elements.isAvailable.checked = food.status === "available";
        foodForm.querySelector(".form-message").textContent = "";
        foodForm.querySelector(".form-message").className = "form-message";
        foodDialog.showModal();
        foodForm.elements.foodName.focus();
    }

    function openFoodLifecycleDialog(food) {
        const restoring = food.status === "stopped";
        foodLifecycleTitle.textContent = restoring ? "Khôi phục món" : "Xóa món khỏi thực đơn";
        foodLifecycleDescription.textContent = food.name;
        foodLifecycleNotice.textContent = restoring
            ? "Món sẽ được khôi phục ở trạng thái Tạm hết. Admin cần bật Đang bán trước khi nhân viên có thể đặt món."
            : "Món sẽ biến mất khỏi danh sách đang dùng và màn Đặt món. Chọn bộ lọc Đã xóa / Ngừng bán để xem hoặc khôi phục; lịch sử vẫn được giữ.";
        foodLifecycleMessage.textContent = "";
        foodLifecycleMessage.className = "form-message";
        foodLifecycleConfirm.textContent = restoring ? "Khôi phục món" : "Xóa khỏi thực đơn";
        foodLifecycleConfirm.className = restoring ? "primary-action" : "danger-action";
        foodLifecycleDialog.showModal();
        foodLifecycleConfirm.focus();
    }

    function setSubmitting(button, submitting, idleLabel) {
        button.disabled = submitting;
        button.textContent = submitting ? "Đang lưu..." : idleLabel;
    }

    function showFormError(form, error) {
        const message = form.querySelector(".form-message");
        message.textContent = error.message;
        message.className = "form-message is-error";
    }

    function render() {
        const editable = window.restaurantAuth.hasRole("Admin", "WarehouseManager");
        for (const id of ["addCategoryButton", "addSizeButton", "addFoodButton", "manageCatalogButton"]) document.getElementById(id).hidden = !editable;
        renderSummary();
        renderFilters();
        renderFoodList();
        renderFoodDetail();
    }

    categoryFilters.addEventListener("change", () => {
        state.category = categoryFilters.value;
        render();
    });

    statusFilters.addEventListener("change", () => {
        state.status = statusFilters.value;
        render();
    });

    foodSearchInput.addEventListener("input", () => {
        state.searchTerm = foodSearchInput.value.trim();
        renderFoodList();
    });

    addCategoryButton.addEventListener("click", () => {
        openFormDialog(categoryDialog, categoryForm, "name");
    });

    addFoodButton.addEventListener("click", () => {
        if (!state.categories.length) {
            openFormDialog(categoryDialog, categoryForm, "name");
            const message = categoryForm.querySelector(".form-message");
            message.textContent = "Hãy tạo danh mục trước khi thêm món.";
            return;
        }
        prepareCreateFoodDialog();
    });

    addSizeButton.addEventListener("click", () => {
        if (!state.foods.length) {
            if (!state.categories.length) {
                openFormDialog(categoryDialog, categoryForm, "name");
                categoryForm.querySelector(".form-message").textContent = "Hãy tạo danh mục và món ăn trước.";
                return;
            }
            prepareCreateFoodDialog("Hãy tạo món ăn trước khi thêm kích cỡ.");
            return;
        }
        openFormDialog(sizeDialog, sizeForm, "foodId");
        updateSizePriceLabel();
    });

    document.querySelectorAll("[data-close-food-dialog]").forEach((button) => {
        button.addEventListener("click", () => {
            document.getElementById(button.dataset.closeFoodDialog).close();
        });
    });

    categoryForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (!categoryForm.checkValidity()) {
            categoryForm.reportValidity();
            return;
        }

        const data = new FormData(categoryForm);
        const submit = document.querySelector("#categorySubmit");
        setSubmitting(submit, true, "Tạo danh mục");
        try {
            await window.restaurantApi.post("/categories", {
                name: data.get("name").trim(),
                description: data.get("description").trim(),
                // API hiện tại dùng kiểu boolean cho displayOrder.
                displayOrder: false,
                isActive: true
            });
            categoryDialog.close();
            await loadFoods();
        } catch (error) {
            showFormError(categoryForm, error);
        } finally {
            setSubmitting(submit, false, "Tạo danh mục");
        }
    });

    foodForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (!foodForm.checkValidity()) {
            foodForm.reportValidity();
            return;
        }

        const data = new FormData(foodForm);
        const submit = foodSubmit;
        setSubmitting(submit, true, "Tạo món");
        try {
            const payload = {
                categoryId: Number(data.get("categoryId")),
                foodName: data.get("foodName").trim(),
                foodDescription: data.get("foodDescription").trim(),
                foodImageUrl: data.get("foodImageUrl").trim(),
                foodPrice: Number(data.get("foodPrice")),
                saleUnit: "portion",
                isAvailable: data.get("isAvailable") === "on"
            };

            if (state.editingFoodId) {
                await window.restaurantApi.put(`/foods/${state.editingFoodId}`, payload);
            } else {
                await window.restaurantApi.post("/foods", payload);
            }
            foodDialog.close();
            await loadFoods();
        } catch (error) {
            showFormError(foodForm, error);
        } finally {
            setSubmitting(submit, false, state.editingFoodId ? "Lưu thay đổi" : "Tạo món");
        }
    });

    sizeForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (!sizeForm.checkValidity()) {
            sizeForm.reportValidity();
            return;
        }

        const data = new FormData(sizeForm);
        const submit = document.querySelector("#sizeSubmit");
        setSubmitting(submit, true, "Tạo kích cỡ");
        try {
            await window.restaurantApi.post(`/foods/${Number(data.get("foodId"))}/sizes`, {
                sizeName: data.get("sizeName").trim(),
                sizePrice: Number(data.get("sizePrice")),
                isDefault: data.get("isDefault") === "on"
            });
            sizeDialog.close();
            await loadFoods();
        } catch (error) {
            showFormError(sizeForm, error);
        } finally {
            setSubmitting(submit, false, "Tạo kích cỡ");
        }
    });

    foodLifecycleConfirm.addEventListener("click", async () => {
        const food = getSelectedFood();
        if (!food) return;

        const restoring = food.status === "stopped";
        setSubmitting(foodLifecycleConfirm, true, restoring ? "Khôi phục món" : "Xóa khỏi thực đơn");
        try {
            if (restoring) {
                await window.restaurantApi.patch(`/foods/${food.id}/active`, { isActive: true });
            } else {
                await window.restaurantApi.delete(`/foods/${food.id}`);
            }
            state.status = "active";
            foodLifecycleDialog.close();
            closeFoodDetail();
            await loadFoods();
            document.getElementById("foodListNotice").textContent = restoring ? "Đã khôi phục món. Kiểm tra trạng thái bán trước khi đặt món." : "Đã xóa món khỏi danh sách đang dùng. Xem lại tại bộ lọc Đã xóa / Ngừng bán.";
        } catch (error) {
            foodLifecycleMessage.textContent = error.message;
            foodLifecycleMessage.className = "form-message is-error";
        } finally {
            setSubmitting(foodLifecycleConfirm, false, restoring ? "Khôi phục món" : "Xóa khỏi thực đơn");
        }
    });

    closeFoodDetailButton.addEventListener("click", closeFoodDetail);
    sizeFoodSelect.addEventListener("change", updateSizePriceLabel);

    setupNavigation();
    window.addEventListener("restaurant:auth-changed", render);
    render();
    loadFoods();
})();
