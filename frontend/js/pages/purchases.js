(function () {
    const { WarehouseSession, supplierSnapshot, purchaseSnapshot } = window.restaurantWarehouse;
    const session = new WarehouseSession({ api: window.restaurantApi, auth: window.restaurantAuth, storage: window.sessionStorage, mode: "purchases" });
    const ui = window.restaurantWarehouseUi.createPage(session, render);
    const { $, esc, quantity, money, time, locked } = ui;
    const labels = { Draft: "Nháp", Received: "Đã nhận hàng", Cancelled: "Đã hủy" };
    let supplierEdit = null, confirmation = null, optionsReady = false;
    function setWorkspace(view) {
        const suppliers = view === "suppliers";
        $("purchasesWorkspace").hidden = suppliers; $("suppliersWorkspace").hidden = !suppliers;
        for (const [id, active] of [["purchasesTab", !suppliers], ["suppliersTab", suppliers]]) {
            $(id).setAttribute("aria-selected", String(active)); $(id).tabIndex = active ? 0 : -1;
        }
        if (suppliers) $("purchaseDetailPanel").classList.remove("is-open");
    }
    function monthOf(value) {
        if (!value) return "";
        const date = new Date(/[zZ]$|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
        if (!Number.isFinite(date.getTime())) return "";
        const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Ho_Chi_Minh", year: "numeric", month: "2-digit" }).formatToParts(date);
        return `${parts.find((p) => p.type === "year").value}-${parts.find((p) => p.type === "month").value}`;
    }
    const supplierName = (id) => session.suppliers.find((s) => s.supplierId === id)?.name || `Nhà cung cấp #${id}`;
    function render() {
        const term = $("purchaseSearch").value.trim().toLocaleLowerCase("vi"), status = $("purchaseStatus").value, month = $("purchaseMonth").value;
        const purchases = session.purchases.filter((p) => `#${p.id} ${supplierName(p.supplierId)}`.toLocaleLowerCase("vi").includes(term) && (!status || p.status === status) && (!month || monthOf(p.orderedAt) === month));
        $("purchaseCount").textContent = `${purchases.length} phiếu`;
        $("purchaseSummary").innerHTML = [["Phiếu nháp", session.purchases.filter((p) => p.status === "Draft").length], ["Đã nhận hàng", session.purchases.filter((p) => p.status === "Received").length],
            ["Đã hủy", session.purchases.filter((p) => p.status === "Cancelled").length], ["NCC đang hợp tác", session.suppliers.filter((s) => s.isActive).length]]
            .map(([label, value]) => `<span><strong>${value}</strong> ${label.toLocaleLowerCase("vi")}</span>`).join("");
        $("purchaseList").className = "purchase-register";
        $("purchaseList").innerHTML = purchases.length ? purchases.map((p) => `<button class="purchase-register-row${session.purchase?.id === p.id ? " is-selected" : ""}" type="button" data-purchase="${p.id}" aria-pressed="${session.purchase?.id === p.id}"${locked() ? " disabled" : ""}>
            <span class="purchase-row-identity"><strong>Phiếu #${p.id}</strong><span>${esc(supplierName(p.supplierId))}</span><small>${time(p.orderedAt)}</small></span><span class="purchase-state state-${esc(p.status)}">${esc(labels[p.status] || p.status)}</span><strong class="purchase-row-total">${money(p.totalAmount)}</strong><span class="warehouse-row-arrow" aria-hidden="true">›</span></button>`).join("") : '<p class="warehouse-empty">Không có phiếu nhập phù hợp.</p>';
        const p = session.purchase;
        $("purchaseDetail").innerHTML = p ? `<div class="warehouse-detail-heading"><strong>Phiếu #${p.id} · ${esc(labels[p.status] || p.status)}</strong><span>${esc(p.supplierName)} · Người tạo #${p.createdBy}</span><span>Tạo: ${time(p.orderedAt)} · Nhận: ${time(p.receivedAt)}</span></div>
            <div class="warehouse-table-wrap"><table class="warehouse-table"><thead><tr><th>Nguyên liệu</th><th>Số lượng</th><th>Đơn giá</th><th>Thành tiền</th></tr></thead><tbody>${p.items.map((i) => `<tr><td>${esc(i.ingredientName)}</td><td>${quantity(i.quantity)} ${esc(i.unit)}</td><td>${money(i.unitPrice)}</td><td>${money(i.totalPrice)}</td></tr>`).join("")}</tbody></table></div>
            <p class="warehouse-total">Tổng phiếu: <strong>${money(p.totalAmount)}</strong></p>
            <div class="warehouse-actions"><button class="primary-action" type="button" data-receive="${p.id}"${locked() || p.status !== "Draft" ? " disabled" : ""}>Xác nhận nhận hàng</button><button class="secondary-action" type="button" data-cancel-purchase="${p.id}"${locked() || p.status !== "Draft" ? " disabled" : ""}>Hủy phiếu</button></div>
            ${p.status === "Received" ? `<p><a href="inventory.html?referenceType=PurchaseOrder&amp;referenceId=${p.id}">Xem biến động kho của phiếu #${p.id}</a></p>` : `<p class="warehouse-hint">${p.status === "Draft" ? "Phiếu nháp chưa cộng kho. Chỉ xác nhận nhận hàng khi đã kiểm tra đủ hàng thực tế." : p.status === "Cancelled" ? "Phiếu đã hủy, không cộng tồn kho." : "Trạng thái phiếu chưa được hỗ trợ thao tác tại màn hình này."}</p>`}`
            : `<p class="warehouse-empty">${session.loading ? "Đang tải chi tiết phiếu…" : "Chọn một phiếu để xem dòng hàng và xử lý."}</p>`;
        const supplierTerm = $("supplierSearch").value.trim().toLocaleLowerCase("vi"), active = $("supplierStatus").value;
        const suppliers = session.suppliers.filter((s) => `${s.name} ${s.phone} ${s.email}`.toLocaleLowerCase("vi").includes(supplierTerm) && (!active || s.isActive === (active === "active")));
        $("supplierList").innerHTML = suppliers.length ? suppliers.map((s) => `<article class="warehouse-card">
            <div><strong>${esc(s.name)} · #${s.supplierId}</strong><span>${esc(s.phone)} · ${esc(s.email)}</span><span>${esc(s.address)}</span><span>${s.isActive ? "Đang hoạt động" : "Ngừng hợp tác"}</span></div>
            <div class="warehouse-actions"><button class="secondary-action" type="button" data-edit-supplier="${s.supplierId}"${locked() ? " disabled" : ""}>Sửa</button>
            <button class="secondary-action" type="button" ${s.isActive ? "data-delete-supplier" : "data-restore-supplier"}="${s.supplierId}"${locked() ? " disabled" : ""}>${s.isActive ? "Ngừng hợp tác" : "Khôi phục hợp tác"}</button></div></article>`).join("") : '<p class="warehouse-empty">Không có nhà cung cấp phù hợp.</p>';
        for (const id of ["addPurchaseButton", "manageSupplierAdd", "supplierSubmit"]) $(id).disabled = locked();
        $("purchaseSubmit").disabled = locked() || !optionsReady || !$("purchaseSupplier").value || !session.ingredients.some((i) => i.isActive);
        $("addPurchaseItemButton").disabled = locked() || !optionsReady || !session.ingredients.some((i) => i.isActive);
        $("addSupplierButton").disabled = locked() || !optionsReady;
    }
    function supplierOptions(preferred) {
        const value = String(preferred || $("purchaseSupplier").value || "");
        const suppliers = session.suppliers.filter((s) => s.isActive);
        $("purchaseSupplier").innerHTML = '<option value="">Chọn nhà cung cấp</option>' + suppliers.map((s) => `<option value="${s.supplierId}">${esc(s.name)}</option>`).join("");
        $("purchaseSupplier").value = suppliers.some((s) => String(s.supplierId) === value) ? value : suppliers.length === 1 ? String(suppliers[0].supplierId) : "";
    }
    function addItemRow() {
        const ingredients = session.ingredients.filter((i) => i.isActive);
        if (!ingredients.length) return;
        const row = document.createElement("div"); row.className = "purchase-item-row";
        row.innerHTML = `<label class="form-field purchase-ingredient-field"><span>Nguyên liệu</span><select data-ingredient required><option value="">Chọn nguyên liệu</option>${ingredients.map((i) => `<option value="${i.ingredientId}">${esc(i.name)}</option>`).join("")}</select><small data-unit></small></label>
            <label class="form-field purchase-quantity-field"><span>Số lượng</span><input data-quantity type="number" min="0.001" step="0.001" inputmode="decimal" required></label>
            <label class="form-field purchase-price-field"><span>Đơn giá</span><input data-price type="number" min="0" step="0.01" inputmode="decimal" required></label>
            <button class="purchase-remove-line" type="button" aria-label="Xóa dòng">×</button>`;
        row.querySelector("[data-ingredient]").addEventListener("change", (event) => { row.querySelector("[data-unit]").textContent = ingredients.find((i) => String(i.ingredientId) === event.target.value)?.unit || ""; });
        row.querySelector(".purchase-remove-line").addEventListener("click", () => { if (!session.busy) { row.remove(); if (!$("purchaseItemRows").children.length) addItemRow(); } });
        $("purchaseItemRows").appendChild(row);
    }
    function openPurchase() {
        if (locked() || $("purchaseDialog").open) return;
        optionsReady = false; $("purchaseForm").reset(); $("purchaseItemRows").replaceChildren(); $("purchaseSupplier").innerHTML = '<option value="">Đang tải…</option>';
        ui.message("Đang tải lựa chọn mới nhất…", false, "purchaseFormMessage"); $("purchaseDialog").showModal();
        ui.run(async () => {
            session.loading = true;
            try {
                await session.loadPurchases();
                if (!$("purchaseDialog").open) return;
                supplierOptions(); addItemRow(); optionsReady = true;
                $("purchaseIngredientLink").hidden = session.ingredients.some((i) => i.isActive);
                ui.message(!session.ingredients.some((i) => i.isActive) ? "Chưa có nguyên liệu đang hoạt động để nhập." : !session.suppliers.some((s) => s.isActive) ? "Thêm nhà cung cấp trước khi tạo phiếu." : "", false, "purchaseFormMessage");
            } finally { session.loading = false; }
        }, "", "purchaseFormMessage");
    }
    function openSupplier(supplier = null) {
        if (locked()) return;
        supplierEdit = supplier ? { id: supplier.supplierId, expected: supplierSnapshot(supplier) } : null;
        $("supplierForm").reset(); ui.message("", false, "supplierFormMessage");
        $("supplierDialogTitle").textContent = supplier ? `Sửa nhà cung cấp #${supplier.supplierId}` : "Thêm nhà cung cấp";
        if (supplier) for (const key of ["name", "phone", "email", "address"]) $("supplierForm").elements.namedItem(key).value = supplier[key];
        $("supplierDialog").showModal();
    }
    function openConfirm(kind, id, expected, title, text, data = {}) {
        if (locked()) return;
        confirmation = { kind, id, expected, data }; $("warehouseConfirmTitle").textContent = title; $("warehouseConfirmText").textContent = text; $("warehouseConfirm").showModal();
    }
    $("addPurchaseButton").addEventListener("click", openPurchase);
    for (const [id, view] of [["purchasesTab", "purchases"], ["suppliersTab", "suppliers"]]) {
        $(id).addEventListener("click", () => setWorkspace(view));
        $(id).addEventListener("keydown", (event) => {
            if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
            event.preventDefault();
            const next = event.key === "Home" ? "purchases" : event.key === "End" ? "suppliers" : view === "purchases" ? "suppliers" : "purchases";
            setWorkspace(next); $(next === "purchases" ? "purchasesTab" : "suppliersTab").focus();
        });
    }
    $("closePurchaseDetail").addEventListener("click", () => {
        $("purchaseDetailPanel").classList.remove("is-open");
        $("purchaseList").querySelector(`[data-purchase="${session.purchase?.id}"]`)?.focus();
    });
    $("purchaseDetailPanel").addEventListener("keydown", (event) => { if (event.key === "Escape") $("closePurchaseDetail").click(); });
    for (const id of ["manageSupplierAdd", "addSupplierButton"]) $(id).addEventListener("click", () => openSupplier());
    $("addPurchaseItemButton").addEventListener("click", () => { if (!locked()) addItemRow(); });
    $("purchaseSupplier").addEventListener("change", render);
    for (const id of ["purchaseSearch", "purchaseStatus", "purchaseMonth", "supplierSearch", "supplierStatus"]) $(id).addEventListener("input", render);
    document.addEventListener("click", (event) => {
        const b = event.target.closest("button"); if (!b || locked()) return;
        if (b.dataset.purchase) {
            $("purchaseDetailPanel").classList.add("is-open");
            let failed = false;
            ui.run(async () => {
                try { return await session.selectPurchase(Number(b.dataset.purchase)); }
                catch (error) { failed = true; throw error; }
            }).then(() => {
                if (!failed) return;
                $("purchaseDetailPanel").classList.remove("is-open");
                $("uiMessage").setAttribute("tabindex", "-1");
                $("uiMessage").scrollIntoView({ block: "nearest" });
                $("uiMessage").focus({ preventScroll: true });
            });
            if (window.matchMedia?.("(max-width: 1000px)").matches) $("closePurchaseDetail").focus();
        }
        if (b.dataset.receive || b.dataset.cancelPurchase) {
            const p = session.purchase, receive = Boolean(b.dataset.receive);
            openConfirm(receive ? "receive" : "cancelPurchase", p.id, purchaseSnapshot(p), receive ? `Nhận hàng phiếu #${p.id}` : `Hủy phiếu #${p.id}`,
                receive ? `Xác nhận đã nhận đủ hàng của ${p.supplierName}, tổng phiếu ${money(p.totalAmount)}. Hệ thống sẽ cộng kho và ghi biến động một lần. Không thay đổi cờ hỏng/hết của nguyên liệu.` : "Phiếu sẽ chuyển sang Đã hủy, không cộng tồn kho.");
        }
        if (b.dataset.editSupplier) openSupplier(session.suppliers.find((s) => s.supplierId === Number(b.dataset.editSupplier)));
        if (b.dataset.deleteSupplier || b.dataset.restoreSupplier) {
            const restore = Boolean(b.dataset.restoreSupplier), s = session.suppliers.find((s) => s.supplierId === Number(b.dataset.deleteSupplier || b.dataset.restoreSupplier));
            openConfirm(restore ? "supplierActive" : "deleteSupplier", s.supplierId, supplierSnapshot(s), restore ? "Khôi phục hợp tác" : "Ngừng hợp tác",
                `${s.name}: ${restore ? "cho phép chọn lại khi tạo phiếu mới" : "ngừng chọn cho phiếu mới; phiếu cũ vẫn giữ lịch sử"}.`, { isActive: restore });
        }
    });
    $("purchaseForm").addEventListener("submit", (event) => {
        event.preventDefault(); if (locked() || !optionsReady) return;
        const data = { supplierId: $("purchaseSupplier").value, items: Array.from($("purchaseItemRows").children).map((row) => ({ ingredientId: row.querySelector("[data-ingredient]").value, quantity: row.querySelector("[data-quantity]").value, unitPrice: row.querySelector("[data-price]").value })) };
        ui.run(async () => { const created = await session.command("createPurchase", null, data); $("purchaseDialog").close(); setWorkspace("purchases"); $("purchaseDetailPanel").classList.add("is-open"); ui.message(`Đã tạo phiếu nháp #${created.id}. Kiểm tra chi tiết trước khi xác nhận nhận hàng.`); }, "", "purchaseFormMessage");
    });
    $("supplierForm").addEventListener("submit", (event) => {
        event.preventDefault(); if (locked()) return;
        const data = Object.fromEntries(new FormData(event.currentTarget)), edit = supplierEdit;
        ui.run(async () => {
            const created = await session.command(edit ? "updateSupplier" : "createSupplier", edit?.id, data, edit?.expected);
            $("supplierDialog").close(); if ($("purchaseDialog").open) supplierOptions(edit?.id || created.supplierId);
            ui.message(edit ? "Đã cập nhật nhà cung cấp." : `Đã thêm nhà cung cấp #${created.supplierId}.`);
        }, "", "supplierFormMessage");
    });
    $("warehouseConfirmForm").addEventListener("submit", (event) => {
        event.preventDefault(); if (locked() || !confirmation) return;
        const c = confirmation; $("warehouseConfirm").close(); confirmation = null;
        ui.run(() => session.command(c.kind, c.id, c.data, c.expected), "Đã xử lý và tải lại dữ liệu mới nhất.");
    });
    $("closeWarehouseConfirm").addEventListener("click", () => $("warehouseConfirm").close());
    for (const [dialog, selector] of [["purchaseDialog", "[data-close-purchase-dialog]"], ["supplierDialog", "[data-close-supplier-dialog]"]]) {
        document.querySelectorAll(selector).forEach((b) => b.addEventListener("click", () => { if (!session.busy) $(dialog).close(); }));
        $(dialog).addEventListener("cancel", (event) => { if (session.busy) event.preventDefault(); });
    }
    window.restaurantAuth.ready.then(() => ui.run(async () => {
        session.assertAccess();
        if (session.pending?.accepted && session.pending.kind === "createPurchase" && session.pending.id) session.selectedPurchaseId = session.pending.id;
        await session.refresh();
        const id = session.selectedPurchaseId || Number(new URLSearchParams(window.location.search).get("purchaseId")) || session.purchases[0]?.id;
        if (id) await session.selectPurchase(id);
        if (new URLSearchParams(window.location.search).get("tab") === "suppliers") setWorkspace("suppliers");
        else if (new URLSearchParams(window.location.search).get("purchaseId")) $("purchaseDetailPanel").classList.add("is-open");
    }));
})();
