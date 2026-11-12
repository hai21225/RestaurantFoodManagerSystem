(function () {
    const { WarehouseSession, stockSnapshot } = window.restaurantWarehouse;
    const session = new WarehouseSession({ api: window.restaurantApi, auth: window.restaurantAuth, storage: window.sessionStorage, mode: "inventory" });
    const ui = window.restaurantWarehouseUi.createPage(session, render);
    const { $, esc, quantity, time, locked } = ui;
    const statuses = { Available: "Bình thường", OutOfStock: "Đánh dấu hết hàng", Spoiled: "Hỏng / hết hạn" };
    const types = { Sale: "Xuất cho đơn", Purchase: "Nhận hàng", AdjustmentIn: "Điều chỉnh tăng", AdjustmentOut: "Điều chỉnh giảm", Wastage: "Hao hụt" };
    const references = { Order: "Đơn hàng", PurchaseOrder: "Phiếu nhập", ManualAdjustment: "Điều chỉnh tay" };
    let adjustment = null, ingredientOptions = "", selectedStockId = null;
    function setWorkspace(view) {
        const history = view === "ledger";
        $("stockWorkspace").hidden = history; $("ledgerSection").hidden = !history;
        for (const [id, active] of [["stockTab", !history], ["ledgerTab", history]]) {
            $(id).setAttribute("aria-selected", String(active)); $(id).tabIndex = active ? 0 : -1;
        }
        if (history) $("stockDetailPanel").classList.remove("is-open");
    }
    function renderStockDetail(row) {
        const target = $("stockDetail");
        if (!row) { target.innerHTML = '<p class="warehouse-empty">Chọn một nguyên liệu để xem lượng tồn và thao tác.</p>'; return; }
        const { stock, ingredient, alert } = row;
        target.innerHTML = `<div class="stock-detail-identity"><span class="warehouse-record-code">NGUYÊN LIỆU #${stock.ingredientId}</span><h4>${esc(ingredient?.name || `Nguyên liệu #${stock.ingredientId}`)}</h4>
            <span class="stock-state${alert ? " is-warning" : ""}">${esc(statuses[ingredient?.stockStatus] || ingredient?.stockStatus || "—")}${ingredient?.isActive === false ? " · Ngừng sử dụng" : ""}</span></div>
            <div class="stock-detail-quantity"><span>Tồn ghi sổ</span><strong>${quantity(stock.quanlity)} <small>${esc(ingredient?.unit || "—")}</small></strong></div>
            <dl class="stock-detail-facts"><div><dt>Tồn tối thiểu</dt><dd>${quantity(ingredient?.minimumStock)} ${esc(ingredient?.unit || "")}</dd></div><div><dt>Cập nhật</dt><dd>${time(stock.updatedAt)}</dd></div></dl>
            ${alert ? `<p class="stock-detail-alert">${alert.shortage > 0 ? `Thiếu theo ngưỡng: ${quantity(alert.shortage)} ${esc(alert.unit)}` : "Cần kiểm tra tình trạng nguyên liệu thực tế."}</p>` : ""}
            <div class="stock-detail-actions"><button class="primary-action" type="button" data-adjust="${stock.ingredientId}"${locked() || !ingredient ? " disabled" : ""}>Điều chỉnh tồn</button><button class="secondary-action" type="button" data-history="${stock.ingredientId}"${session.loading || session.busy ? " disabled" : ""}>Xem lịch sử nguyên liệu</button></div>
            <p class="warehouse-hint">Lượng tồn là số ghi sổ. Chỉ điều chỉnh sau khi kiểm tra thực tế; mỗi thay đổi cần ghi lý do.</p>`;
    }
    function render() {
        const term = $("inventorySearch").value.trim().toLocaleLowerCase("vi"), view = $("inventoryView").value;
        const low = new Map(session.lowStock.map((i) => [i.ingredientId, i]));
        const rows = session.inventory.map((stock) => ({ stock, ingredient: session.ingredients.find((i) => i.ingredientId === stock.ingredientId), alert: low.get(stock.ingredientId) }))
            .filter(({ stock, ingredient, alert }) => `${ingredient?.name || ""} #${stock.ingredientId}`.toLocaleLowerCase("vi").includes(term)
                && (view === "all" || (view === "low" ? Boolean(alert) : alert?.stockStatus === view)));
        $("inventoryCount").textContent = `${rows.length} nguyên liệu`;
        $("inventorySummary").innerHTML = [["Dòng tồn kho", session.inventory.length], ["Cần kho xử lý", session.lowStock.length],
            ["Báo hỏng", session.lowStock.filter((i) => i.stockStatus === "Spoiled").length], ["Hết theo sổ", session.lowStock.filter((i) => i.isOutOfStock).length]]
            .map(([label, value]) => `<span><strong>${value}</strong> ${label.toLocaleLowerCase("vi")}</span>`).join("");
        if (!rows.some(({ stock }) => stock.ingredientId === selectedStockId)) selectedStockId = rows[0]?.stock.ingredientId || null;
        $("inventoryList").innerHTML = rows.length ? rows.map(({ stock, ingredient, alert }) => `<button type="button" class="inventory-stock-row${selectedStockId === stock.ingredientId ? " is-selected" : ""}" data-stock="${stock.ingredientId}" aria-pressed="${selectedStockId === stock.ingredientId}">
            <span class="stock-row-name"><strong>${esc(ingredient?.name || `Nguyên liệu #${stock.ingredientId}`)}</strong><small>#${stock.ingredientId}${ingredient?.isActive === false ? " · Ngừng sử dụng" : ""}</small></span>
            <span class="stock-state${alert ? " is-warning" : ""}">${esc(statuses[ingredient?.stockStatus] || ingredient?.stockStatus || "—")}</span>
            <strong class="stock-row-quantity">${quantity(stock.quanlity)}</strong><span class="stock-row-unit">${esc(ingredient?.unit || "—")}</span><span class="warehouse-row-arrow" aria-hidden="true">›</span>
            </button>`).join("") : '<p class="warehouse-empty">Không có nguyên liệu phù hợp.</p>';
        renderStockDetail(rows.find(({ stock }) => stock.ingredientId === selectedStockId));
        const options = '<option value="">Tất cả</option>' + session.ingredients.map((i) => `<option value="${i.ingredientId}">${esc(i.name)} (#${i.ingredientId})</option>`).join("");
        if (options !== ingredientOptions) { const selected = $("ledgerIngredient").value; $("ledgerIngredient").innerHTML = options; $("ledgerIngredient").value = selected; ingredientOptions = options; }
        $("ledgerRows").innerHTML = session.ledger.items.map((row) => `<tr><td>${time(row.createdAt)}</td><td>${esc(row.ingredientName || `#${row.ingredientId}`)}</td><td>${esc(types[row.type] || row.type)}</td>
            <td>${quantity(row.quanlity)} ${esc(row.unit)}</td><td>${quantity(row.quanlityBefore)} → ${quantity(row.quanlityAfter)} ${esc(row.unit)}</td>
            <td>${esc(references[row.referenceType] || row.referenceType || "—")}${row.referenceId ? ` #${row.referenceId}` : ""}<br>${esc(row.note)}</td><td>#${row.createdBy}</td></tr>`).join("") || '<tr><td colspan="7">Không có biến động theo bộ lọc.</td></tr>';
        $("ledgerPage").textContent = `Trang ${session.ledger.page} · ${session.ledger.total} biến động`;
        $("ledgerPrev").disabled = session.loading || session.busy || session.ledger.page <= 1;
        $("ledgerNext").disabled = session.loading || session.busy || session.ledger.page * session.ledger.pageSize >= session.ledger.total;
        $("adjustSubmit").disabled = locked();
    }
    $("inventorySearch").addEventListener("input", render); $("inventoryView").addEventListener("change", render);
    $("inventoryList").addEventListener("click", (event) => {
        const row = event.target.closest("[data-stock]");
        if (!row) return;
        selectedStockId = Number(row.dataset.stock); render(); $("stockDetailPanel").classList.add("is-open");
        if (window.matchMedia?.("(max-width: 1000px)").matches) $("closeStockDetail").focus();
    });
    $("closeStockDetail").addEventListener("click", () => {
        $("stockDetailPanel").classList.remove("is-open");
        $("inventoryList").querySelector(`[data-stock="${selectedStockId}"]`)?.focus();
    });
    $("stockDetailPanel").addEventListener("keydown", (event) => { if (event.key === "Escape") $("closeStockDetail").click(); });
    $("stockDetail").addEventListener("click", (event) => {
        const button = event.target.closest("[data-adjust]");
        if (button && !locked()) {
            const id = Number(button.dataset.adjust), stock = session.inventory.find((i) => i.ingredientId === id), ingredient = session.ingredients.find((i) => i.ingredientId === id);
            adjustment = { id, expected: stockSnapshot(stock, ingredient) };
            $("adjustForm").reset(); $("adjustMessage").textContent = "";
            $("adjustCurrent").textContent = `${ingredient.name}: tồn ghi sổ ${quantity(stock.quanlity)} ${ingredient.unit}. Nhập phần tăng (+) hoặc giảm (−); số này sẽ cộng vào tồn hiện tại.`;
            $("adjustDialog").showModal(); $("adjustQuantity").focus();
        }
        const history = event.target.closest("[data-history]");
        if (history) {
            setWorkspace("ledger");
            $("ledgerFilters").reset();
            $("ledgerIngredient").value = history.dataset.history;
            ui.run(async () => { session.loading = true; try { await session.loadLedger(Object.fromEntries(new FormData($("ledgerFilters"))), 1); } finally { session.loading = false; } });
            $("ledgerTab").focus();
        }
    });
    for (const [id, view] of [["stockTab", "stock"], ["ledgerTab", "ledger"]]) {
        $(id).addEventListener("click", () => setWorkspace(view));
        $(id).addEventListener("keydown", (event) => {
            if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
            event.preventDefault();
            const next = event.key === "Home" ? "stock" : event.key === "End" ? "ledger" : view === "stock" ? "ledger" : "stock";
            setWorkspace(next); $(next === "stock" ? "stockTab" : "ledgerTab").focus();
        });
    }
    $("adjustForm").addEventListener("submit", (event) => {
        event.preventDefault(); if (locked() || !adjustment) return;
        ui.run(async () => {
            await session.command("adjust", adjustment.id, { quantity: $("adjustQuantity").value, note: $("adjustNote").value }, adjustment.expected);
            $("adjustDialog").close(); ui.message("Đã điều chỉnh tồn và tải lại lịch sử kho.");
        }, "", "adjustMessage");
    });
    $("closeAdjust").addEventListener("click", () => { if (!session.busy) $("adjustDialog").close(); });
    $("adjustDialog").addEventListener("cancel", (event) => { if (session.busy) event.preventDefault(); });
    $("ledgerFilters").addEventListener("submit", (event) => {
        event.preventDefault(); if (session.busy || session.loading) return;
        ui.run(async () => { session.loading = true; try { await session.loadLedger(Object.fromEntries(new FormData(event.currentTarget)), 1); } finally { session.loading = false; } });
    });
    for (const [id, delta] of [["ledgerPrev", -1], ["ledgerNext", 1]]) $(id).addEventListener("click", () => ui.run(async () => {
        session.loading = true; try { await session.loadLedger(session.ledgerFilters, session.ledger.page + delta); } finally { session.loading = false; }
    }));
    window.restaurantAuth.ready.then(() => ui.run(async () => {
        session.assertAccess();
        const params = new URLSearchParams(window.location.search);
        for (const key of ["ingredientId", "referenceType", "referenceId"]) if (params.get(key)) session.ledgerFilters[key] = params.get(key);
        if (["ingredientId", "referenceType", "referenceId"].some((key) => params.get(key)) || params.get("tab") === "history") setWorkspace("ledger");
        await session.refresh();
        ui.render();
        for (const [key, value] of Object.entries(session.ledgerFilters)) $("ledgerFilters").elements.namedItem(key).value = value;
    }));
})();
