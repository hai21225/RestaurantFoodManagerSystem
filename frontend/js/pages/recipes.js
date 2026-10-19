(function () {
    const state = {
        foods: [],
        sizes: [],
        recipes: [],
        ingredients: [],
        foodId: "all",
        status: "all",
        search: "",
        selectedSizeId: null,
        selectedDetails: null,
        editingRecipeId: null
    };

    const recipeSearch = document.querySelector("#recipeSearch");
    const foodFilter = document.querySelector("#foodFilter");
    const statusFilter = document.querySelector("#recipeStatusFilter");
    const recipeList = document.querySelector("#recipeList");
    const recipeListCount = document.querySelector("#recipeListCount");
    const recipeDetail = document.querySelector("#recipeDetail");
    const recipeDetailPanel = document.querySelector(".recipe-detail-panel");
    const totalSizeCount = document.querySelector("#totalSizeCount");
    const configuredCount = document.querySelector("#configuredCount");
    const missingCount = document.querySelector("#missingCount");
    const recipeDialog = document.querySelector("#recipeDialog");
    const recipeForm = document.querySelector("#recipeForm");
    const recipeDialogTitle = document.querySelector("#recipeDialogTitle");
    const recipeDialogContext = document.querySelector("#recipeDialogContext");
    const recipeIngredientRows = document.querySelector("#recipeIngredientRows");
    const recipeFormMessage = document.querySelector("#recipeFormMessage");
    const recipeSubmit = document.querySelector("#recipeSubmit");
    const ingredientDialog = document.querySelector("#ingredientDialog");
    const ingredientForm = document.querySelector("#ingredientForm");
    const ingredientFormMessage = document.querySelector("#ingredientFormMessage");
    const ingredientSubmit = document.querySelector("#ingredientSubmit");
    const availableIngredientCount = document.querySelector("#availableIngredientCount");

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function getRecipeForSize(sizeId) {
        return state.recipes.find((recipe) => recipe.sizeId === sizeId && recipe.isActive);
    }

    function getSize(sizeId) {
        return state.sizes.find((size) => size.sizeId === sizeId);
    }

    function getFilteredSizes() {
        const term = state.search.toLocaleLowerCase("vi");
        return state.sizes.filter((size) => {
            const recipe = getRecipeForSize(size.sizeId);
            const matchesFood = state.foodId === "all" || String(size.foodId) === state.foodId;
            const matchesSearch = size.foodName.toLocaleLowerCase("vi").includes(term);
            const matchesStatus = state.status === "all"
                || (state.status === "configured" && recipe)
                || (state.status === "missing" && !recipe);
            return matchesFood && matchesSearch && matchesStatus;
        });
    }

    async function loadData(preferredSizeId = state.selectedSizeId) {
        recipeList.innerHTML = '<div class="recipe-empty"><strong>Đang tải công thức...</strong></div>';

        try {
            const [foods, ingredients, recipes] = await Promise.all([
                window.restaurantApi.get("/foods?includeInactive=true"),
                window.restaurantApi.get("/management/ingredients"),
                window.restaurantApi.get("/management/recipes")
            ]);
            const activeFoods = foods.filter((food) => food.isActive);
            const sizesByFood = await Promise.all(activeFoods.map((food) =>
                window.restaurantApi.get(`/foods/${food.foodId}/sizes`)
            ));

            state.foods = activeFoods.map((food, index) => ({
                foodId: food.foodId,
                foodName: food.foodName,
                sizes: sizesByFood[index]
            }));
            state.sizes = state.foods.flatMap((food) => window.foodSizes.sort(
                food.sizes.filter((size) => window.foodSizes.isSupported(size.sizeName))
            ).map((size) => ({
                ...size,
                sizeName: window.foodSizes.normalize(size.sizeName),
                foodId: food.foodId,
                foodName: food.foodName
            })));
            state.ingredients = ingredients.filter((ingredient) => ingredient.isActive);
            state.recipes = recipes;
            state.selectedSizeId = state.sizes.some((size) => size.sizeId === preferredSizeId)
                ? preferredSizeId
                : null;
            state.selectedDetails = null;

            renderFoodFilter();
            renderSummary();
            renderList();
            renderDetail();
        } catch (error) {
            state.foods = [];
            state.sizes = [];
            state.recipes = [];
            recipeList.innerHTML = `<div class="recipe-empty"><strong>Không tải được công thức</strong><span>${escapeHtml(error.message)}</span></div>`;
        }
    }

    function renderFoodFilter() {
        const selectedValue = state.foodId;
        foodFilter.innerHTML = '<option value="all">Tất cả món</option>' + state.foods
            .filter((food) => food.sizes.some((size) => window.foodSizes.isSupported(size.sizeName)))
            .map((food) => `<option value="${food.foodId}">${escapeHtml(food.foodName)}</option>`)
            .join("");
        foodFilter.value = selectedValue;
        availableIngredientCount.textContent = `${state.ingredients.length} nguyên liệu khả dụng`;
    }

    function renderSummary() {
        const configured = state.sizes.filter((size) => getRecipeForSize(size.sizeId)).length;
        totalSizeCount.textContent = String(state.sizes.length);
        configuredCount.textContent = String(configured);
        missingCount.textContent = String(state.sizes.length - configured);
    }

    function renderList() {
        const sizes = getFilteredSizes();
        recipeListCount.textContent = `${sizes.length} kích cỡ`;

        if (!sizes.length) {
            recipeList.innerHTML = '<div class="recipe-empty"><strong>Không có kích cỡ phù hợp</strong><span>Hãy kiểm tra bộ lọc hoặc thêm size trong trang Món ăn.</span></div>';
            return;
        }

        recipeList.innerHTML = sizes.map((size) => {
            const recipe = getRecipeForSize(size.sizeId);
            return `
                <article class="recipe-row${state.selectedSizeId === size.sizeId ? " is-selected" : ""}" data-size-row="${size.sizeId}">
                    <div class="recipe-row-main">
                        <strong>${escapeHtml(size.foodName)}</strong>
                        <span class="recipe-row-meta">${escapeHtml(recipe?.name || "Chưa có công thức")} ${recipe ? `- Phiên bản ${escapeHtml(recipe.version)}` : ""}</span>
                    </div>
                    <span class="recipe-size">${escapeHtml(size.sizeName)}</span>
                    <span class="recipe-state ${recipe ? "is-ready" : "is-missing"}">${recipe ? "Đã thiết lập" : "Chưa có"}</span>
                    <div class="recipe-row-actions">
                        ${recipe ? `<button class="secondary-action" type="button" data-view-recipe="${recipe.recipeId}" data-size-id="${size.sizeId}">Xem</button>` : ""}
                        <button class="${recipe ? "secondary-action" : "primary-action"}" type="button" data-edit-size="${size.sizeId}" ${recipe ? `data-recipe-id="${recipe.recipeId}"` : ""}>${recipe ? "Sửa" : "Thiết lập"}</button>
                    </div>
                </article>
            `;
        }).join("");

        recipeList.querySelectorAll("[data-view-recipe]").forEach((button) => {
            button.addEventListener("click", () => selectRecipe(Number(button.dataset.sizeId), Number(button.dataset.viewRecipe)));
        });
        recipeList.querySelectorAll("[data-edit-size]").forEach((button) => {
            button.addEventListener("click", () => openRecipeDialog(
                Number(button.dataset.editSize),
                button.dataset.recipeId ? Number(button.dataset.recipeId) : null
            ));
        });
    }

    async function selectRecipe(sizeId, recipeId) {
        state.selectedSizeId = sizeId;
        state.selectedDetails = null;
        renderList();
        recipeDetail.innerHTML = '<div class="recipe-empty"><strong>Đang tải chi tiết...</strong></div>';
        recipeDetailPanel.classList.add("is-open");

        try {
            state.selectedDetails = await window.restaurantApi.get(`/management/recipes/${recipeId}`);
            renderDetail();
        } catch (error) {
            recipeDetail.innerHTML = `<div class="recipe-empty"><strong>Không tải được chi tiết</strong><span>${escapeHtml(error.message)}</span></div>`;
        }
    }

    function renderDetail() {
        const size = getSize(state.selectedSizeId);
        const recipe = size ? getRecipeForSize(size.sizeId) : null;

        if (!size) {
            recipeDetail.innerHTML = '<div class="recipe-empty"><strong>Chưa chọn công thức</strong><span>Chọn một kích cỡ trong danh sách.</span></div>';
            return;
        }

        if (!recipe) {
            recipeDetail.innerHTML = `
                <div class="recipe-empty">
                    <strong>${escapeHtml(size.foodName)} - ${escapeHtml(size.sizeName)}</strong>
                    <span>Chưa có công thức đang sử dụng.</span>
                    <button class="primary-action" type="button" data-create-selected>Thiết lập công thức</button>
                </div>`;
            recipeDetail.querySelector("[data-create-selected]").addEventListener("click", () => openRecipeDialog(size.sizeId));
            return;
        }

        if (!state.selectedDetails || state.selectedDetails.recipeId !== recipe.recipeId) {
            recipeDetail.innerHTML = `
                <div class="recipe-detail-title"><strong>${escapeHtml(size.foodName)}</strong><span>${escapeHtml(size.sizeName)} - ${escapeHtml(recipe.name)}</span></div>
                <div class="recipe-empty"><strong>${recipe.ingredientCount} nguyên liệu</strong><span>Chọn Xem để tải định lượng chi tiết.</span></div>`;
            return;
        }

        const details = state.selectedDetails;
        recipeDetail.innerHTML = `
            <div class="recipe-detail-title"><strong>${escapeHtml(details.foodName)}</strong><span>${escapeHtml(details.sizeName)} - ${escapeHtml(details.name)} - Phiên bản ${escapeHtml(details.version)}</span></div>
            <div class="recipe-detail-list">
                ${details.ingredients.map((ingredient) => `
                    <div class="recipe-detail-item"><span>${escapeHtml(ingredient.ingredientName)}</span><strong>${Number(ingredient.quantity).toLocaleString("vi-VN", { maximumFractionDigits: 3 })} ${escapeHtml(ingredient.unit)}</strong></div>
                `).join("")}
            </div>
            <div class="recipe-detail-actions">
                <button class="secondary-action" type="button" data-stop-recipe>Ngừng sử dụng</button>
                <button class="primary-action" type="button" data-edit-recipe>Sửa công thức</button>
            </div>`;
        recipeDetail.querySelector("[data-edit-recipe]").addEventListener("click", () => openRecipeDialog(size.sizeId, recipe.recipeId));
        recipeDetail.querySelector("[data-stop-recipe]").addEventListener("click", () => deactivateRecipe(recipe.recipeId, size.sizeId));
    }

    function ingredientOptions(selectedId) {
        return '<option value="">Chọn nguyên liệu</option>' + state.ingredients.map((ingredient) => `
            <option value="${ingredient.ingredientId}" ${ingredient.ingredientId === selectedId ? "selected" : ""}>${escapeHtml(ingredient.name)}</option>
        `).join("");
    }

    function addIngredientRow(item = {}) {
        const row = document.createElement("div");
        row.className = "recipe-ingredient-row";
        row.innerHTML = `
            <label class="form-field">
                <span>Nguyên liệu</span>
                <select data-ingredient-id required>${ingredientOptions(Number(item.ingredientId) || null)}</select>
            </label>
            <label class="form-field recipe-quantity-field">
                <span>Định lượng</span>
                <input data-ingredient-quantity type="number" inputmode="decimal" min="0.001" step="0.001" value="${item.quantity ?? ""}" required>
                <span class="recipe-unit" data-ingredient-unit></span>
            </label>
            <button class="remove-ingredient-button" type="button" aria-label="Xóa nguyên liệu" title="Xóa nguyên liệu">×</button>`;

        const select = row.querySelector("[data-ingredient-id]");
        const updateUnit = () => {
            const ingredient = state.ingredients.find((entry) => entry.ingredientId === Number(select.value));
            row.querySelector("[data-ingredient-unit]").textContent = ingredient?.unit || "";
        };
        select.addEventListener("change", updateUnit);
        row.querySelector(".remove-ingredient-button").addEventListener("click", () => {
            row.remove();
            ensureIngredientRow();
        });
        recipeIngredientRows.appendChild(row);
        updateUnit();
    }

    function ensureIngredientRow() {
        if (!recipeIngredientRows.children.length) addIngredientRow();
    }

    async function openRecipeDialog(sizeId, recipeId = null) {
        const size = getSize(sizeId);
        if (!size) return;

        if (!state.ingredients.length) {
            openIngredientDialog("Hãy tạo nguyên liệu trước khi thiết lập công thức.");
            return;
        }

        state.editingRecipeId = recipeId;
        recipeForm.reset();
        recipeIngredientRows.innerHTML = "";
        recipeFormMessage.textContent = "";
        recipeFormMessage.className = "form-message";
        document.querySelector("#recipeSizeId").value = String(sizeId);
        recipeDialogTitle.textContent = recipeId ? "Sửa công thức" : "Tạo công thức";
        recipeDialogContext.textContent = `${size.foodName} - ${size.sizeName}`;
        recipeSubmit.textContent = "Lưu công thức";

        try {
            if (recipeId) {
                const details = await window.restaurantApi.get(`/management/recipes/${recipeId}`);
                recipeForm.elements.name.value = details.name;
                recipeForm.elements.version.value = details.version;
                details.ingredients.forEach(addIngredientRow);
            } else {
                recipeForm.elements.name.value = `${size.foodName} ${size.sizeName}`;
                recipeForm.elements.version.value = "1";
                addIngredientRow();
            }
            recipeDialog.showModal();
            recipeForm.elements.name.focus();
        } catch (error) {
            window.alert(error.message);
        }
    }

    function openIngredientDialog(messageText = "") {
        ingredientForm.reset();
        ingredientFormMessage.textContent = messageText;
        ingredientFormMessage.className = messageText ? "form-message is-error" : "form-message";
        ingredientDialog.showModal();
        ingredientForm.elements.name.focus();
    }

    async function deactivateRecipe(recipeId, sizeId) {
        try {
            await window.restaurantApi.delete(`/management/recipes/${recipeId}`);
            state.selectedDetails = null;
            await loadData(sizeId);
            renderDetail();
        } catch (error) {
            window.alert(error.message);
        }
    }

    function setSubmitting(button, loading, idleText) {
        button.disabled = loading;
        button.textContent = loading ? "Đang lưu..." : idleText;
    }

    recipeForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (!recipeForm.checkValidity()) {
            recipeForm.reportValidity();
            return;
        }

        const ingredients = Array.from(recipeIngredientRows.querySelectorAll(".recipe-ingredient-row")).map((row) => ({
            ingredientId: Number(row.querySelector("[data-ingredient-id]").value),
            quantity: Number(row.querySelector("[data-ingredient-quantity]").value)
        }));
        const uniqueIds = new Set(ingredients.map((item) => item.ingredientId));
        if (uniqueIds.size !== ingredients.length) {
            recipeFormMessage.textContent = "Một nguyên liệu không được lặp lại trong cùng công thức.";
            recipeFormMessage.className = "form-message is-error";
            return;
        }

        const sizeId = Number(document.querySelector("#recipeSizeId").value);
        const payload = {
            sizeId,
            name: recipeForm.elements.name.value.trim(),
            version: recipeForm.elements.version.value.trim() || "1",
            ingredients
        };

        setSubmitting(recipeSubmit, true, "Lưu công thức");
        try {
            if (state.editingRecipeId) {
                await window.restaurantApi.put(`/management/recipes/${state.editingRecipeId}`, payload);
            } else {
                await window.restaurantApi.post("/management/recipes", payload);
            }
            recipeDialog.close();
            await loadData(sizeId);
            const recipe = getRecipeForSize(sizeId);
            if (recipe) await selectRecipe(sizeId, recipe.recipeId);
        } catch (error) {
            recipeFormMessage.textContent = error.message;
            recipeFormMessage.className = "form-message is-error";
        } finally {
            setSubmitting(recipeSubmit, false, "Lưu công thức");
        }
    });

    ingredientForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (!ingredientForm.checkValidity()) {
            ingredientForm.reportValidity();
            return;
        }

        const data = new FormData(ingredientForm);
        setSubmitting(ingredientSubmit, true, "Tạo nguyên liệu");
        try {
            await window.restaurantApi.post("/management/ingredients", {
                name: data.get("name").trim(),
                unit: data.get("unit"),
                minimumStock: Number(data.get("minimumStock"))
            });
            ingredientDialog.close();
            await loadData();
        } catch (error) {
            ingredientFormMessage.textContent = error.message;
            ingredientFormMessage.className = "form-message is-error";
        } finally {
            setSubmitting(ingredientSubmit, false, "Tạo nguyên liệu");
        }
    });

    recipeSearch.addEventListener("input", () => {
        state.search = recipeSearch.value.trim();
        renderList();
    });
    foodFilter.addEventListener("change", () => {
        state.foodId = foodFilter.value;
        renderList();
    });
    statusFilter.querySelectorAll("[data-status]").forEach((button) => {
        button.addEventListener("click", () => {
            state.status = button.dataset.status;
            statusFilter.querySelectorAll("[data-status]").forEach((item) => item.classList.toggle("is-active", item === button));
            renderList();
        });
    });

    document.querySelector("#addIngredientButton").addEventListener("click", () => openIngredientDialog());
    document.querySelector("#addIngredientRowButton").addEventListener("click", () => addIngredientRow());
    document.querySelector("#closeRecipeDetail").addEventListener("click", () => recipeDetailPanel.classList.remove("is-open"));
    document.querySelectorAll("[data-close-recipe-dialog]").forEach((button) => {
        button.addEventListener("click", () => document.getElementById(button.dataset.closeRecipeDialog).close());
    });
    document.querySelectorAll("[data-page]").forEach((button) => {
        button.addEventListener("click", () => { window.location.href = button.dataset.page; });
    });

    loadData();
})();
