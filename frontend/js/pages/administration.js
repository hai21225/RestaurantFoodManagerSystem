(function () {
    const { AdminSession, definitions, snapshot } = window.restaurantAdmin;
    const auth = window.restaurantAuth, api = window.restaurantApi;
    const session = new AdminSession({ api, auth, storage: window.sessionStorage });
    const $ = (id) => document.getElementById(id);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const money = (value) => `${Number(value).toLocaleString("vi-VN", { maximumFractionDigits: 2 })} đ`;
    let editing = null, expected = null, confirming = null, targetItem = null;
    const locked = () => session.busy || session.loading || !session.loaded || !!session.pending;
    function message(value = "", error = false) { $("adminNotice").textContent = value; $("adminNotice").classList.toggle("is-error", error); $("adminNotice").hidden = !value; }
    function option(value, label, selected = false, disabled = false) { return `<option value="${esc(value)}"${selected ? " selected" : ""}${disabled ? " disabled" : ""}>${esc(label)}</option>`; }
    function fieldHtml(f, item) {
        let value = item?.[f.name] ?? f.value ?? "";
        const title = `<span>${esc(f.label)}</span>`;
        if (f.type === "checkbox") return `<label class="admin-checkbox"><input name="${f.name}" type="checkbox"${value ? " checked" : ""}>${title}</label>`;
        let input;
        if (f.type === "textarea") input = `<textarea name="${f.name}" rows="3" maxlength="${f.max || 500}">${esc(value)}</textarea>`;
        else if (["select", "area", "topping"].includes(f.type)) {
            let opts;
            if (f.type === "area") opts = option("", "Chưa phân khu", value === "" || value == null) + (session.lookups.areas || []).filter((x) => x.isActive || x.areaId === value).map((x) => option(x.areaId, `${x.areaName}${x.isActive ? "" : " (ngừng dùng)"}`, x.areaId === value)).join("");
            else if (f.type === "topping") opts = option("", "Chọn topping") + session.lookups.toppings.filter((x) => x.isActive && !session.items.some((i) => i.toppingId === x.toppingId)).map((x) => option(x.toppingId, `${x.toppingName} · ${money(x.toppingPrice)}`)).join("");
            else {
                opts = f.options.map((x) => { const [v, name] = Array.isArray(x) ? x : [x, x]; return option(v, name, v === value); }).join("");
                if (value && !f.options.some((x) => (Array.isArray(x) ? x[0] : x) === value)) opts = option(value, `${value} (chọn lại giá trị được hỗ trợ)`, true, true) + opts;
            }
            input = `<select name="${f.name}"${f.required ? " required" : ""}>${opts}</select>`;
        } else {
            if (f.type === "datetime-local") value = String(value).replace(/[zZ]$/, "").slice(0, 19);
            input = `<input name="${f.name}" type="${f.type === "integer" ? "number" : f.type}" value="${esc(value)}"${f.required ? " required" : ""}${f.type === "datetime-local" ? ' step="1"' : ""}${f.max && f.type === "text" ? ` maxlength="${f.max}"` : ""}${["number", "integer"].includes(f.type) ? ` min="${f.min ?? 0}" max="${f.max ?? 9007199254740991}" step="${f.type === "integer" ? "1" : "0.01"}" required` : ""}>`;
        }
        return `<label class="admin-field">${title}${input}</label>`;
    }
    function details(item) {
        const module = session.module;
        if (module === "links") return `${money(item.toppingPrice)} · ${item.isAvailable ? "Đang bán" : "Tạm hết"}`;
        const lines = session.definition.fields.filter((f) => f.name !== session.definition.name).map((f) => {
            let value = item[f.name];
            if (value == null || value === "") value = "—";
            else if (f.type === "checkbox") value = value ? "Có" : "Không";
            else if (["sizePrice", "toppingPrice", "minimumOrderAmount", "maxDiscountAmount"].includes(f.name)) value = money(value);
            else if (f.type === "area") value = item.areaName || "Chưa phân khu";
            else if (f.name === "discountType") value = value === "Percent" ? "Phần trăm" : "Số tiền cố định";
            else if (f.type === "datetime-local") value = String(value).slice(0, 19).replace("T", " ");
            return `<div><dt>${esc(f.label)}</dt><dd>${esc(value)}</dd></div>`;
        });
        if (module === "promotions") lines.push(`<div><dt>Phạm vi</dt><dd>${item.sizeIds?.length || item.categoryIds?.length ? `${item.sizeIds?.length || 0} kích cỡ · ${item.categoryIds?.length || 0} danh mục` : "Toàn hóa đơn"}</dd></div>`);
        if (module === "tables") lines.push(`<div><dt>Trạng thái phục vụ</dt><dd>${esc(({ Available: "Trống", Occupied: "Có khách", Reserved: "Đã đặt", Maintenance: "Bảo trì" })[item.status] || item.status)}</dd></div>`);
        return `<dl class="admin-details">${lines.join("")}</dl>`;
    }
    function render() {
        const d = session.definition, module = session.module;
        $("adminTitle").textContent = d.title;
        $("adminTabs").innerHTML = Object.entries(definitions).filter(([, x]) => auth.hasRole(...x.roles)).map(([key, x]) => `<button type="button" class="admin-tab${key === module ? " is-active" : ""}" data-module="${key}" aria-pressed="${key === module}"${session.busy || session.loading || session.pending ? " disabled" : ""}>${x.title}</button>`).join("");
        $("foodSelector").hidden = !d.food;
        if (d.food) $("managedFood").innerHTML = (session.lookups.foods || []).map((f) => option(f.foodId, `${f.foodName}${f.isActive ? "" : " (ngừng dùng)"}`, f.foodId === Number(session.foodId))).join("");
        $("managedFood").disabled = locked();
        $("adminAdd").hidden = !!d.editOnly; $("adminAdd").disabled = locked() || (d.food && !session.foodId);
        $("adminAdd").textContent = d.singleton ? "Sửa thiết lập" : module === "links" ? "Gán topping" : "Thêm mới";
        $("adminRefresh").disabled = session.busy || session.loading;
        $("adminStatus").disabled = !!d.singleton || module === "links";
        const term = $("adminSearch").value.trim().toLocaleLowerCase("vi"), status = $("adminStatus").value;
        const items = session.items.filter((x) => String(x[d.name]).toLocaleLowerCase("vi").includes(term) && (d.singleton || module === "links" || status === "all" || (x.isActive !== false) === (status === "active")));
        $("adminCount").textContent = session.loaded ? `${items.length} mục` : session.loading ? "Đang tải…" : "Chưa đọc được dữ liệu";
        $("adminRows").innerHTML = !session.loaded && !session.items.length ? '<p class="empty-state">Chờ tải dữ liệu từ máy chủ. Nếu có thông báo lỗi, hãy xử lý hoặc làm mới.</p>' : items.length ? items.map((x) => {
            const n = x[d.id], inactive = x.isActive === false;
            let actions = module !== "links" ? `<button class="secondary-action" type="button" data-edit="${n}">Sửa</button>` : "";
            if (!["tables", "settings", "links"].includes(module)) actions += `<button class="secondary-action" type="button" data-action="${inactive ? "active" : "delete"}" data-id="${n}">${inactive ? "Khôi phục" : "Ngừng dùng"}</button>`;
            if (module === "links") actions += `<button class="secondary-action" type="button" data-action="delete" data-id="${n}">Gỡ khỏi món</button>`;
            if (module === "toppings" && !inactive) actions += `<button class="secondary-action" type="button" data-action="availability" data-id="${n}">${x.isAvailable ? "Báo tạm hết" : "Bật bán"}</button>`;
            if (module === "sizes" && !inactive && !x.isDefault) actions += `<button class="secondary-action" type="button" data-action="default" data-id="${n}">Đặt mặc định</button>`;
            if (module === "promotions") actions += `<button class="secondary-action" type="button" data-targets="${n}">Chọn phạm vi</button>`;
            return `<article class="admin-record admin-data-row" data-admin-module="${module}"><div class="admin-record-heading"><span class="admin-row-mark" aria-hidden="true">${esc(String(x[d.name] || d.title).trim().slice(0, 1).toLocaleUpperCase("vi"))}</span><div><h3>${esc(x[d.name] || d.title)}</h3>${d.singleton ? "" : `<span class="admin-row-status${inactive ? " is-inactive" : ""}">#${n} · ${inactive ? "Ngừng dùng" : "Đang dùng"}</span>`}</div></div><div class="admin-record-detail">${details(x)}</div><div class="admin-actions">${actions}</div></article>`;
        }).join("") : '<p class="empty-state">Không có mục phù hợp. Thử đổi bộ lọc hoặc thêm mới.</p>';
        $("adminRows").querySelectorAll("button").forEach((b) => { b.disabled = locked(); });
        $("adminPending").hidden = !session.pending;
        $("adminPendingText").textContent = session.pending ? `${session.pending.description}. ${session.pending.accepted ? "Máy chủ đã nhận lệnh; cần tải lại dữ liệu." : "Chưa xác định được kết quả. Không gửi lại thao tác."}` : "";
        $("adminReview").disabled = session.busy || session.loading;
        $("adminAck").hidden = !session.pending?.reviewed; $("adminAck").disabled = session.busy;
        for (const form of [$("adminForm"), $("targetForm"), $("adminConfirmForm")]) form.querySelectorAll("input,textarea,select,button[type=submit]").forEach((el) => { el.disabled = locked() || el.dataset.unavailable === "true"; });
        $("adminContext").textContent = module === "settings" ? "Hệ thống tính VAT/phí dịch vụ. Lưu cấu hình không tự tính lại tiền các đơn đã mở. Các khoản tiền hiển thị theo VND."
            : module === "promotions" ? "Thời gian theo Việt Nam (UTC+7). Tạo khuyến mãi và chọn phạm vi là hai bước riêng. Sửa phạm vi không tự tính lại các đơn đã áp dụng."
            : module === "tables" ? "Sửa tên, số chỗ và khu vực. Thay đổi trạng thái/xóa/khôi phục bàn tại màn Bàn ăn."
            : module === "links" ? "Gán/gỡ từng topping cho món đã chọn. Topping ngừng dùng không được API trả trong danh sách gán; khôi phục topping để xem và gỡ liên kết cũ."
            : "Ngừng dùng giữ lịch sử. Chọn bộ lọc Ngừng dùng để xem và khôi phục.";
    }
    async function run(action, success = "") {
        try { const promise = action(); render(); const result = await promise; if (success) message(success); return result; }
        catch (e) { message(`${e.message}${session.pending ? " Hãy tải dữ liệu để đối chiếu; không tự gửi lại." : ""}`, true); }
        finally { render(); }
    }
    function openEditor(item = null) {
        if (locked()) return;
        editing = item; expected = item ? snapshot(item) : null;
        $("adminFormTitle").textContent = `${item ? "Sửa" : "Thêm"} ${session.definition.title.toLocaleLowerCase("vi")}`;
        $("adminFormHint").textContent = session.module === "promotions" && !item ? "Khuyến mãi mới được bật và áp dụng toàn hóa đơn ngay trong khoảng thời gian đã chọn. Nếu cần giới hạn theo món/danh mục, hãy chọn thời điểm bắt đầu trong tương lai, rồi lưu và Chọn phạm vi trước thời điểm đó." : "";
        $("adminFields").innerHTML = session.definition.fields.map((f) => fieldHtml(f, item)).join("");
        $("adminFormMessage").textContent = ""; $("adminEditor").showModal();
    }
    $("adminTabs").addEventListener("click", (e) => {
        const b = e.target.closest("[data-module]"); if (!b || session.busy || session.loading || session.pending) return;
        session.module = b.dataset.module; session.items = []; session.loaded = false;
        message();
        $("adminSearch").value = ""; $("adminStatus").value = "active";
        const url = new URL(location.href); url.searchParams.set("tab", session.module); history.replaceState(null, "", url);
        window.dispatchEvent(new CustomEvent("restaurant:admin-module-changed"));
        run(() => session.refresh());
    });
    $("managedFood").addEventListener("change", () => { session.foodId = Number($("managedFood").value); run(() => session.refresh()); });
    $("adminSearch").addEventListener("input", render); $("adminStatus").addEventListener("change", render);
    $("adminRefresh").addEventListener("click", () => run(() => session.refresh(), "Đã tải dữ liệu mới nhất."));
    $("adminAdd").addEventListener("click", () => openEditor(session.definition.singleton ? session.items[0] : null));
    $("adminRows").addEventListener("click", (e) => {
        const b = e.target.closest("button"); if (!b || locked()) return;
        const n = Number(b.dataset.edit ?? b.dataset.id ?? b.dataset.targets), item = session.items.find((x) => x[session.definition.id] === n); if (!item) return;
        if (b.dataset.edit != null) return openEditor(item);
        if (b.dataset.targets) {
            targetItem = item; expected = snapshot(item);
            $("targetName").textContent = item.name;
            const checks = (items, name, selected, key, label, active) => items.map((x) => `<label class="admin-checkbox"><input type="checkbox" name="${name}" value="${x[key]}"${selected.includes(x[key]) ? " checked" : ""}${!active(x) && !selected.includes(x[key]) ? ' disabled data-unavailable="true"' : ""}><span>${esc(label(x))}${active(x) ? "" : " (ngừng dùng — bỏ chọn để lưu)"}</span></label>`).join("");
            $("targetCategories").innerHTML = checks(session.lookups.categories, "categoryIds", item.categoryIds || [], "id", (x) => x.name, (x) => x.isActive);
            $("targetSizes").innerHTML = checks(session.lookups.sizes, "sizeIds", item.sizeIds || [], "sizeId", (x) => `${x.foodName} · ${x.sizeName}`, (x) => x.isActive && x.foodIsActive);
            $("targetMessage").textContent = ""; $("confirmWholeOrder").checked = false; $("targetDialog").showModal(); return;
        }
        const action = b.dataset.action;
        confirming = { action, id: n, expected: snapshot(item), data: action === "availability" ? { isAvailable: !item.isAvailable } : { isActive: true } };
        $("adminConfirmText").textContent = `${b.textContent}: ${item[session.definition.name]}. ${action === "delete" && session.module !== "links" ? "Mục sẽ ẩn khỏi danh sách đang dùng, lịch sử vẫn được giữ." : "Dữ liệu sẽ được tải lại từ máy chủ sau khi lưu."}`;
        $("adminConfirmMessage").textContent = ""; $("adminConfirm").showModal();
    });
    $("adminForm").addEventListener("submit", async (e) => {
        e.preventDefault(); if (locked()) return;
        const body = Object.fromEntries(new FormData(e.currentTarget));
        try {
            const p = session.command(editing ? "update" : "create", editing?.[session.definition.id] ?? null, body, expected); render(); const result = await p;
            $("adminEditor").close(); message(result?.unchanged ? "Thông tin không thay đổi. Đã đối chiếu với dữ liệu hiện tại." : "Đã lưu và tải lại dữ liệu từ máy chủ.");
        } catch (error) { $("adminFormMessage").textContent = error.message; message(error.message, true); }
        finally { render(); }
    });
    $("adminConfirmForm").addEventListener("submit", async (e) => {
        e.preventDefault(); if (locked() || !confirming) return;
        try { const p = session.command(confirming.action, confirming.id, confirming.data, confirming.expected); render(); await p; $("adminConfirm").close(); message("Đã cập nhật và tải lại dữ liệu."); }
        catch (error) { $("adminConfirmMessage").textContent = error.message; message(error.message, true); }
        finally { render(); }
    });
    $("targetForm").addEventListener("submit", async (e) => {
        e.preventDefault(); if (locked() || !targetItem) return;
        const form = new FormData(e.currentTarget), body = { sizeIds: form.getAll("sizeIds").map(Number), categoryIds: form.getAll("categoryIds").map(Number) };
        if (!body.sizeIds.length && !body.categoryIds.length && !$("confirmWholeOrder").checked) { $("targetMessage").textContent = "Không chọn phạm vi sẽ áp dụng toàn hóa đơn. Hãy đánh dấu xác nhận nếu đây là ý định của bạn."; return; }
        try { const p = session.command("targets", targetItem.id, body, expected); render(); await p; $("targetDialog").close(); message("Đã cập nhật phạm vi. Các đơn đã áp khuyến mãi giữ số tiền server đã ghi."); }
        catch (error) { $("targetMessage").textContent = error.message; message(error.message, true); }
        finally { render(); }
    });
    $("adminReview").addEventListener("click", () => run(() => session.review(() => session.load()), "Đã tải dữ liệu. Kiểm tra thao tác trước rồi xác nhận đã đối chiếu."));
    $("adminAck").addEventListener("click", () => { try { session.acknowledge(); render(); message("Đã xác nhận đối chiếu. Kiểm tra trước khi thực hiện lệnh mới."); } catch (e) { message(e.message, true); } });
    document.querySelectorAll("[data-close]").forEach((b) => b.addEventListener("click", () => $(b.dataset.close).close()));
    auth.ready.then(() => {
        const requested = new URL(location.href).searchParams.get("tab");
        if (!session.pending) session.module = Object.prototype.hasOwnProperty.call(definitions, requested) ? requested : Object.keys(definitions).find((m) => auth.hasRole(...definitions[m].roles)) || "categories";
        session.foodId ||= Number(new URL(location.href).searchParams.get("foodId")) || null;
        run(() => session.refresh());
    });
})();
