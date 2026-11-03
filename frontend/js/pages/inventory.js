(function () {
    const { WarehouseSession, stockSnapshot } = window.restaurantWarehouse;
    const session = new WarehouseSession({ api: window.restaurantApi, auth: window.restaurantAuth, storage: window.sessionStorage, mode: "inventory" });
    const ui = window.restaurantWarehouseUi.createPage(session, render);
    const { $, esc, quantity, time, locked } = ui;
    const statuses = { Available: "Bình thường", OutOfStock: "Đánh dấu hết hàng", Spoiled: "Hỏng / hết hạn" };
    const types = { Sale: "Xuất cho đơn", Purchase: "Nhận hàng", AdjustmentIn: "Điều chỉnh tăng", AdjustmentOut: "Điều chỉnh giảm", Wastage: "Hao hụt" };
    const references = { Order: "Đơn hàng", PurchaseOrder: "Phiếu nhập", ManualAdjustment: "Điều chỉnh tay" };
    let adjustment = null, ingredientOptions = "";
    function render() {
        const term = $("inventorySearch").value.trim().toLocaleLowerCase("vi"), view = $("inventoryView").value;
        const low = new Map(session.lowStock.map((i) => [i.ingredientId, i]));
        const rows = session.inventory.map((stock) => ({ stock, ingredient: session.ingredients.find((i) => i.ingredientId === stock.ingredientId), alert: low.get(stock.ingredientId) }))
            .filter(({ stock, ingredient, alert }) => `${ingredient?.name || ""} #${stock.ingredientId}`.toLocaleLowerCase("vi").includes(term)
                && (view === "all" || (view === "low" ? Boolean(alert) : alert?.stockStatus === view)));
        $("inventoryCount").textContent = `${rows.length} nguyên liệu`;
        $("inventorySummary").innerHTML = [["Dòng tồn kho", session.inventory.length], ["Cần kho xử lý", session.lowStock.length],
            ["Báo hỏng", session.lowStock.filter((i) => i.stockStatus === "Spoiled").length], ["Hết theo sổ", session.lowStock.filter((i) => i.isOutOfStock).length]]
            .map(([label, value]) => `<article class="summary-tile"><span>${label}</span><strong>${value}</strong></article>`).join("");
        $("inventoryList").innerHTML = rows.length ? rows.map(({ stock, ingredient, alert }) => `<article class="warehouse-card${alert ? " needs-attention" : ""}">
            <div><strong>${esc(ingredient?.name || `Nguyên liệu #${stock.ingredientId}`)}</strong><span>#${stock.ingredientId} · ${esc(ingredient?.unit || "—")} · ${esc(statuses[ingredient?.stockStatus] || ingredient?.stockStatus || "—")}${ingredient?.isActive === false ? " · Ngừng sử dụng" : ""}</span>
            <span>Cập nhật: ${time(stock.updatedAt)}</span></div>
            <div><span>Tồn ghi sổ</span><strong>${quantity(stock.quanlity)} ${esc(ingredient?.unit)}</strong><span>Tối thiểu: ${quantity(ingredient?.minimumStock)}</span>
            ${alert ? `<span>${alert.shortage > 0 ? `Thiếu theo ngưỡng: ${quantity(alert.shortage)} ${esc(alert.unit)}` : "Cần kiểm tra trạng thái thực tế"}</span>` : ""}</div>
            <div class="warehouse-actions"><button class="secondary-action" type="button" data-adjust="${stock.ingredientId}"${locked() || !ingredient ? " disabled" : ""}>Điều chỉnh</button><button class="secondary-action" type="button" data-history="${stock.ingredientId}"${session.loading || session.busy ? " disabled" : ""}>Lịch sử</button></div>
            </article>`).join("") : '<p class="warehouse-empty">Không có nguyên liệu phù hợp.</p>';
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
            $("ledgerFilters").reset();
            $("ledgerIngredient").value = history.dataset.history;
            ui.run(async () => { session.loading = true; try { await session.loadLedger(Object.fromEntries(new FormData($("ledgerFilters"))), 1); } finally { session.loading = false; } });
            $("ledgerSection").scrollIntoView({ behavior: "smooth", block: "start" });
        }
    });
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
        await session.refresh();
        ui.render();
        for (const [key, value] of Object.entries(session.ledgerFilters)) $("ledgerFilters").elements.namedItem(key).value = value;
    }));
})();
