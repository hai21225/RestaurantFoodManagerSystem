(function () {
    const $ = (selector) => document.querySelector(selector);
    const escapeHtml = (value) => String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[char]));
    const money = (value) => typeof value === "number" && Number.isFinite(value) ? `${value.toLocaleString("vi-VN")}đ` : "—";
    const statusLabel = (value) => ({ Pending: "Chờ bếp nhận", SentToKitchen: "Đã gửi bếp", Accepted: "Phiếu đã được nhận", Preparing: "Bếp đang làm", Ready: "Sẵn sàng phục vụ", Completed: "Bếp đã hoàn tất", Paid: "Đã thanh toán", Cancelled: "Đã hủy" }[value] || value || "Chưa rõ");
    const state = { foods: [], categories: [], category: "all", search: "", cart: [], dish: null, mutation: null };
    const api = window.restaurantApi;
    const auth = window.restaurantAuth;
    const pos = window.restaurantPos;
    const timing = window.restaurantPreparationTiming;
    // Chỉ mở bàn được chọn rõ trên URL; mục Đặt món luôn trở về khung chọn bàn.
    const tableId = new URLSearchParams(window.location.search).get("tableId");
    const session = new pos.OrderSession({ tableId, api, auth, storage: window.sessionStorage });
    const dialog = $("#orderMutationDialog");
    const dishDialog = $("#sizeDialog");
    let sentVersion = "";
    let entryError = "";
    let timingRequest = 0;
    let preparationHistory = { orderId: null, rows: [], state: "idle" };
    const closed = () => session.order && pos.terminal(session.order.status);
    const hasValidTableId = () => Number.isInteger(session.tableId) && session.tableId > 0 && session.tableId <= 2147483647;
    const hasTableContext = () => session.isOperator() && session.table?.isActive && session.table.status !== "Maintenance" && (session.loaded || session.loading);
    const locked = () => !session.loaded || session.loading || session.busy || Boolean(session.pending) || session.hasExtraPending() || !session.isOperator() || closed() || !session.table?.isActive || session.table.status === "Maintenance" || (!session.order && session.table.status !== "Available");
    const findFood = (sizeId) => state.foods.find((food) => food.sizes.some((size) => size.sizeId === sizeId));

    function message(text, error = false) {
        $("#orderMessage").textContent = text;
        $(".pos-message").classList.toggle("is-error", error);
    }

    function renderEntryState() {
        const ready = Boolean(hasTableContext());
        const opening = !ready && hasValidTableId() && (session.loading || (!session.loaded && !entryError));
        const maintenance = session.loaded && session.table?.status === "Maintenance";
        $("#orderEntryCard").hidden = ready;
        $("#orderContextBar").hidden = !ready;
        $("#orderLayout").hidden = !ready;
        $("#cartDock").hidden = !ready;
        $("#activeOrderPanel").hidden = !ready || !session.order;
        $(".order-screen").classList.toggle("is-awaiting-table", !ready);
        $("#orderEntryTitle").textContent = opening ? "Đang mở bàn…" : maintenance ? "Bàn đang bảo trì" : entryError ? "Chưa mở được bàn" : "Chọn bàn để bắt đầu";
        $("#orderEntryDescription").textContent = opening ? "Đang tải thông tin bàn và đơn hiện tại."
            : maintenance ? "Bàn này tạm ngừng phục vụ. Chọn một bàn khác để đặt món."
            : entryError || "Chọn bàn đang phục vụ hoặc bàn trống. Thực đơn và đơn tạm sẽ xuất hiện sau khi mở bàn.";
        $("#orderEntryChooseTable").textContent = hasValidTableId() ? "Chọn bàn khác" : "Chọn bàn";
        $("#orderEntryRetry").hidden = !hasValidTableId() || opening;
        $("#orderEntryRetry").disabled = session.busy || session.loading;
        $(".pos-message").hidden = (!ready && !session.pending) || (!$("#orderMessage").textContent && !session.pending);
        if (!ready) $(".cart-panel").classList.remove("is-open");
    }

    function renderButtons() {
        const isLocked = locked();
        const sendLabel = session.busy ? "Đang xử lý…" : session.order ? "Gửi món gọi thêm" : "Gửi bếp";
        [$("#sendKitchenButton"), $("#cartSendButton")].forEach((button) => {
            button.textContent = sendLabel;
            button.disabled = isLocked || !state.cart.length;
        });
        $("#clearOrderButton").disabled = !state.cart.length || session.busy || Boolean(session.pending);
        $("#refreshOrderButton").disabled = session.busy || session.loading;
        $("#orderHistoryButton").disabled = session.busy || !session.order;
        $("#orderToolsButton").hidden = !session.order;
        $("#orderToolsButton").disabled = !session.order || session.busy || session.loading || Boolean(session.pending);
        $("#cancelWholeOrderButton").hidden = !session.isAdmin() || !session.isOpen();
        $("#cancelWholeOrderButton").disabled = isLocked;
        const ownPending = session.pending?.tableId === session.tableId;
        $("#retryOrderButton").hidden = !ownPending;
        $("#retryOrderButton").disabled = session.busy || session.loading;
        $("#pendingTableLink").hidden = !session.pending || ownPending;
        if (session.pending) $("#pendingTableLink").href = `order.html?tableId=${encodeURIComponent(session.pending.tableId)}&ui=20261112-print1`;
        $("#orderNoteInput").disabled = isLocked || Boolean(session.order);
        $("#cartList").querySelectorAll("button, input").forEach((input) => { input.disabled = isLocked; });
        $("#foodGrid").querySelectorAll("button").forEach((button) => { button.disabled = isLocked; });
        $("#sentOrderItems").querySelectorAll("button").forEach((button) => { button.disabled = isLocked; });
        $("#orderMutationForm").querySelectorAll("input, textarea, select, button").forEach((input) => { input.disabled = session.busy || Boolean(session.pending); });
        $("#orderMutationForm").querySelectorAll("[data-close-mutation]").forEach((button) => { button.disabled = session.busy; });
        if (state.mutation) {
            const mutation = state.mutation;
            const allowed = mutation.kind === "cancelItem" ? session.order?.items.some((item) => item.id === mutation.item.id && session.canCancel(item))
                : mutation.kind === "cancelOrder" ? session.isAdmin() && session.isOpen()
                : session.isOpen() && pos.groups(session.order).some((group) => group.key === mutation.group.key && group.active.length);
            $("#mutationConfirm").disabled = isLocked || !allowed;
        }
        $("#mutationConfirm").textContent = session.busy ? "Đang xử lý…" : "Xác nhận";
        if (session.pending && !session.busy) message("Yêu cầu trước chưa được xác nhận đầy đủ. Hãy thử xác nhận lại; không gửi một yêu cầu mới.", true);
        renderEntryState();
    }

    function renderTable() {
        const table = session.table;
        $("#order-title").textContent = table ? `Bàn ${table.tableNumber}` : tableId ? `Bàn ${tableId}` : "Chưa chọn bàn";
        $("#orderContext").textContent = !session.loaded ? "Chọn bàn hoặc làm mới để tải đơn thật."
            : closed() ? "Đơn đã kết thúc. Chọn bàn để mở lượt phục vụ mới."
            : !session.isOperator() ? "Bạn đang xem đơn; chỉ phục vụ và quản trị viên được sửa tại POS."
            : session.order ? [table.areaName, `Đơn #${session.order.orderId}`, "Chọn món bên dưới để gọi thêm."].filter(Boolean).join(" · ")
            : table.status === "Maintenance" ? "Bàn đang bảo trì. Hãy chọn bàn khác."
            : table.status !== "Available" ? "Chưa xác định được đơn của bàn đang sử dụng. Hãy làm mới hoặc chọn lại bàn."
            : "Bàn chưa có đơn đang mở. Chọn món rồi gửi bếp.";
    }

    function renderSentItems() {
        $("#activeOrderPanel").hidden = !session.order;
        if (!session.order) { sentVersion = ""; return; }
        const order = session.order;
        const version = JSON.stringify([order, auth.getUser()?.roles]);
        if (version === sentVersion) return;
        sentVersion = version;
        $("#orderNoteInput").value = order.note || "";
        $("#activeOrderTitle").textContent = `Món đã gửi · Đơn #${order.orderId}`;
        $("#activeOrderStatus").textContent = statusLabel(order.status);
        const isLocked = locked();
        $("#sentOrderItems").innerHTML = pos.groups(order).map((group) => {
            const first = group.items[0];
            const groupHint = !session.canReduce(group) && group.active.length ? "Bếp đã nhận món: chỉ được gọi tăng; cần quản trị viên để giảm." : "";
            return `<article class="sent-dish">
                <div class="sent-dish-heading"><div><strong>${escapeHtml(first.foodName || "Món đã ngừng bán")}</strong><p>${escapeHtml(first.sizeName)} · ${group.quantity} phần còn phục vụ</p></div>
                ${group.active.length && session.isOperator() && session.isOpen() ? `<button class="secondary-action" type="button" data-edit-group="${escapeHtml(group.key)}" ${isLocked ? "disabled" : ""}>${session.canReduce(group) ? "Đổi số lượng" : "Gọi tăng"}</button>` : ""}</div>
                ${group.items.map((item) => `<div class="sent-dish-line ${pos.cancelled(item) ? "is-cancelled" : ""}">
                    <div><span class="pos-item-status">${escapeHtml(statusLabel(item.status))} · ${item.quantity} phần</span>${session.isOpen() ? `<span class="ordered-time" data-ordered-item="${escapeHtml(item.id)}" aria-live="off">Đang lấy giờ gọi…</span>` : ""}${item.status === "Pending" && session.isOpen() ? `<span class="preparation-clock preparation-clock--waiting" data-waiting-item="${escapeHtml(item.id)}" aria-live="off">Đang lấy thời gian chờ…</span>` : ""}${item.status === "Preparing" && session.isOpen() ? `<span class="preparation-clock" data-preparation-item="${escapeHtml(item.id)}" aria-live="off">Đang lấy thời gian…</span>` : ""}<span class="sent-unit-price">Đơn giá: ${money(item.unitPrice)}</span>
                    ${item.note ? `<p>Ghi chú: ${escapeHtml(item.note)}</p>` : ""}
                    ${item.toppings?.length ? `<p>Topping mỗi phần: ${item.toppings.map((t) => `${escapeHtml(t.toppingName)} × ${t.quantity}`).join(", ")}</p>` : ""}
                    ${item.cancelReason ? `<p>Lý do hủy: ${escapeHtml(item.cancelReason)}</p>` : ""}</div>
                    ${session.canCancel(item) ? `<button class="secondary-action" type="button" data-cancel-item="${item.id}" ${isLocked ? "disabled" : ""}>Hủy ${item.quantity} phần</button>` : ""}
                </div>`).join("")}
                ${groupHint ? `<p class="field-hint">${groupHint}</p>` : ""}
            </article>`;
        }).join("") || '<p class="cart-empty">Đơn chưa có dòng món.</p>';
        [["#serverSubtotal", "subtotal"], ["#serverDiscount", "discountAmount"], ["#serverService", "serviceChargeAmount"], ["#serverTax", "taxAmount"], ["#serverTotal", "totalAmount"]]
            .forEach(([selector, field]) => { $(selector).textContent = money(order[field]); });
    }

    function renderPreparationTimes() {
        const order = session.order;
        const items = new Map((order?.items || []).map((item) => [String(item.id), item]));
        const history = preparationHistory.orderId === order?.orderId ? preparationHistory : { rows: [], state: "idle" };
        const notice = $("#preparationTimeNotice");
        const error = session.loaded ? history.error : "";
        const text = error ? `Chưa tải được giờ gọi và thời gian món. ${error} Bấm Làm mới đơn để thử lại.` : "";
        notice.hidden = !text;
        if (notice.textContent !== text) notice.textContent = text;
        document.querySelectorAll("[data-ordered-item]").forEach((node) => {
            const item = items.get(node.dataset.orderedItem);
            const ordered = item && timing.orderedForItem(item, history.rows);
            const known = ordered && timing.label(ordered) !== null;
            node.textContent = !session.loaded ? "Giờ gọi chưa cập nhật" : known
                ? `Gọi lúc ${new Date(timing.parseUtc(ordered)).toLocaleTimeString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh", hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23" })}`
                : history.state === "loading" ? "Đang lấy giờ gọi…" : history.state === "failed" ? "Chưa rõ giờ gọi riêng" : "Chưa có giờ gọi riêng";
            node.classList.toggle("is-unavailable", !session.loaded || !known);
            node.title = known ? `Giờ gọi dòng món theo máy chủ: ${new Date(timing.parseUtc(ordered)).toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" })} (giờ Việt Nam).`
                : "Chỉ dùng giờ gọi riêng được máy chủ lưu; không suy từ giờ tạo đơn, cập nhật hoặc tách dòng món.";
        });
        document.querySelectorAll("[data-waiting-item]").forEach((node) => {
            const item = items.get(node.dataset.waitingItem);
            const ordered = item && timing.orderedForItem(item, history.rows);
            const elapsed = item?.status === "Pending" && ordered && timing.label(ordered);
            node.textContent = !session.loaded ? "Thời gian chờ chưa cập nhật" : elapsed ? `Chờ bếp nhận · ${elapsed}`
                : history.state === "loading" ? "Đang lấy thời gian chờ…" : history.state === "failed" ? "Chưa rõ thời gian chờ" : "Chưa có giờ gọi riêng";
            node.classList.toggle("is-unavailable", !session.loaded || !elapsed);
            node.title = "Thời gian từ giờ gọi riêng của dòng món đến hiện tại, khi bếp chưa nhận món; không phải thời gian dự kiến hoàn thành.";
        });
        document.querySelectorAll("[data-preparation-item]").forEach((node) => {
            const item = items.get(node.dataset.preparationItem);
            const start = item && timing.startedForItem(item, history.rows);
            const elapsed = start && timing.label(start);
            node.textContent = !session.loaded ? "Thời gian chưa cập nhật" : elapsed ? `Đang làm · ${elapsed}`
                : history.state === "loading" ? "Đang lấy thời gian…"
                : history.state === "failed" ? "Chưa rõ thời gian" : "Chưa có mốc bắt đầu";
            node.classList.toggle("is-unavailable", !session.loaded || !elapsed);
            node.title = elapsed ? `Từ lúc bếp bắt đầu món: ${new Date(timing.parseUtc(start)).toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" })} (giờ Việt Nam). Thời gian đã làm, không phải dự kiến hoàn thành.`
                : "Chỉ tính từ mốc bếp bắt đầu món do máy chủ lưu; không tính từ lúc gọi món hoặc mở trang.";
        });
    }

    async function loadPreparationTimes() {
        const request = ++timingRequest, order = session.order;
        if (!session.loaded || !session.isOpen() || !order.items.length) {
            preparationHistory = { orderId: null, rows: [], state: "idle" }; renderPreparationTimes(); return;
        }
        const previous = preparationHistory.orderId === order.orderId ? preparationHistory.rows : [];
        preparationHistory = { orderId: order.orderId, rows: previous, state: "loading", error: preparationHistory.orderId === order.orderId ? preparationHistory.error : "" };
        renderPreparationTimes();
        try {
            // Lịch sử món cho phép Phục vụ đọc; lỗi phần thời gian không khóa thao tác POS.
            const rows = await api.get(`/orders/${order.orderId}/item-history`);
            if (!Array.isArray(rows)) throw new Error("Không đọc được lịch sử thời gian món.");
            if (request !== timingRequest || session.order?.orderId !== order.orderId) return;
            preparationHistory = { orderId: order.orderId, rows, state: "ready" };
        } catch (error) {
            if (request !== timingRequest || session.order?.orderId !== order.orderId) return;
            preparationHistory = { orderId: order.orderId, rows: [], state: "failed", error: error.message };
        }
        renderPreparationTimes();
    }

    function renderCategories() {
        $("#categoryCount").textContent = `${state.categories.length} mục`;
        $("#categoryList").innerHTML = [{ id: "all", name: "Tất cả" }, ...state.categories].map((category) => `<button type="button" class="category-button${state.category === category.id ? " is-active" : ""}" data-category-id="${escapeHtml(category.id)}"><strong>${escapeHtml(category.name)}</strong><small>${category.id === "all" ? state.foods.length : state.foods.filter((food) => food.categoryId === category.id).length}</small></button>`).join("");
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
        // Chữ đầu theo thứ tự tên món: bll = Bò lúc lắc, cchs = Cơm chiên hải sản.
        const initials = name.split(" ").map((word) => word[0] || "").join("");
        return terms.length === 1 && state.search.length >= 2 && initials.startsWith(state.search);
    }

    function renderFoods() {
        const foods = state.foods.filter((food) => (state.category === "all" || food.categoryId === state.category) && matchesFood(food));
        $("#foodCount").textContent = `${foods.length} món`;
        $("#foodGrid").innerHTML = foods.length ? foods.map((food) => {
            const priceSize = food.sizes.find((size) => size.isDefault) || food.sizes[0];
            const initials = food.foodName.trim().split(/\s+/).slice(0, 2).map((word) => word[0]).join("").toLocaleUpperCase("vi-VN");
            let imageUrl = "";
            try {
                const image = new URL(food.foodImageUrl, window.location.href);
                if (food.foodImageUrl && ["http:", "https:"].includes(image.protocol)) imageUrl = image.href;
            } catch { /* Món chưa có đường dẫn ảnh hợp lệ dùng chữ viết tắt. */ }
            return `<button class="food-card" type="button" data-food-id="${food.foodId}" ${locked() ? "disabled" : ""}>
            <span class="food-visual" aria-hidden="true"><svg viewBox="0 0 100 80" fill="none" stroke="currentColor" stroke-width="1.4"><circle cx="50" cy="40" r="28"/><circle cx="50" cy="40" r="21"/><path d="M10 14v16m5-16v16m5-16v16m-10-7h10m-5 7v36M85 14v52m0-52c-10 7-10 21 0 22"/></svg><span class="food-monogram">${escapeHtml(initials)}</span>${imageUrl ? `<img src="${escapeHtml(imageUrl)}" alt="" loading="lazy" data-food-image>` : ""}</span>
            <span class="food-card-content"><span class="food-card-top"><h4>${escapeHtml(food.foodName)}</h4></span><span class="food-description">${escapeHtml(food.foodDescription || "")}</span>
            <span class="food-meta"><span class="food-price">${money(priceSize.sizePrice)}</span><span>${escapeHtml(priceSize.sizeName)} · ${food.sizes.length} kích cỡ</span></span></span>
            <span class="food-card-add" aria-hidden="true">+</span></button>`;
        }).join("")
            : '<div class="empty-state"><strong>Chưa có món phù hợp</strong><span>Chỉ tìm món đang bán. Thử từ khác hoặc chọn Tất cả danh mục.</span></div>';
        $("#foodGrid").querySelectorAll("[data-food-image]").forEach((image) => image.addEventListener("error", () => { image.hidden = true; }));
    }

    async function loadMenu() {
        if (!hasTableContext()) return;
        $("#foodGrid").innerHTML = '<div class="empty-state"><strong>Đang tải thực đơn…</strong></div>';
        try {
            const menu = await api.get("/menu");
            state.categories = menu.map((category) => ({ id: String(category.categoryId), name: category.categoryName }));
            state.foods = menu.flatMap((category) => category.foods.filter((food) => food.sizes.length).map((food) => ({ ...food, categoryId: String(category.categoryId) })));
            renderCategories(); renderFoods(); renderCart();
        } catch (error) {
            $("#foodGrid").innerHTML = `<div class="empty-state"><strong>Không tải được thực đơn</strong><span>${escapeHtml(error.message)}</span></div>`;
        }
    }

    function renderCart() {
        $("#cartItemCount").textContent = `${state.cart.length} món`;
        $("#cartDockCount").textContent = `${state.cart.length} món chưa gửi`;
        $("#cartList").innerHTML = state.cart.length ? state.cart.map((item, index) => {
            const food = findFood(item.sizeId);
            const size = food?.sizes.find((entry) => entry.sizeId === item.sizeId);
            const name = food?.foodName || "Món từ yêu cầu trước";
            return `<article class="cart-item"><div class="cart-item-top"><div class="cart-item-name"><strong>${escapeHtml(name)}</strong><span class="cart-item-size">${escapeHtml(size?.sizeName || "Cỡ đã chọn")} · Đơn giá cỡ: ${money(size?.sizePrice)}</span></div></div>
                ${item.toppings.length ? `<p class="field-hint">Topping mỗi phần: ${item.toppings.map((t) => escapeHtml(food?.toppings.find((entry) => entry.toppingId === t.toppingId)?.toppingName || "Topping đã chọn")).join(", ")}</p>` : ""}
                <div class="quantity-control"><button class="quantity-button" type="button" data-cart-index="${index}" data-delta="-1" aria-label="Giảm ${escapeHtml(name)}">−</button><strong>${item.quantity} phần</strong><button class="quantity-button" type="button" data-cart-index="${index}" data-delta="1" aria-label="Tăng ${escapeHtml(name)}">+</button><button class="quantity-button cart-remove" type="button" data-cart-index="${index}" data-delta="-${item.quantity}" aria-label="Xóa ${escapeHtml(name)} khỏi giỏ">×</button></div>
                <input class="cart-item-note" maxlength="500" value="${escapeHtml(item.note)}" data-cart-note="${index}" aria-label="Ghi chú ${escapeHtml(name)}" placeholder="Ghi chú riêng cho món"></article>`;
        }).join("") : '<p class="cart-empty">Giỏ món chưa gửi đang trống.</p>';
        renderButtons();
    }

    function render() { renderTable(); renderSentItems(); renderButtons(); renderPreparationTimes(); }

    async function refresh(quiet = false) {
        if (session.busy || session.loading) return;
        if (!hasValidTableId()) { render(); return; }
        const previouslyLoaded = session.loaded;
        entryError = "";
        const task = session.refresh();
        renderButtons();
        try {
            await task;
            if (session.order) window.localStorage.setItem("restaurant.lastOrder", JSON.stringify(session.order));
            if (!quiet) message(previouslyLoaded ? "Đã tải trạng thái mới nhất từ máy chủ." : "");
            render();
            // Không chờ truy vấn phụ trước khi mở form hoặc hoàn tất refresh nghiệp vụ.
            loadPreparationTimes();
        } catch (error) {
            entryError = error.message;
            message(error.message, true);
            render();
        }
    }

    function openDish(foodId) {
        if (locked()) return;
        const food = state.foods.find((item) => item.foodId === foodId);
        if (!food) return;
        state.dish = food;
        $("#dishForm").reset();
        $("#sizeDialogTitle").textContent = food.foodName;
        $("#sizeDialogDescription").textContent = "Chọn cỡ, topping mỗi phần và số phần cần gọi.";
        const selected = food.sizes.find((size) => size.isDefault) || food.sizes[0];
        $("#sizeOptions").innerHTML = food.sizes.map((size) => `<label class="size-option"><input type="radio" name="sizeId" value="${size.sizeId}" ${size.sizeId === selected.sizeId ? "checked" : ""} required><strong>${escapeHtml(size.sizeName)}</strong><span>${money(size.sizePrice)}</span></label>`).join("");
        $("#toppingOptions").hidden = !food.toppings.length;
        $("#toppingChoices").innerHTML = food.toppings.map((topping) => `<label><input type="checkbox" name="toppingId" value="${topping.toppingId}"> ${escapeHtml(topping.toppingName)} · ${money(topping.toppingPrice)}</label>`).join("");
        $("#sizeDialogMessage").textContent = "";
        dishDialog.showModal();
        $("#dishQuantity").focus();
    }

    async function runMutation(action) {
        const task = action();
        renderButtons();
        try {
            const result = await task;
            if (["create", "add"].includes(result.kind)) { state.cart = []; $("#orderNoteInput").value = ""; }
            if (dialog.open) dialog.close();
            if (result.order?.note) $("#orderNoteInput").value = result.order.note;
            window.localStorage.setItem("restaurant.lastOrder", JSON.stringify(result.order));
            message(result.moved ? "Yêu cầu đã được xác nhận, nhưng đơn đã chuyển sang bàn khác. Hãy chọn lại bàn trước khi tiếp tục."
                : result.kind === "unchanged" ? "Số lượng không thay đổi."
                : result.kind === "create" || result.kind === "add" ? "Đã gửi món. Trạng thái và tổng tiền được tải lại từ máy chủ." : "Đã cập nhật đơn từ máy chủ.");
            renderCart(); render();
        } catch (error) {
            const originalMessage = error.message;
            if (!session.pending && [403, 404, 409].includes(error.status)) {
                await refresh(true);
                const mutation = state.mutation;
                if (dialog.open && mutation?.kind === "quantity") {
                    const latest = pos.groups(session.order).find((group) => group.key === mutation.group.key);
                    if (latest?.active.length) {
                        mutation.group = latest;
                        $("#mutationQuantity").min = session.canReduce(latest) ? "0" : String(latest.quantity);
                        $("#mutationDescription").textContent = `${latest.items[0].foodName} · hiện còn ${latest.quantity} phần. Trạng thái vừa được tải lại.`;
                        updateMutationHints();
                    }
                }
            }
            if (dialog.open) $("#mutationMessage").textContent = originalMessage;
            message(session.pending ? `${originalMessage} Yêu cầu chưa được xác nhận; hãy dùng nút thử xác nhận lại.` : originalMessage, true);
            render();
        }
    }

    async function openMutation(kind, value) {
        if (locked() || !session.order) return;
        await refresh(true);
        if (locked() || !session.isOpen()) return;
        const group = kind === "quantity" ? pos.groups(session.order).find((entry) => entry.key === value) : null;
        const item = kind === "cancelItem" ? session.order.items.find((entry) => entry.id === Number(value)) : null;
        if ((kind === "quantity" && !group?.active.length) || (kind === "cancelItem" && (!item || !session.canCancel(item)))) {
            message("Trạng thái món vừa thay đổi. Kiểm tra lại món; phần bếp đã nhận cần quản trị viên xử lý.", true);
            return;
        }
        if (kind === "cancelOrder" && !session.isAdmin()) return;
        state.mutation = { kind, group, item };
        $("#orderMutationForm").reset();
        $("#mutationMessage").textContent = "";
        $("#mutationQuantityField").hidden = kind !== "quantity";
        $("#mutationQuantity").required = kind === "quantity";
        $("#mutationReasonCodeField").hidden = !session.isAdmin() || kind === "quantity";
        $("#mutationTitle").textContent = kind === "quantity" ? "Đổi số lượng" : kind === "cancelItem" ? "Hủy món đã gửi" : "Hủy toàn bộ đơn";
        $("#mutationDescription").textContent = group ? `${group.items[0].foodName} · ${group.items[0].sizeName} · hiện có ${group.quantity} phần còn phục vụ trong lượt gọi này.`
            : item ? `${item.foodName} · ${item.sizeName} · ${item.quantity} phần · ${statusLabel(item.status)}` : `Đơn #${session.order.orderId}. Toàn bộ món còn phục vụ sẽ bị hủy.`;
        if (group) {
            $("#mutationQuantity").value = group.quantity;
            $("#mutationQuantity").min = session.canReduce(group) ? "0" : String(group.quantity);
        }
        updateMutationHints(); renderButtons();
        dialog.showModal();
        (kind === "quantity" ? $("#mutationQuantity") : $("#mutationReason")).focus();
    }

    function updateMutationHints() {
        const mutation = state.mutation;
        if (!mutation) return;
        const isQuantity = mutation.kind === "quantity";
        const reducing = isQuantity && Number($("#mutationQuantity").value) < mutation.group.quantity;
        const prepared = mutation.group?.active.filter((item) => ["Ready", "Completed"].includes(item.status)).reduce((sum, item) => sum + item.quantity, 0) || 0;
        $("#mutationWastageField").hidden = !isQuantity || !session.isAdmin() || !reducing || Number($("#mutationQuantity").value) >= prepared;
        $("#mutationReason").required = !isQuantity || Number($("#mutationQuantity").value) === 0;
        $("#mutationStockHint").textContent = isQuantity && !session.canReduce(mutation.group) ? "Bếp đã nhận món. Bạn chỉ được tăng số phần; giảm/hủy cần quản trị viên."
            : $("#mutationReasonCode").value !== "CustomerChange" ? "Phần bị hủy do hết/thiếu/hỏng nguyên liệu hoặc sự cố sẽ ghi hao hụt, không hoàn kho."
            : "Máy chủ quyết định hoàn kho cho phần chưa nấu; phần đã bắt đầu nấu bị hủy không hoàn kho.";
    }

    $("#categoryList").addEventListener("click", (event) => {
        const button = event.target.closest("[data-category-id]");
        if (button) { state.category = button.dataset.categoryId; renderCategories(); renderFoods(); }
    });
    $("#foodSearchInput").addEventListener("input", (event) => { state.search = normalizeSearch(event.target.value); renderFoods(); });
    $("#foodGrid").addEventListener("click", (event) => { const button = event.target.closest("[data-food-id]"); if (button) openDish(Number(button.dataset.foodId)); });
    $("#dishForm").addEventListener("submit", (event) => {
        event.preventDefault();
        if (locked() || !state.dish || !event.target.reportValidity()) return;
        const quantity = Number($("#dishQuantity").value);
        if (!Number.isInteger(quantity) || quantity < 1) { $("#sizeDialogMessage").textContent = "Số phần phải là số nguyên dương."; return; }
        const data = new FormData(event.target);
        const sizeId = Number(data.get("sizeId"));
        const toppings = data.getAll("toppingId").map((id) => ({ toppingId: Number(id), quantity: 1 })).sort((a, b) => a.toppingId - b.toppingId);
        const existing = state.cart.find((item) => item.sizeId === sizeId && JSON.stringify(item.toppings) === JSON.stringify(toppings) && !item.note);
        if (existing && existing.quantity + quantity > 2147483647) { $("#sizeDialogMessage").textContent = "Số phần vượt quá giới hạn."; return; }
        if (existing) existing.quantity += quantity;
        else state.cart.push({ sizeId, quantity, note: "", toppings });
        dishDialog.close(); renderCart();
    });
    [$("#cancelSizeButton"), $("#closeSizeDialog")].forEach((button) => button.addEventListener("click", () => dishDialog.close()));
    $("#cartList").addEventListener("click", (event) => {
        const button = event.target.closest("[data-cart-index]");
        if (!button || locked()) return;
        const index = Number(button.dataset.cartIndex);
        const item = state.cart[index];
        const next = item.quantity + Number(button.dataset.delta);
        if (next > 2147483647) return;
        if (next <= 0) state.cart.splice(index, 1); else item.quantity = next;
        renderCart();
    });
    $("#cartList").addEventListener("input", (event) => {
        if (event.target.matches("[data-cart-note]") && !locked()) state.cart[Number(event.target.dataset.cartNote)].note = event.target.value;
    });
    $("#clearOrderButton").addEventListener("click", () => { if (session.busy || session.pending) return; state.cart = []; renderCart(); });
    function send() {
        if (locked() || !state.cart.length) return;
        runMutation(() => session.send(state.cart.map((item) => ({ ...item, toppings: item.toppings.map((t) => ({ ...t })) })), $("#orderNoteInput").value.trim()));
    }
    [$("#sendKitchenButton"), $("#cartSendButton")].forEach((button) => button.addEventListener("click", send));
    $("#retryOrderButton").addEventListener("click", () => runMutation(() => session.resume()));
    $("#refreshOrderButton").addEventListener("click", async () => { await refresh(); await loadMenu(); });
    $("#orderEntryRetry").addEventListener("click", async () => { await refresh(); await loadMenu(); });
    $("#chooseTableButton").addEventListener("click", () => { window.location.href = "tables.html?ui=20261112-print1"; });
    $("#cartDock").addEventListener("click", () => { $(".cart-panel").classList.add("is-open"); $("#closeCartButton").focus(); });
    $("#closeCartButton").addEventListener("click", () => { $(".cart-panel").classList.remove("is-open"); $("#cartDock").focus(); });
    $("#sentOrderItems").addEventListener("click", (event) => {
        const edit = event.target.closest("[data-edit-group]");
        const cancel = event.target.closest("[data-cancel-item]");
        if (edit) openMutation("quantity", edit.dataset.editGroup);
        if (cancel) openMutation("cancelItem", cancel.dataset.cancelItem);
    });
    $("#cancelWholeOrderButton").addEventListener("click", () => openMutation("cancelOrder"));
    $("#mutationQuantity").addEventListener("input", updateMutationHints);
    $("#mutationReasonCode").addEventListener("change", updateMutationHints);
    document.querySelectorAll("[data-close-mutation]").forEach((button) => button.addEventListener("click", () => { if (!session.busy) dialog.close(); }));
    dialog.addEventListener("cancel", (event) => { if (session.busy) event.preventDefault(); });
    $("#orderMutationForm").addEventListener("submit", (event) => {
        event.preventDefault();
        if (locked() || !state.mutation || !event.target.reportValidity()) return;
        const { kind, group, item } = state.mutation;
        const reason = $("#mutationReason").value.trim();
        const reasonCode = $("#mutationReasonCode").value;
        runMutation(() => kind === "quantity" ? session.changeQuantity(group.key, Number($("#mutationQuantity").value), reason, $("#mutationWastage").checked)
            : kind === "cancelItem" ? session.cancelItem(item.id, reason, reasonCode) : session.cancelOrder(reason, reasonCode));
    });
    $("#orderHistoryButton").addEventListener("click", async () => {
        if (!session.order) return;
        $("#orderHistoryContent").textContent = "Đang tải lịch sử…";
        $("#orderHistoryDialog").showModal();
        try {
            const id = session.order.orderId;
            const [orders, items] = await Promise.all([api.get(`/orders/${id}/history`), api.get(`/orders/${id}/item-history`)]);
            const rows = [...orders.map((entry) => ({ ...entry, type: "Đơn", detail: entry.note })), ...items.map((entry) => ({ ...entry, type: `Dòng món #${entry.orderItemId ?? "—"}`, detail: entry.reason }))].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
            $("#orderHistoryContent").innerHTML = rows.map((entry) => {
                const utc = /(?:Z|[+-]\d\d:\d\d)$/i.test(entry.createdAt) ? entry.createdAt : `${entry.createdAt}Z`;
                return `<article class="pos-history-entry"><strong>${escapeHtml(entry.type)} · ${escapeHtml(statusLabel(entry.oldStatus))} → ${escapeHtml(statusLabel(entry.newStatus))}</strong><p>${escapeHtml(entry.changedByName || "Nhân viên")} · ${escapeHtml(new Date(utc).toLocaleString("vi-VN"))}</p><p>${escapeHtml(entry.detail || "")}${entry.reasonCode ? ` (${escapeHtml(entry.reasonCode)})` : ""}</p></article>`;
            }).join("") || "Chưa có lịch sử.";
        } catch (error) { $("#orderHistoryContent").textContent = error.message; }
    });
    $("#closeOrderHistory").addEventListener("click", () => $("#orderHistoryDialog").close());
    $("#orderToolsButton").addEventListener("click", () => { if (session.order && !session.pending) location.href = `order-tools.html?orderId=${session.order.orderId}&ui=20261112-print1`; });
    window.addEventListener("restaurant:auth-changed", render);
    if (session.pending && ["create", "add"].includes(session.pending.kind) && session.pending.tableId === session.tableId) {
        state.cart = session.pending.body.items.map((item) => ({ ...item }));
        $("#orderNoteInput").value = session.pending.body.note || "";
    }
    renderCart(); render();
    Promise.resolve(auth.ready).then(async () => { await refresh(); await loadMenu(); });
    // Không cắt ngang nhập liệu/hộp thoại; trước khi mở lệnh sửa cũng tải lại đơn.
    let poll, preparationClock;
    function startPolling() {
        window.clearInterval(poll);
        window.clearInterval(preparationClock);
        poll = window.setInterval(() => {
            if (!document.hidden && session.isOpen() && !dialog.open && !dishDialog.open && !$("#orderHistoryDialog").open
                && !document.activeElement?.closest("input, textarea, select")) refresh(true);
        }, 5000);
        preparationClock = window.setInterval(() => { if (!document.hidden) renderPreparationTimes(); }, 1000);
    }
    startPolling();
    window.addEventListener("pageshow", (event) => { startPolling(); if (event.persisted) refresh(true); });
    window.addEventListener("pagehide", () => { window.clearInterval(poll); window.clearInterval(preparationClock); });
})();
