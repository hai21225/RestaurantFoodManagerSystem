(function () {
    const $ = (selector) => document.querySelector(selector);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const money = (value) => typeof value === "number" && Number.isFinite(value) ? `${value.toLocaleString("vi-VN")}đ` : "0đ";
    const api = window.restaurantApi;
    const auth = window.restaurantAuth;

    const orderStatusLabels = {
        Pending: { text: "Chờ bếp nhận", cls: "is-pending" },
        SentToKitchen: { text: "Đã gửi bếp", cls: "is-pending" },
        Accepted: { text: "Đã nhận", cls: "is-confirmed" },
        Preparing: { text: "Đang chế biến", cls: "is-occupied" },
        Ready: { text: "Sẵn sàng phục vụ", cls: "is-available" },
        Completed: { text: "Hoàn tất", cls: "is-available" },
        Paid: { text: "Đã thanh toán", cls: "is-paid" },
        Cancelled: { text: "Đã hủy", cls: "is-cancelled" }
    };

    const params = new URLSearchParams(window.location.search);
    const state = {
        qrToken: (params.get("qrToken") || "").trim(),
        tableId: Number(params.get("tableId")) || 0,
        tables: [],
        categories: [],
        foods: [],
        selectedCategory: "all",
        search: "",
        cart: [],
        myOrders: [],
        activeDish: null,
        busy: false
    };

    function showNotice(text, type = "") {
        const el = $("#qrNotice");
        if (!text) {
            el.hidden = true;
            el.textContent = "";
            return;
        }
        el.textContent = text;
        el.className = `v2-notice${type ? ` is-${type}` : ""}`;
        el.hidden = false;
    }

    function normalizeSearch(value) {
        return String(value ?? "").toLocaleLowerCase("vi-VN").normalize("NFD")
            .replace(/[\u0300-\u036f]/g, "").replace(/đ/g, "d")
            .replace(/[^a-z0-9]+/g, " ").trim();
    }

    function matchesFood(food) {
        if (!state.search) return true;
        const name = normalizeSearch(food.foodName);
        const terms = state.search.split(" ");
        if (terms.every((term) => name.includes(term))) return true;
        const initials = name.split(" ").map((w) => w[0] || "").join("");
        return terms.length === 1 && state.search.length >= 2 && initials.startsWith(state.search);
    }

    function safeImageUrl(rawUrl) {
        if (!rawUrl) return "";
        try {
            const url = new URL(rawUrl, window.location.href);
            return ["http:", "https:"].includes(url.protocol) ? url.href : "";
        } catch (_) {
            return "";
        }
    }

    function renderTableSelectors() {
        const activeTables = state.tables.filter((t) => t.status !== "Maintenance");
        const options = activeTables.map((t) =>
            `<option value="${t.tableId}">Bàn ${esc(t.tableNumber)} (${esc(t.areaName || "Khu chung")})</option>`
        ).join("");
        $("#manualTableSelect").innerHTML = options || '<option value="">Không có bàn</option>';
        $("#qrTableSelect").innerHTML = '<option value="">-- Chọn bàn --</option>' + options;
        if (state.tableId > 0) {
            $("#manualTableSelect").value = String(state.tableId);
            $("#qrTableSelect").value = String(state.tableId);
        }
        const selected = activeTables.find((t) => t.tableId === state.tableId);
        $("#qrTableTitle").textContent = selected ? `Bàn ${selected.tableNumber} · ${selected.areaName || "Khu chung"}` : "Chưa chọn bàn";
        $("#qrTableSelect").hidden = Boolean(selected);
        $("#qrTokenDisplay").textContent = state.qrToken ? `Mã QR: ${state.qrToken}` : "";
    }

    function renderGate() {
        const hasToken = Boolean(state.qrToken);
        $("#qrGatePanel").hidden = hasToken;
        $("#qrWorkspace").hidden = !hasToken;
        renderTableSelectors();
    }

    function renderCategories() {
        const list = [{ id: "all", name: "Tất cả" }, ...state.categories];
        $("#qrCategoryTabs").innerHTML = list.map((cat) => {
            const count = cat.id === "all" ? state.foods.length : state.foods.filter((f) => f.categoryId === cat.id).length;
            const active = state.selectedCategory === cat.id;
            return `<button type="button" class="v2-pill${active ? " is-active" : ""}" role="tab"
                aria-selected="${active}" data-category-id="${esc(cat.id)}">${esc(cat.name)} (${count})</button>`;
        }).join("");
    }

    function renderMenu() {
        const visible = state.foods.filter((f) => (state.selectedCategory === "all" || f.categoryId === state.selectedCategory) && matchesFood(f));
        $("#qrFoodCount").textContent = `${visible.length} món`;
        if (!visible.length) {
            $("#qrFoodGrid").innerHTML = '<div class="empty-state"><strong>Không tìm thấy món</strong><span>Thử từ khóa khác hoặc chọn Tất cả.</span></div>';
            return;
        }
        $("#qrFoodGrid").innerHTML = visible.map((food) => {
            const defSize = (food.sizes || []).find((s) => s.isDefault) || food.sizes[0];
            const initials = (food.foodName || "M").trim().split(/\s+/).slice(0, 2).map((w) => w[0]).join("").toLocaleUpperCase("vi-VN");
            const imgUrl = safeImageUrl(food.foodImageUrl);
            return `<article class="v2-food-card">
                <div class="v2-food-head">
                    <div class="v2-food-thumb" aria-hidden="true">
                        <span>${esc(initials)}</span>
                        ${imgUrl ? `<img src="${esc(imgUrl)}" alt="" loading="lazy" data-qr-food-img>` : ""}
                    </div>
                    <div>
                        <h3 class="v2-food-title">${esc(food.foodName)}</h3>
                        <p class="v2-food-desc">${money(defSize?.sizePrice)} · ${esc(defSize?.sizeName || "")}</p>
                    </div>
                </div>
                <button class="primary-action" type="button" data-add-food="${food.foodId}" ${state.busy ? "disabled" : ""}>Chọn món</button>
            </article>`;
        }).join("");
        $("#qrFoodGrid").querySelectorAll("[data-qr-food-img]").forEach((img) => {
            img.addEventListener("error", () => { img.hidden = true; });
        });
    }

    function renderCart() {
        const totalQty = state.cart.reduce((sum, item) => sum + item.quantity, 0);
        const totalPrice = state.cart.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0);
        $("#qrCartCount").textContent = `${totalQty} phần`;
        $("#qrCartTotal").textContent = money(totalPrice);
        $("#qrSendOrderBtn").disabled = state.busy || !state.cart.length || !state.qrToken;
        $("#qrClearCartBtn").disabled = state.busy || !state.cart.length;

        const openOrder = findOpenOrder();
        $("#qrSendOrderBtn").textContent = state.busy ? "Đang gửi…" : openOrder ? `Gọi thêm vào đơn #${openOrder.orderId}` : "Gửi gọi món";

        if (!state.cart.length) {
            $("#qrCartList").innerHTML = '<p class="v2-notice">Giỏ món đang trống. Chọn món từ thực đơn.</p>';
            return;
        }
        $("#qrCartList").innerHTML = state.cart.map((item, idx) => `
            <div class="v2-cart-row">
                <div>
                    <strong>${esc(item.foodName)} (${esc(item.sizeName)})</strong>
                    ${item.toppingNames.length ? `<div class="v2-promo-meta">+ ${esc(item.toppingNames.join(", "))}</div>` : ""}
                    ${item.note ? `<div class="v2-promo-meta">Ghi chú: ${esc(item.note)}</div>` : ""}
                    <div class="v2-promo-meta">${money(item.unitPrice * item.quantity)}</div>
                </div>
                <div class="v2-qty-group">
                    <button class="v2-qty-btn" type="button" data-cart-idx="${idx}" data-delta="-1" aria-label="Giảm">−</button>
                    <strong>${item.quantity}</strong>
                    <button class="v2-qty-btn" type="button" data-cart-idx="${idx}" data-delta="1" aria-label="Tăng">+</button>
                </div>
            </div>`).join("");
    }

    function findOpenOrder() {
        return state.myOrders.find((o) =>
            !["Paid", "Cancelled"].includes(o.status) && (!state.tableId || o.tableId === state.tableId)
        ) || null;
    }

    function renderMyOrders() {
        const list = state.myOrders;
        $("#myOrdersCount").textContent = `${list.length} đơn`;
        if (!list.length) {
            $("#myOrdersList").innerHTML = '<p class="v2-notice">Bạn chưa có đơn hàng nào.</p>';
            return;
        }
        $("#myOrdersList").innerHTML = list.map((o) => {
            const st = orderStatusLabels[o.status] || { text: o.status, cls: "" };
            return `<article class="v2-res-card">
                <div class="v2-res-head">
                    <strong>Đơn #${o.orderId} · Bàn ${esc(o.tableNumber || o.tableId)}</strong>
                    <span class="v2-badge ${st.cls}">${esc(st.text)}</span>
                </div>
                <div class="v2-res-meta">
                    <span>Tổng tiền: <strong>${money(o.totalAmount)}</strong></span>
                    ${o.items ? `<span>${o.items.length} dòng món</span>` : ""}
                </div>
            </article>`;
        }).join("");
    }

    async function loadMyOrders() {
        if (!auth.hasRole("Customer")) {
            state.myOrders = [];
            renderMyOrders();
            return;
        }
        try {
            const data = await api.get("/orders/my");
            state.myOrders = Array.isArray(data) ? data : (data?.items || []);
            for (const o of state.myOrders) {
                if (o?.orderId && !["Paid", "Cancelled"].includes(o.status)) {
                    window.restaurantRealtime?.joinOrder(o.orderId);
                }
            }
            renderMyOrders();
            renderCart();
        } catch (_) {
            // Keep existing orders if transient error
        }
    }

    async function verifyQrToken(token, expectedTableId = 0) {
        const cleaned = String(token ?? "").trim();
        if (!cleaned) {
            state.qrToken = "";
            return false;
        }
        try {
            const table = await api.get(`/public/tables/resolve-qr?qrToken=${encodeURIComponent(cleaned)}`);
            if (expectedTableId && table.tableId !== expectedTableId) {
                state.qrToken = "";
                showNotice(`Mã QR này không khớp với bàn đã chọn (đây là mã của Bàn ${table.tableNumber}).`, "error");
                return false;
            }
            state.qrToken = cleaned;
            state.tableId = table.tableId;
            return true;
        } catch (error) {
            state.qrToken = "";
            showNotice(error.message || "Mã QR bàn không hợp lệ hoặc bàn đang tạm ngừng phục vụ.", "error");
            return false;
        }
    }

    async function loadMenu() {
        try {
            const menu = await api.get("/menu");
            state.categories = (Array.isArray(menu) ? menu : []).map((c) => ({ id: String(c.categoryId), name: c.categoryName }));
            state.foods = (Array.isArray(menu) ? menu : []).flatMap((c) =>
                (c.foods || []).filter((f) => (f.sizes || []).length).map((f) => ({ ...f, categoryId: String(c.categoryId) }))
            );
            renderCategories();
            renderMenu();
        } catch (_) {
            // Keep existing menu if transient error
        }
    }

    async function loadInitialData() {
        try {
            const [tables, menu] = await Promise.all([
                api.get("/public/tables"),
                api.get("/menu")
            ]);
            state.tables = Array.isArray(tables) ? tables : [];
            state.categories = (Array.isArray(menu) ? menu : []).map((c) => ({ id: String(c.categoryId), name: c.categoryName }));
            state.foods = (Array.isArray(menu) ? menu : []).flatMap((c) =>
                (c.foods || []).filter((f) => (f.sizes || []).length).map((f) => ({ ...f, categoryId: String(c.categoryId) }))
            );
            if (state.qrToken) {
                await verifyQrToken(state.qrToken, state.tableId);
            }
            renderGate();
            renderCategories();
            renderMenu();
            await loadMyOrders();
        } catch (error) {
            showNotice(error.message || "Không thể tải thực đơn và danh sách bàn.", "error");
        }
    }

    function openDishDialog(foodId) {
        const food = state.foods.find((f) => f.foodId === foodId);
        if (!food) return;
        state.activeDish = food;
        $("#qrDishForm").reset();
        $("#qrDishTitle").textContent = food.foodName;
        const defSize = food.sizes.find((s) => s.isDefault) || food.sizes[0];
        $("#qrSizeOptions").innerHTML = food.sizes.map((s) => `
            <label class="v2-table-item">
                <span><input type="radio" name="sizeId" value="${s.sizeId}" ${s.sizeId === defSize.sizeId ? "checked" : ""}> ${esc(s.sizeName)}</span>
                <strong>${money(s.sizePrice)}</strong>
            </label>`).join("");
        const toppings = food.toppings || [];
        $("#qrToppingWrap").hidden = !toppings.length;
        $("#qrToppingOptions").innerHTML = toppings.map((t) => {
            const outOfStock = t.stockQuantity !== null && t.stockQuantity !== undefined && t.stockQuantity <= 0;
            const disabled = !t.isAvailable || outOfStock;
            const stockLabel = outOfStock ? "Hết hàng" : t.stockQuantity != null ? `Còn ${t.stockQuantity}` : (!t.isAvailable ? "Tạm hết" : "");
            return `
            <label class="v2-table-item"${disabled ? ' style="opacity:0.6"' : ""}>
                <span><input type="checkbox" name="toppingId" value="${t.toppingId}"${disabled ? " disabled" : ""}> ${esc(t.toppingName)}${stockLabel ? ` <small>(${esc(stockLabel)})</small>` : ""}</span>
                <strong>+${money(t.toppingPrice)}</strong>
            </label>`;
        }).join("");
        $("#qrDishDialog").showModal();
    }

    $("#qrManualForm").addEventListener("submit", async (event) => {
        event.preventDefault();
        const tId = Number($("#manualTableSelect").value);
        const token = $("#manualQrInput").value.trim();
        if (!token) {
            showNotice("Vui lòng nhập mã QR bàn.", "error");
            return;
        }
        showNotice("");
        const ok = await verifyQrToken(token, tId);
        renderGate();
        if (ok) renderCart();
    });

    $("#changeQrBtn").addEventListener("click", () => {
        state.qrToken = "";
        renderGate();
    });

    $("#qrTableSelect").addEventListener("change", (event) => {
        state.tableId = Number(event.target.value) || 0;
        renderTableSelectors();
        renderCart();
    });

    $("#qrCategoryTabs").addEventListener("click", (event) => {
        const btn = event.target.closest("[data-category-id]");
        if (!btn) return;
        state.selectedCategory = btn.dataset.categoryId;
        renderCategories();
        renderMenu();
    });

    $("#qrSearchInput").addEventListener("input", (event) => {
        state.search = normalizeSearch(event.target.value);
        renderMenu();
    });

    $("#qrFoodGrid").addEventListener("click", (event) => {
        const btn = event.target.closest("[data-add-food]");
        if (!btn) return;
        openDishDialog(Number(btn.dataset.addFood));
    });

    $("#closeQrDishBtn").addEventListener("click", () => $("#qrDishDialog").close());

    function getQrCartToppingUsage(toppingId) {
        return state.cart.reduce((sum, item) => {
            const has = item.toppings?.some((t) => t.toppingId === toppingId);
            return sum + (has ? item.quantity : 0);
        }, 0);
    }

    $("#qrDishForm").addEventListener("submit", (event) => {
        event.preventDefault();
        const food = state.activeDish;
        if (!food) return;
        const sizeId = Number(event.target.querySelector('input[name="sizeId"]:checked')?.value);
        const size = food.sizes.find((s) => s.sizeId === sizeId) || food.sizes[0];
        const toppingIds = Array.from(event.target.querySelectorAll('input[name="toppingId"]:checked')).map((el) => Number(el.value));
        const selectedToppings = (food.toppings || []).filter((t) => toppingIds.includes(t.toppingId));
        const quantity = Math.max(1, Number($("#qrDishQty").value) || 1);
        for (const t of selectedToppings) {
            if (!t.isAvailable || (t.stockQuantity !== null && t.stockQuantity !== undefined && t.stockQuantity <= 0)) {
                showNotice(`Topping "${t.toppingName}" hiện đã hết hàng.`, "error");
                return;
            }
            if (t.stockQuantity != null) {
                const used = getQrCartToppingUsage(t.toppingId);
                if (used + quantity > t.stockQuantity) {
                    showNotice(`Topping "${t.toppingName}" chỉ còn ${t.stockQuantity} (trong giỏ đã có ${used}).`, "error");
                    return;
                }
            }
        }
        const note = $("#qrDishNote").value.trim();
        const unitPrice = size.sizePrice + selectedToppings.reduce((sum, t) => sum + t.toppingPrice, 0);

        state.cart.push({
            foodId: food.foodId,
            foodName: food.foodName,
            sizeId: size.sizeId,
            sizeName: size.sizeName,
            toppings: toppingIds.map((id) => ({ toppingId: id })),
            toppingNames: selectedToppings.map((t) => t.toppingName),
            quantity,
            note,
            unitPrice
        });
        $("#qrDishDialog").close();
        renderCart();
    });

    $("#qrCartList").addEventListener("click", (event) => {
        const btn = event.target.closest("[data-cart-idx]");
        if (!btn) return;
        const idx = Number(btn.dataset.cartIdx);
        const delta = Number(btn.dataset.delta);
        const cartItem = state.cart[idx];
        if (!cartItem) return;
        if (delta > 0 && cartItem.toppings?.length) {
            const food = state.foods.find((f) => f.foodId === cartItem.foodId);
            for (const entry of cartItem.toppings) {
                const topping = food?.toppings?.find((t) => t.toppingId === entry.toppingId);
                if (topping && topping.stockQuantity != null) {
                    const used = getQrCartToppingUsage(entry.toppingId);
                    if (used + delta > topping.stockQuantity) {
                        showNotice(`Topping "${topping.toppingName}" chỉ còn ${topping.stockQuantity}.`, "error");
                        return;
                    }
                }
            }
        }
        cartItem.quantity += delta;
        if (cartItem.quantity <= 0) state.cart.splice(idx, 1);
        renderCart();
    });

    $("#qrClearCartBtn").addEventListener("click", () => {
        state.cart = [];
        renderCart();
    });

    $("#qrSendOrderBtn").addEventListener("click", async () => {
        if (state.busy || !state.cart.length || !state.qrToken) return;
        const openOrder = findOpenOrder();
        if (!openOrder && !state.tableId) {
            showNotice("Vui lòng chọn bàn trước khi gửi gọi món.", "error");
            return;
        }
        state.busy = true;
        renderCart();
        const itemsPayload = state.cart.map((item) => ({
            foodId: item.foodId,
            sizeId: item.sizeId,
            quantity: item.quantity,
            note: item.note || null,
            toppings: item.toppings
        }));
        const idempotencyKey = (window.crypto?.randomUUID?.() || `qr-${Date.now()}-${Math.random().toString(16).slice(2)}`);
        try {
            if (openOrder) {
                await api.post(`/orders/${openOrder.orderId}/items`, {
                    items: itemsPayload,
                    tableQrToken: state.qrToken
                }, { headers: { "Idempotency-Key": idempotencyKey } });
                showNotice(`Đã gọi thêm món vào đơn #${openOrder.orderId}!`, "success");
            } else {
                const created = await api.post("/orders", {
                    tableId: state.tableId,
                    tableQrToken: state.qrToken,
                    note: $("#qrOrderNote").value.trim() || null,
                    items: itemsPayload
                }, { headers: { "Idempotency-Key": idempotencyKey } });
                showNotice(`Đã gửi đơn #${created?.orderId || ""} thành công!`, "success");
            }
            state.cart = [];
            $("#qrOrderNote").value = "";
            await loadMyOrders();
        } catch (error) {
            showNotice(error.message || "Không thể gửi gọi món.", "error");
        } finally {
            state.busy = false;
            renderCart();
        }
    });

    $("#refreshMyOrdersBtn").addEventListener("click", () => loadMyOrders());

    window.addEventListener("restaurant:realtime", (event) => {
        const evtName = String(event.detail?.event || "");
        if (/order|kitchen|table/i.test(evtName)) {
            loadMyOrders();
        }
        if (/topping/i.test(evtName)) {
            loadMenu();
        }
    });

    window.addEventListener("restaurant:realtime-connected", () => {
        loadMyOrders();
    });

    auth.ready.then(() => {
        loadInitialData();
    });
})();
