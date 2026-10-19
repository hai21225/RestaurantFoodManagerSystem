(function () {
    const list = document.querySelector("#purchaseList");
    const count = document.querySelector("#purchaseCount");
    const summaryValues = document.querySelectorAll(".management-summary strong");
    const pageMessage = document.querySelector("#uiMessage");
    const purchaseDialog = document.querySelector("#purchaseDialog");
    const purchaseForm = document.querySelector("#purchaseForm");
    const purchaseSupplier = document.querySelector("#purchaseSupplier");
    const purchaseItemRows = document.querySelector("#purchaseItemRows");
    const purchaseTotal = document.querySelector("#purchaseTotal");
    const purchaseFormMessage = document.querySelector("#purchaseFormMessage");
    const purchaseSubmit = document.querySelector("#purchaseSubmit");
    const addPurchaseItemButton = document.querySelector("#addPurchaseItemButton");
    const purchaseIngredientLink = document.querySelector("#purchaseIngredientLink");
    const supplierDialog = document.querySelector("#supplierDialog");
    const supplierForm = document.querySelector("#supplierForm");
    const supplierFormMessage = document.querySelector("#supplierFormMessage");
    const supplierSubmit = document.querySelector("#supplierSubmit");
    const addSupplierButton = document.querySelector("#addSupplierButton");

    let suppliers = [];
    let ingredients = [];
    let isSubmitting = false;
    let optionsRequestId = 0;

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function showMessage(element, message, isError = false) {
        element.textContent = message;
        element.className = isError ? "form-message is-error" : "form-message";
    }

    function formatCurrency(value) {
        return `${value.toLocaleString("vi-VN")}đ`;
    }

    const monthFormatter = new Intl.DateTimeFormat("en-CA", {
        timeZone: "Asia/Ho_Chi_Minh",
        year: "numeric",
        month: "2-digit"
    });

    function monthOf(value) {
        if (!value) return "";
        const timestamp = String(value);
        const date = new Date(/(?:Z|[+-]\d{2}:\d{2})$/i.test(timestamp) ? timestamp : `${timestamp}Z`);
        return Number.isNaN(date.getTime()) ? "" : monthFormatter.format(date);
    }

    async function loadPurchases() {
        list.className = "empty-workspace";
        list.innerHTML = "<div><strong>Đang tải phiếu nhập...</strong></div>";
        try {
            const purchases = await window.restaurantApi.get("/management/purchase-orders");
            const statusOf = (item) => String(item.status || "").toLowerCase();
            count.textContent = `${purchases.length} phiếu`;
            summaryValues[0].textContent = String(purchases.filter((item) => ["draft", "pending"].includes(statusOf(item))).length);
            summaryValues[1].textContent = String(purchases.filter((item) => ["approved", "ordered"].includes(statusOf(item))).length);
            summaryValues[2].textContent = String(purchases.filter((item) => statusOf(item) === "received").length);
            const currentMonth = monthOf(new Date().toISOString());
            const monthTotal = purchases
                .filter((item) => statusOf(item) === "received" && monthOf(item.receivedAt) === currentMonth)
                .reduce((sum, item) => sum + (Number(item.totalAmount) || 0), 0);
            summaryValues[3].textContent = formatCurrency(monthTotal);

            list.className = "management-rows";
            list.innerHTML = purchases.length ? purchases.map((item) => `
                <article class="management-row">
                    <div><strong>Phiếu #${escapeHtml(item.id)}</strong><span>Nhà cung cấp #${escapeHtml(item.supplierId)}</span></div>
                    <div><span>Trạng thái</span><strong>${escapeHtml(item.status)}</strong></div>
                    <div><span>Tổng tiền</span><strong>${formatCurrency(Number(item.totalAmount) || 0)}</strong></div>
                </article>
            `).join("") : '<div class="empty-workspace"><div><strong>Chưa có phiếu nhập hàng</strong></div></div>';
        } catch (error) {
            list.className = "empty-workspace";
            list.innerHTML = `<div><strong>Không tải được phiếu nhập</strong><span>${escapeHtml(error.message)}</span></div>`;
        }
    }

    function updateSubmitAvailability() {
        purchaseSubmit.disabled = isSubmitting || !purchaseSupplier.value || !ingredients.length;
    }

    function renderSupplierOptions(preferredId) {
        const selectedId = String(preferredId || purchaseSupplier.value || "");
        purchaseSupplier.innerHTML = '<option value="">Chọn nhà cung cấp</option>' + suppliers.map((supplier) =>
            `<option value="${supplier.supplierId}">${escapeHtml(supplier.name)}</option>`
        ).join("");
        if (suppliers.some((supplier) => String(supplier.supplierId) === selectedId)) {
            purchaseSupplier.value = selectedId;
        } else if (suppliers.length === 1) {
            purchaseSupplier.value = String(suppliers[0].supplierId);
        }
        updateSubmitAvailability();
    }

    function updateTotal() {
        const total = Array.from(purchaseItemRows.querySelectorAll(".purchase-item-row"))
            .reduce((sum, row) => {
                const quantity = Number(row.querySelector("[data-quantity]").value);
                const price = Number(row.querySelector("[data-price]").value);
                return sum + (quantity > 0 && price >= 0 ? quantity * price : 0);
            }, 0);
        purchaseTotal.textContent = `Tổng tiền: ${formatCurrency(total)}`;
    }

    function addItemRow() {
        if (!ingredients.length) return;
        const row = document.createElement("div");
        row.className = "purchase-item-row";
        row.innerHTML = `
            <label class="form-field purchase-ingredient-field">
                <span>Nguyên liệu</span>
                <select data-ingredient required>
                    <option value="">Chọn nguyên liệu</option>
                    ${ingredients.map((ingredient) =>
                        `<option value="${ingredient.ingredientId}">${escapeHtml(ingredient.name)}</option>`
                    ).join("")}
                </select>
                <small data-unit></small>
            </label>
            <label class="form-field purchase-quantity-field">
                <span>Số lượng</span>
                <input data-quantity type="number" inputmode="decimal" min="0.001" step="0.001" required>
            </label>
            <label class="form-field purchase-price-field">
                <span>Đơn giá</span>
                <input data-price type="number" inputmode="decimal" min="0" step="1" required>
            </label>
            <button class="purchase-remove-line" type="button" aria-label="Xóa dòng" title="Xóa dòng">×</button>`;

        const ingredientSelect = row.querySelector("[data-ingredient]");
        ingredientSelect.addEventListener("change", () => {
            const ingredient = ingredients.find((entry) => String(entry.ingredientId) === ingredientSelect.value);
            row.querySelector("[data-unit]").textContent = ingredient?.unit || "";
            updateTotal();
        });
        row.querySelector("[data-quantity]").addEventListener("input", updateTotal);
        row.querySelector("[data-price]").addEventListener("input", updateTotal);
        row.querySelector(".purchase-remove-line").addEventListener("click", () => {
            row.remove();
            if (!purchaseItemRows.children.length) addItemRow();
            updateTotal();
        });
        purchaseItemRows.appendChild(row);
    }

    async function openPurchaseDialog() {
        if (purchaseDialog.open) return;
        const requestId = ++optionsRequestId;
        suppliers = [];
        ingredients = [];
        purchaseForm.reset();
        purchaseItemRows.replaceChildren();
        purchaseSupplier.innerHTML = '<option value="">Đang tải...</option>';
        purchaseTotal.textContent = "Tổng tiền: 0đ";
        showMessage(purchaseFormMessage, "Đang tải nhà cung cấp và nguyên liệu...");
        purchaseSubmit.disabled = true;
        addPurchaseItemButton.disabled = true;
        addSupplierButton.disabled = true;
        purchaseIngredientLink.hidden = true;
        purchaseDialog.showModal();

        try {
            const [supplierList, ingredientList] = await Promise.all([
                window.restaurantApi.get("/management/suppliers"),
                window.restaurantApi.get("/management/ingredients")
            ]);
            if (!purchaseDialog.open || requestId !== optionsRequestId) return;
            suppliers = supplierList.filter((item) => item.isActive);
            ingredients = ingredientList.filter((item) => item.isActive);
            renderSupplierOptions();
            addSupplierButton.disabled = false;
            addPurchaseItemButton.disabled = !ingredients.length;
            purchaseIngredientLink.hidden = Boolean(ingredients.length);
            if (ingredients.length) addItemRow();
            if (!suppliers.length) {
                showMessage(purchaseFormMessage, "Chưa có nhà cung cấp. Hãy thêm nhà cung cấp trước.");
                addSupplierButton.focus();
            } else if (!ingredients.length) {
                showMessage(purchaseFormMessage, "Chưa có nguyên liệu để nhập hàng.");
            } else {
                showMessage(purchaseFormMessage, "");
                purchaseSupplier.focus();
            }
            updateSubmitAvailability();
        } catch (error) {
            if (purchaseDialog.open && requestId === optionsRequestId) {
                purchaseSupplier.innerHTML = '<option value="">Không tải được nhà cung cấp</option>';
                showMessage(purchaseFormMessage, error.message, true);
            }
        }
    }

    function currentUserId() {
        try {
            const user = JSON.parse(window.sessionStorage.getItem("restaurant.currentUser") || "null");
            const id = Number(user?.userId);
            return Number.isInteger(id) && id > 0 ? id : null;
        } catch {
            return null;
        }
    }

    purchaseForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (isSubmitting) return;
        if (!purchaseForm.checkValidity()) {
            purchaseForm.reportValidity();
            return;
        }

        const items = Array.from(purchaseItemRows.querySelectorAll(".purchase-item-row")).map((row) => ({
            ingredientId: Number(row.querySelector("[data-ingredient]").value),
            quantity: Number(row.querySelector("[data-quantity]").value),
            unitPrice: Number(row.querySelector("[data-price]").value)
        }));
        if (!items.length || new Set(items.map((item) => item.ingredientId)).size !== items.length) {
            showMessage(purchaseFormMessage, "Mỗi nguyên liệu chỉ được thêm một lần trong phiếu.", true);
            return;
        }

        const userId = currentUserId();
        if (!userId) {
            showMessage(purchaseFormMessage, "Bạn cần đăng nhập trước khi tạo phiếu nhập.", true);
            return;
        }

        isSubmitting = true;
        purchaseSubmit.disabled = true;
        purchaseSubmit.textContent = "Đang tạo...";
        showMessage(purchaseFormMessage, "");
        try {
            const created = await window.restaurantApi.post("/management/purchase-orders", {
                supplierId: Number(purchaseSupplier.value),
                createdBy: userId,
                items
            });
            purchaseDialog.close();
            pageMessage.textContent = `Đã tạo phiếu nhập #${created.id} ở trạng thái Nháp.`;
            await loadPurchases();
        } catch (error) {
            showMessage(purchaseFormMessage, error.message, true);
        } finally {
            isSubmitting = false;
            purchaseSubmit.textContent = "Tạo phiếu nhập";
            updateSubmitAvailability();
        }
    });

    supplierForm.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (supplierSubmit.disabled) return;
        if (!supplierForm.checkValidity()) {
            supplierForm.reportValidity();
            return;
        }
        if (!currentUserId()) {
            showMessage(supplierFormMessage, "Bạn cần đăng nhập trước khi thêm nhà cung cấp.", true);
            return;
        }

        const data = new FormData(supplierForm);
        supplierSubmit.disabled = true;
        supplierSubmit.textContent = "Đang lưu...";
        showMessage(supplierFormMessage, "");
        try {
            const created = await window.restaurantApi.post("/management/suppliers", {
                name: data.get("name").trim(),
                phone: data.get("phone").trim(),
                email: data.get("email").trim(),
                address: data.get("address").trim()
            });
            supplierDialog.close();
            try {
                const supplierList = await window.restaurantApi.get("/management/suppliers");
                suppliers = supplierList.filter((item) => item.isActive);
                renderSupplierOptions(created.supplierId);
                showMessage(purchaseFormMessage, "");
                purchaseSupplier.focus();
            } catch (error) {
                showMessage(purchaseFormMessage, error.message, true);
            }
        } catch (error) {
            showMessage(supplierFormMessage, error.message, true);
        } finally {
            supplierSubmit.disabled = false;
            supplierSubmit.textContent = "Lưu nhà cung cấp";
        }
    });

    document.querySelector("#addPurchaseButton").addEventListener("click", openPurchaseDialog);
    purchaseSupplier.addEventListener("change", updateSubmitAvailability);
    addPurchaseItemButton.addEventListener("click", () => {
        addItemRow();
        purchaseItemRows.lastElementChild?.querySelector("[data-ingredient]")?.focus();
    });
    addSupplierButton.addEventListener("click", () => {
        supplierForm.reset();
        showMessage(supplierFormMessage, "");
        supplierDialog.showModal();
        supplierForm.elements.name.focus();
    });
    document.querySelectorAll("[data-close-purchase-dialog]").forEach((button) => {
        button.addEventListener("click", () => {
            if (!isSubmitting) purchaseDialog.close();
        });
    });
    document.querySelectorAll("[data-close-supplier-dialog]").forEach((button) => {
        button.addEventListener("click", () => {
            if (!supplierSubmit.disabled) supplierDialog.close();
        });
    });
    purchaseDialog.addEventListener("cancel", (event) => {
        if (isSubmitting) event.preventDefault();
    });
    supplierDialog.addEventListener("cancel", (event) => {
        if (supplierSubmit.disabled) event.preventDefault();
    });

    loadPurchases();
})();
