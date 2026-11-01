(function () {
    const { CatalogSession, ingredientSnapshot, recipeSnapshot, stockStatuses } = window.restaurantCatalog;
    const auth = window.restaurantAuth, session = new CatalogSession({ api: window.restaurantApi, auth, storage: window.sessionStorage });
    const $ = (id) => document.getElementById(id);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const quantity = (value) => Number(value).toLocaleString("vi-VN", { maximumFractionDigits: 3 });
    let tab = new URLSearchParams(window.location.search).get("tab") === "recipes" ? "recipes" : "ingredients";
    let selectedSizeId = null, editingIngredient = null, editingRecipe = null, editingStatus = null;
    let editorBusy = false, editorVersion = 0, confirmAction = null;
    const locked = () => session.busy || session.loading || session.detailLoading || editorBusy || !!session.pending || !session.loaded;
    function message(text = "", error = false, target = "catalogNotice") { $(target).textContent = text; $(target).classList.toggle("is-error", error); }
    function badge(label, tone = "") { return `<span class="catalog-state ${tone}">${esc(label)}</span>`; }
    function setTab(value) {
        tab = value; $("ingredientsPanel").hidden = tab !== "ingredients"; $("recipesPanel").hidden = tab !== "recipes";
        $("ingredientsTab").setAttribute("aria-selected", String(tab === "ingredients")); $("recipesTab").setAttribute("aria-selected", String(tab === "recipes"));
        $("addIngredient").hidden = tab !== "ingredients"; $("addRecipe").hidden = tab !== "recipes";
        const url = new URL(window.location.href); url.searchParams.set("tab", tab); window.history.replaceState(null, "", url);
    }
    function renderIngredients() {
        const query = $("ingredientSearch").value.trim().toLocaleLowerCase("vi"), active = $("ingredientActiveFilter").value, status = $("ingredientStockFilter").value;
        const rows = session.ingredients.filter((i) => (`${i.name} ${i.unit}`).toLocaleLowerCase("vi").includes(query)
            && (active === "all" || i.isActive === (active === "active")) && (status === "all" || i.stockStatus === status));
        $("ingredientCount").textContent = `${rows.length} nguyên liệu`;
        $("ingredientRows").innerHTML = rows.length ? rows.map((i) => `<tr><td><strong>${esc(i.name)}</strong><small>#${i.ingredientId} · ${i.isActive ? "Đang dùng" : "Ngừng dùng"}</small></td>
            <td>${esc(i.unit)}</td><td>${quantity(i.minimumStock)}</td><td>${badge(stockStatuses[i.stockStatus] || i.stockStatus, i.stockStatus === "Spoiled" ? "is-danger" : i.stockStatus === "OutOfStock" ? "is-warning" : "")}${!i.isActive ? badge("Ngừng dùng", "is-inactive") : ""}</td>
            <td><div class="catalog-row-actions"><button class="secondary-action" data-edit-ingredient="${i.ingredientId}" ${locked() ? "disabled" : ""}>Sửa</button><button class="secondary-action" data-stock-status="${i.ingredientId}" ${locked() ? "disabled" : ""}>Tình trạng</button>
            <button class="secondary-action" data-ingredient-active="${i.ingredientId}" ${locked() ? "disabled" : ""}>${i.isActive ? "Ngừng dùng" : "Khôi phục"}</button><a href="inventory.html?ingredientId=${i.ingredientId}">Xem kho / Lịch sử</a></div></td></tr>`).join("")
            : '<tr><td colspan="5" class="catalog-empty">Không có nguyên liệu phù hợp. Bấm Thêm nguyên liệu để tạo danh mục mới.</td></tr>';
    }
    function renderRecipes() {
        const foodValue = $("recipeFoodFilter").value, search = $("recipeSearch").value.trim().toLocaleLowerCase("vi"), state = $("recipeStateFilter").value;
        $("recipeFoodFilter").innerHTML = '<option value="all">Tất cả món</option>' + session.foods.map((f) => `<option value="${f.foodId}">${esc(f.foodName)}${f.isActive ? "" : " (ngừng dùng)"}</option>`).join("");
        $("recipeFoodFilter").value = session.foods.some((f) => String(f.foodId) === foodValue) ? foodValue : "all";
        const sizes = session.sizes.filter((s) => {
            const active = session.activeRecipe(s.sizeId), versions = session.recipes.filter((r) => r.sizeId === s.sizeId);
            return (s.isActive || versions.length) && ($("recipeFoodFilter").value === "all" || String(s.foodId) === $("recipeFoodFilter").value)
                && `${s.foodName} ${s.sizeName} ${versions.map((r) => `${r.name} ${r.version}`).join(" ")}`.toLocaleLowerCase("vi").includes(search)
                && (state === "all" || !!active === (state === "configured"));
        });
        $("recipeCount").textContent = `${sizes.length} kích cỡ`;
        $("recipeRows").innerHTML = sizes.length ? sizes.map((s) => {
            const r = session.activeRecipe(s.sizeId);
            return `<article class="catalog-recipe-row${selectedSizeId === s.sizeId ? " is-selected" : ""}"><strong>${esc(s.foodName)} · ${esc(s.sizeName)}</strong>
                <p>${r ? `${esc(r.name)} · Phiên bản ${esc(r.version)} · ${r.ingredientCount} nguyên liệu` : "Chưa có công thức đang hoạt động"}</p>
                <div>${badge(r ? "Đã thiết lập" : "Chưa thiết lập", r ? "" : "is-warning")}${!s.isActive ? badge("Kích cỡ ngừng dùng", "is-inactive") : ""}${!s.foodIsActive ? badge("Món ngừng dùng", "is-inactive") : ""}</div>
                <div class="catalog-row-actions"><button class="secondary-action" data-view-size="${s.sizeId}" ${session.busy || session.loading ? "disabled" : ""}>Xem / Phiên bản</button>
                <button class="${r ? "secondary-action" : "primary-action"}" data-edit-size="${s.sizeId}" ${locked() || !s.isActive ? "disabled" : ""}>${r ? "Sửa công thức" : "Thiết lập"}</button></div></article>`;
        }).join("") : '<p class="catalog-empty">Chưa có kích cỡ phù hợp. Kiểm tra bộ lọc hoặc tạo kích cỡ tại <a href="foods.html">Món ăn</a>.</p>';
        renderDetail();
    }
    function renderDetail() {
        const size = session.sizes.find((s) => s.sizeId === selectedSizeId), detail = session.detail;
        if (!size) { $("recipeDetail").innerHTML = '<p class="catalog-empty">Chọn Xem / Phiên bản bên cạnh một kích cỡ để xem định lượng và lịch sử.</p>'; return; }
        const versions = session.recipes.filter((r) => r.sizeId === size.sizeId).sort((a, b) => b.recipeId - a.recipeId);
        const current = session.activeRecipe(size.sizeId);
        let content = `<strong>${esc(size.foodName)} · ${esc(size.sizeName)}</strong>`;
        if (session.detailLoading) content += '<p class="catalog-caption">Đang tải định lượng…</p>';
        else if (detail && detail.sizeId === size.sizeId) {
            content += `<p>${esc(detail.name)} · Phiên bản ${esc(detail.version)} · #${detail.recipeId}</p>${badge(detail.isActive ? "Đang hoạt động" : "Phiên bản ngừng dùng", detail.isActive ? "" : "is-inactive")}
                <dl>${detail.ingredients.map((i) => `<dt>${esc(i.ingredientName)}${session.ingredients.find((x) => x.ingredientId === i.ingredientId)?.isActive === false ? " (ngừng dùng)" : ""}</dt><dd>${quantity(i.quantity)} ${esc(i.unit)}</dd>`).join("")}</dl>
                <div class="catalog-actions">${detail.isActive ? `<button class="secondary-action" data-stop-recipe="${detail.recipeId}" ${locked() ? "disabled" : ""}>Ngừng công thức</button><button class="primary-action" data-edit-recipe="${detail.recipeId}" ${locked() || !size.isActive ? "disabled" : ""}>Sửa bản đang dùng</button>`
                    : `<button class="secondary-action" data-restore-recipe="${detail.recipeId}" ${locked() || current || !size.isActive ? "disabled" : ""}>Khôi phục phiên bản</button>`}</div>
                ${!detail.isActive && current ? '<p class="catalog-caption">Kích cỡ này đã có bản đang dùng. Ngừng bản hiện tại trước khi khôi phục bản cũ.</p>' : ""}`;
        } else content += '<p class="catalog-caption">Chưa có công thức đang dùng. Thiết lập mới hoặc chọn một phiên bản bên dưới.</p>';
        content += `<div class="catalog-history"><h3>Các phiên bản</h3><div class="catalog-history-list">${versions.map((r) => `<button class="secondary-action" data-view-recipe="${r.recipeId}" ${session.busy || session.loading ? "disabled" : ""}>#${r.recipeId} · ${esc(r.version)} · ${r.isActive ? "Đang dùng" : "Ngừng dùng"} · ${esc(r.name)}</button>`).join("") || '<p class="catalog-caption">Chưa có lịch sử phiên bản.</p>'}</div></div>`;
        $("recipeDetail").innerHTML = content;
    }
    function render() {
        const allowed = session.hasAccess(); $("catalogContent").hidden = !allowed; $("catalogAccess").hidden = allowed; if (!allowed) return;
        setTab(tab);
        $("catalogRefresh").disabled = session.busy || session.loading || editorBusy;
        $("addIngredient").disabled = locked(); $("addRecipe").disabled = locked() || !session.sizes.some((s) => s.isActive && !session.activeRecipe(s.sizeId));
        $("catalogPending").hidden = !session.pending;
        if (session.pending) $("pendingSummary").textContent = `${session.pending.kind.includes("Recipe") ? "Công thức" : "Nguyên liệu"} #${session.pending.id || "chưa rõ mã"} · ${session.pending.body?.name || session.pending.body?.stockStatus || "Thay đổi trạng thái"} · ${session.pending.accepted ? "Đã nhận phản hồi thành công, cần tải lại danh mục." : "Chưa rõ kết quả ghi."}`;
        $("reviewPending").disabled = session.busy || session.loading;
        $("acknowledgePending").hidden = !session.pending?.reviewed;
        $("acknowledgePending").disabled = session.busy || session.loading || !session.loaded;
        for (const id of ["ingredientSubmit", "recipeSubmit", "stockStatusSubmit"]) $(id).disabled = locked();
        for (const id of ["ingredientForm", "recipeForm", "stockStatusForm"]) $(id).querySelectorAll("input,select").forEach((field) => {
            field.disabled = session.busy || session.loading || editorBusy || !!session.pending || (field.id === "recipeSize" && !!editingRecipe);
        });
        $("addRecipeLine").disabled = session.busy || session.loading || !!session.pending;
        $("confirmSubmit").disabled = session.busy || session.loading || editorBusy;
        if (!session.loaded) {
            $("catalogSummary").innerHTML = "";
            $("ingredientCount").textContent = "—"; $("recipeCount").textContent = "—";
            $("ingredientRows").innerHTML = `<tr><td colspan="5" class="catalog-empty">${session.busy || session.loading ? "Đang tải danh mục…" : "Chưa tải được danh mục. Bấm Làm mới để thử lại."}</td></tr>`;
            $("recipeRows").innerHTML = '<p class="catalog-empty">Chưa tải được danh mục.</p>';
            $("recipeDetail").innerHTML = '<p class="catalog-empty">Tải lại danh mục trước khi xem phiên bản.</p>'; return;
        }
        $("catalogSummary").innerHTML = [["Nguyên liệu đang dùng", session.ingredients.filter((i) => i.isActive).length], ["Nguyên liệu ngừng dùng", session.ingredients.filter((i) => !i.isActive).length], ["Công thức đang dùng", session.recipes.filter((r) => r.isActive).length]]
            .map(([label, value]) => `<article><span>${label}</span><strong>${value}</strong></article>`).join("");
        renderIngredients(); renderRecipes();
    }
    async function run(action, success = "", target = "catalogNotice") {
        try { const promise = action(); render(); const result = await promise; if (success) message(success); return { ok: true, result }; }
        catch (error) {
            message(error.message, true, target);
            if (target !== "catalogNotice") message(error.message, true);
            if (target === "ingredientFormMessage" && error.status === 409 && /unit|đơn vị/i.test(error.message) && editingIngredient) {
                $("ingredientUnit").value = editingIngredient.item.unit; $("ingredientUnit").readOnly = true;
                $("unitHelp").textContent = "Nguyên liệu đã được sử dụng nên đơn vị được khóa. Hãy tạo nguyên liệu mới nếu cần đổi đơn vị.";
            }
            if ([400, 404, 409].includes(error.status) && !session.pending && session.hasAccess()) {
                try { await session.refresh(); } catch (_) { /* Giữ nguyên lỗi thao tác chính. */ }
            }
            return { ok: false, error };
        } finally { render(); }
    }
    async function openIngredient(id = null) {
        if (locked()) return;
        const version = ++editorVersion; editorBusy = true; render();
        try {
            const item = id ? await session.ingredientDetails(id) : null;
            if (version !== editorVersion) return;
            editingIngredient = item ? { item, snapshot: ingredientSnapshot(item) } : null;
            $("ingredientForm").reset(); $("ingredientName").value = item?.name || ""; $("ingredientUnit").value = item?.unit || ""; $("ingredientMinimum").value = item?.minimumStock ?? 0;
            $("ingredientDialogTitle").textContent = item ? "Sửa nguyên liệu" : "Thêm nguyên liệu"; message("", false, "ingredientFormMessage");
            $("ingredientUnit").readOnly = !!item; $("unitHelp").textContent = item ? "Đang kiểm tra lịch sử sử dụng đơn vị…" : "Nhập đơn vị kho dùng thống nhất cho phiếu nhập và định lượng.";
            $("ingredientDialog").showModal();
            if (item) {
                try {
                    const usage = await session.ingredientUsage(item.ingredientId);
                    if (version !== editorVersion) return;
                    $("ingredientUnit").readOnly = usage.locked;
                    $("unitHelp").textContent = usage.locked ? `${usage.reason} Đơn vị đã khóa; tạo nguyên liệu mới nếu cần đơn vị khác.` : `${usage.reason} Có thể sửa đơn vị.`;
                } catch (error) { if (version === editorVersion) $("unitHelp").textContent = `${error.message} Đơn vị tạm khóa; vẫn có thể sửa tên và mức tồn tối thiểu.`; }
            }
            if (version === editorVersion) $("ingredientName").focus();
        } catch (error) { message(error.message, true); }
        finally { if (version === editorVersion) editorBusy = false; render(); if (version === editorVersion && $("ingredientDialog").open) $("ingredientName").focus(); }
    }
    function addLine(item = {}) {
        const row = document.createElement("div"); row.className = "catalog-line";
        const chosen = Number(item.ingredientId), entries = session.ingredients.filter((i) => i.isActive || i.ingredientId === chosen);
        row.innerHTML = `<label class="form-field"><span>Nguyên liệu</span><select data-line-ingredient required><option value="">Chọn nguyên liệu</option>${entries.map((i) => `<option value="${i.ingredientId}" ${i.ingredientId === chosen ? "selected" : ""}>${esc(i.name)} · ${esc(i.unit)}${i.isActive ? "" : " (ngừng dùng)"}</option>`).join("")}</select><span class="line-unit"></span></label>
            <label class="form-field"><span>Định lượng</span><input data-line-quantity type="number" min="0.001" step="0.001" value="${esc(item.quantity ?? "")}" required><span class="line-unit">Theo đơn vị kho</span></label><button class="secondary-action remove-line" type="button" aria-label="Bỏ dòng nguyên liệu">×</button>`;
        const unit = () => { const i = session.ingredients.find((i) => i.ingredientId === Number(row.querySelector("[data-line-ingredient]").value)); row.querySelector(".line-unit").textContent = i ? `Đơn vị: ${i.unit}${i.isActive ? "" : " · Cần thay hoặc khôi phục trước khi lưu"}` : "Chưa chọn nguyên liệu"; };
        row.querySelector("select").addEventListener("change", unit); row.querySelector("button").addEventListener("click", () => { if (session.busy) return; row.remove(); if (!$("recipeLines").children.length) addLine(); });
        $("recipeLines").appendChild(row); unit();
    }
    async function openRecipe(sizeId = null, recipeId = null) {
        if (locked()) return;
        const version = ++editorVersion; editorBusy = true; render();
        try {
            await session.refresh(); if (version !== editorVersion) return;
            if (!session.ingredients.some((i) => i.isActive)) { setTab("ingredients"); message("Thêm nguyên liệu đang hoạt động trước khi tạo công thức.", true); return; }
            const detail = recipeId ? await session.recipeDetails(recipeId) : null;
            if (version !== editorVersion) return;
            if (detail && !detail.isActive) throw Object.assign(new Error("Phiên bản này đã ngừng dùng. Chọn sửa bản đang hoạt động."), { status: 409 });
            editingRecipe = detail ? { item: detail, snapshot: recipeSnapshot(detail) } : null;
            const eligible = session.sizes.filter((s) => s.isActive && (!session.activeRecipe(s.sizeId) || s.sizeId === detail?.sizeId));
            const selected = detail?.sizeId || sizeId || eligible[0]?.sizeId;
            if (!eligible.some((s) => s.sizeId === selected)) throw new Error("Kích cỡ không hoạt động hoặc đã có công thức. Tải lại danh sách để kiểm tra.");
            $("recipeForm").reset(); $("recipeLines").innerHTML = "";
            $("recipeSize").innerHTML = eligible.map((s) => `<option value="${s.sizeId}">${esc(s.foodName)} · ${esc(s.sizeName)}${s.foodIsActive ? "" : " (món ngừng dùng)"}</option>`).join(""); $("recipeSize").value = selected;
            $("recipeSize").disabled = !!detail; $("recipeName").value = detail?.name || ""; $("recipeVersion").value = "";
            $("recipeDialogTitle").textContent = detail ? "Sửa công thức · Tạo phiên bản mới" : "Tạo công thức";
            $("recipeDialogContext").textContent = detail ? `Bản ${detail.version} (#${detail.recipeId}) được giữ trong lịch sử sau khi lưu.` : "Mỗi kích cỡ có một công thức đang hoạt động.";
            message("", false, "recipeFormMessage"); (detail?.ingredients || [{}]).forEach(addLine); $("recipeDialog").showModal(); $("recipeName").focus();
        } catch (error) { message(error.message, true); }
        finally { if (version === editorVersion) editorBusy = false; render(); if (version === editorVersion && $("recipeDialog").open) $("recipeName").focus(); }
    }
    async function selectSize(id, recipeId = null) {
        selectedSizeId = id; session.detail = null;
        const target = recipeId || session.activeRecipe(id)?.recipeId;
        if (target) await run(() => session.selectRecipe(target)); else render();
    }
    function confirm(title, text, action) {
        $("confirmTitle").textContent = title; $("confirmText").textContent = text; message("", false, "confirmMessage"); confirmAction = action; $("catalogConfirm").showModal();
    }
    $("ingredientForm").addEventListener("submit", async (event) => {
        event.preventDefault(); if (locked() || !$("ingredientForm").reportValidity()) return;
        message("", false, "ingredientFormMessage");
        const data = { name: $("ingredientName").value, unit: $("ingredientUnit").value, minimumStock: $("ingredientMinimum").value };
        const out = await run(() => session.command(editingIngredient ? "updateIngredient" : "createIngredient", editingIngredient?.item.ingredientId || null, data, editingIngredient?.snapshot), "Đã lưu nguyên liệu. Nguyên liệu đang hoạt động có thể chọn tại Công thức và Nhập hàng.", "ingredientFormMessage");
        if (out.ok) $("ingredientDialog").close();
    });
    $("recipeForm").addEventListener("submit", async (event) => {
        event.preventDefault(); if (locked() || !$("recipeForm").reportValidity()) return;
        const sizeId = Number($("recipeSize").value), ingredients = Array.from($("recipeLines").children).map((row) => ({ ingredientId: Number(row.querySelector("[data-line-ingredient]").value), quantity: row.querySelector("[data-line-quantity]").value }));
        const out = await run(() => session.command(editingRecipe ? "updateRecipe" : "createRecipe", editingRecipe?.item.recipeId || null, { sizeId, name: $("recipeName").value, version: $("recipeVersion").value, ingredients }, editingRecipe?.snapshot), "Đã lưu và tải lại phiên bản công thức hiện tại.", "recipeFormMessage");
        if (out.ok) { $("recipeDialog").close(); await selectSize(sizeId); }
    });
    $("stockStatusForm").addEventListener("submit", async (event) => {
        event.preventDefault(); if (locked() || !editingStatus) return;
        const out = await run(() => session.command("ingredientStatus", editingStatus.item.ingredientId, { stockStatus: $("stockStatusValue").value }, editingStatus.snapshot), "Đã cập nhật tình trạng nguyên liệu; số lượng tồn giữ nguyên.", "stockStatusMessage"); if (out.ok) $("stockStatusDialog").close();
    });
    $("confirmForm").addEventListener("submit", async (event) => {
        event.preventDefault(); if (session.busy || session.loading || !confirmAction) return;
        const out = await run(confirmAction, "Đã thực hiện và tải lại danh mục.", "confirmMessage"); if (out.ok) $("catalogConfirm").close();
    });
    $("ingredientRows").addEventListener("click", async (event) => {
        const button = event.target.closest("button"); if (!button || locked()) return;
        if (button.dataset.editIngredient) return openIngredient(Number(button.dataset.editIngredient));
        if (button.dataset.stockStatus) {
            const out = await run(() => session.ingredientDetails(Number(button.dataset.stockStatus))); if (!out.ok) return;
            editingStatus = { item: out.result, snapshot: ingredientSnapshot(out.result) }; $("stockStatusContext").textContent = out.result.name; $("stockStatusValue").value = out.result.stockStatus; message("", false, "stockStatusMessage"); $("stockStatusDialog").showModal(); return;
        }
        const item = session.ingredients.find((i) => i.ingredientId === Number(button.dataset.ingredientActive));
        if (item) confirm(item.isActive ? "Ngừng dùng nguyên liệu?" : "Khôi phục nguyên liệu?", `${item.name} · ${item.unit}. Ngừng dùng sẽ ẩn khỏi lựa chọn công thức/phiếu nhập mới. Công thức đang dùng không tự ngừng theo. Tồn kho và lịch sử vẫn được giữ.`, () => session.command(item.isActive ? "deleteIngredient" : "ingredientActive", item.ingredientId, { isActive: true }, ingredientSnapshot(item)));
    });
    $("recipeRows").addEventListener("click", (event) => {
        const button = event.target.closest("button"); if (!button) return;
        const id = Number(button.dataset.viewSize || button.dataset.editSize);
        if (button.dataset.viewSize) selectSize(id); else if (button.dataset.editSize) openRecipe(id, session.activeRecipe(id)?.recipeId || null);
    });
    $("recipeDetail").addEventListener("click", (event) => {
        const button = event.target.closest("button"); if (!button) return;
        if (button.dataset.viewRecipe) { const id = Number(button.dataset.viewRecipe), r = session.recipes.find((r) => r.recipeId === id); if (r) selectSize(r.sizeId, id); return; }
        const detail = session.detail; if (!detail || locked()) return;
        if (button.dataset.editRecipe) return openRecipe(detail.sizeId, detail.recipeId);
        const restore = !!button.dataset.restoreRecipe, expected = recipeSnapshot(detail);
        confirm(restore ? "Khôi phục phiên bản công thức?" : "Ngừng công thức?", `${detail.name} · Phiên bản ${detail.version}. Định lượng và các phiên bản cũ vẫn được giữ. Chỉ một công thức được hoạt động cho kích cỡ này.`, async () => { await session.command(restore ? "recipeActive" : "deleteRecipe", detail.recipeId, { isActive: true }, expected); await selectSize(detail.sizeId, detail.recipeId); });
    });
    $("catalogRefresh").addEventListener("click", () => run(() => session.refresh(), "Đã tải danh mục mới nhất."));
    $("addIngredient").addEventListener("click", () => openIngredient()); $("addRecipe").addEventListener("click", () => openRecipe());
    $("addRecipeLine").addEventListener("click", () => { if (!session.busy) addLine(); });
    $("reviewPending").addEventListener("click", () => run(async () => { const done = await session.reviewPending(); message(done ? "Đã đối chiếu thao tác có phản hồi thành công." : "Đã tải danh mục mới. Đối chiếu nguyên liệu và phiên bản công thức, rồi xác nhận thủ công."); }));
    $("acknowledgePending").addEventListener("click", () => confirm("Đã đối chiếu xong?", "Chỉ xác nhận sau khi kiểm tra danh mục và lịch sử phiên bản. Thao tác này mở khóa, không gửi lại yêu cầu ghi trước đó.", () => session.acknowledgeReview()));
    for (const [id, value] of [["ingredientsTab", "ingredients"], ["recipesTab", "recipes"]]) $(id).addEventListener("click", () => { setTab(value); render(); });
    for (const id of ["ingredientSearch", "ingredientActiveFilter", "ingredientStockFilter", "recipeSearch", "recipeFoodFilter", "recipeStateFilter"]) $(id).addEventListener("input", render);
    for (const id of ["ingredientFilters", "recipeFilters"]) $(id).addEventListener("submit", (event) => event.preventDefault());
    document.querySelectorAll("[data-close]").forEach((button) => button.addEventListener("click", () => { if (session.busy) return; ++editorVersion; editorBusy = false; $(button.dataset.close).close(); render(); }));
    document.querySelectorAll("dialog").forEach((dialog) => dialog.addEventListener("cancel", (event) => { if (session.busy) event.preventDefault(); else { ++editorVersion; editorBusy = false; render(); } }));
    auth.ready.then(() => { render(); if (session.hasAccess()) run(() => session.refresh()); });
})();
