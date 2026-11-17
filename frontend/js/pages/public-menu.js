(function () {
    const $ = (selector) => document.querySelector(selector);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
    const money = (value) => typeof value === "number" && Number.isFinite(value) ? `${value.toLocaleString("vi-VN")}đ` : "—";
    const api = window.restaurantApi;
    const auth = window.restaurantAuth;

    const state = {
        categories: [],
        foods: [],
        promotions: [],
        areas: [],
        tables: [],
        selectedCategory: "all",
        selectedArea: "all",
        search: ""
    };

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

    function renderHeaderActions() {
        const user = auth?.getUser();
        if (!user) return;
        const homeUrl = auth.getHomeUrl();
        $("#publicHeaderActions").innerHTML = `
            <a class="secondary-action" href="${esc(homeUrl)}">Vào hệ thống (${esc(user.fullName || user.userName)})</a>
            <a class="primary-action" href="reservations.html">Đặt bàn</a>`;
        $("#reserveCtaLink").href = "reservations.html";
        $("#reserveCtaLink").textContent = "Đặt bàn ngay";
        $("#loginCtaLink").hidden = true;
    }

    function renderPromotions() {
        const section = $("#promoSection");
        if (!state.promotions.length) {
            section.hidden = true;
            return;
        }
        section.hidden = false;
        $("#promoBanner").innerHTML = state.promotions.map((p) => {
            const discountText = p.discountType === "Percent"
                ? `Giảm ${p.discountValue}%`
                : `Giảm ${money(p.discountValue)}`;
            const minText = p.minimumOrderAmount > 0 ? ` · Đơn tối thiểu ${money(p.minimumOrderAmount)}` : "";
            return `<article class="v2-promo-card">
                <strong>${esc(p.name)} — ${esc(discountText)}</strong>
                <p>${esc(p.description || "Áp dụng tại nhà hàng")}</p>
                <div class="v2-promo-meta">${esc(discountText)}${esc(minText)}</div>
            </article>`;
        }).join("");
    }

    function renderCategories() {
        const list = [{ id: "all", name: "Tất cả" }, ...state.categories];
        $("#categoryTabs").innerHTML = list.map((cat) => {
            const count = cat.id === "all" ? state.foods.length : state.foods.filter((f) => f.categoryId === cat.id).length;
            const active = state.selectedCategory === cat.id;
            return `<button type="button" class="v2-pill${active ? " is-active" : ""}" role="tab"
                aria-selected="${active}" data-category-id="${esc(cat.id)}">${esc(cat.name)} (${count})</button>`;
        }).join("");
    }

    function renderMenu() {
        const visible = state.foods.filter((f) => (state.selectedCategory === "all" || f.categoryId === state.selectedCategory) && matchesFood(f));
        $("#menuCount").textContent = `${visible.length} món`;
        if (!visible.length) {
            $("#menuGrid").innerHTML = '<div class="empty-state"><strong>Không tìm thấy món phù hợp</strong><span>Thử từ khóa khác hoặc chọn Tất cả danh mục.</span></div>';
            return;
        }
        $("#menuGrid").innerHTML = visible.map((food) => {
            const initials = (food.foodName || "M").trim().split(/\s+/).slice(0, 2).map((w) => w[0]).join("").toLocaleUpperCase("vi-VN");
            const imgUrl = safeImageUrl(food.foodImageUrl);
            const sizesHtml = (food.sizes || []).map((s) => `<span class="v2-chip">${esc(s.sizeName)}: <strong>${money(s.sizePrice)}</strong></span>`).join("");
            const toppingsHtml = (food.toppings || []).length
                ? `<div class="v2-chip-list">${food.toppings.map((t) => `<span class="v2-chip">+ ${esc(t.toppingName)} (${money(t.toppingPrice)})</span>`).join("")}</div>`
                : "";
            return `<article class="v2-food-card">
                <div class="v2-food-head">
                    <div class="v2-food-thumb" aria-hidden="true">
                        <span>${esc(initials)}</span>
                        ${imgUrl ? `<img src="${esc(imgUrl)}" alt="" loading="lazy" data-public-food-img>` : ""}
                    </div>
                    <div>
                        <h3 class="v2-food-title">${esc(food.foodName)}</h3>
                        ${food.foodDescription ? `<p class="v2-food-desc">${esc(food.foodDescription)}</p>` : ""}
                    </div>
                </div>
                <div class="v2-chip-list">${sizesHtml}</div>
                ${toppingsHtml}
            </article>`;
        }).join("");

        $("#menuGrid").querySelectorAll("[data-public-food-img]").forEach((img) => {
            img.addEventListener("error", () => { img.hidden = true; });
        });
    }

    function renderAreas() {
        const select = $("#areaFilter");
        const options = ['<option value="all">Tất cả khu vực</option>']
            .concat(state.areas.map((a) => `<option value="${esc(a.tableAreaId ?? a.areaId)}">${esc(a.name || a.areaName)}</option>`));
        select.innerHTML = options.join("");
        select.value = state.selectedArea;
    }

    function renderTables() {
        const statusMap = {
            Available: { label: "Còn trống", cls: "is-available" },
            Occupied: { label: "Đang phục vụ", cls: "is-occupied" },
            Reserved: { label: "Đã đặt", cls: "is-pending" },
            Maintenance: { label: "Bảo trì", cls: "is-maintenance" }
        };
        const filtered = state.tables.filter((t) => state.selectedArea === "all" || String(t.areaId ?? "") === String(state.selectedArea));
        const availableCount = filtered.filter((t) => t.status === "Available").length;
        $("#tableSummaryCount").textContent = `${availableCount}/${filtered.length} bàn trống`;

        if (!filtered.length) {
            $("#publicTableList").innerHTML = '<p class="v2-notice">Không có bàn trong khu vực đã chọn.</p>';
            return;
        }
        $("#publicTableList").innerHTML = filtered.map((t) => {
            const st = statusMap[t.status] || { label: t.status || "—", cls: "" };
            return `<div class="v2-table-item">
                <div>
                    <strong>Bàn ${esc(t.tableNumber)}</strong>
                    <div class="v2-promo-meta">${esc(t.areaName || "Khu chung")} · ${t.capacity} khách</div>
                </div>
                <span class="v2-badge ${st.cls}">${esc(st.label)}</span>
            </div>`;
        }).join("");
    }

    async function loadAll() {
        const notice = $("#publicNotice");
        notice.hidden = true;
        try {
            const [menu, promotions, areas, tables] = await Promise.all([
                api.get("/menu"),
                api.get("/public/promotions").catch(() => []),
                api.get("/public/table-areas").catch(() => []),
                api.get("/public/tables")
            ]);
            state.categories = (Array.isArray(menu) ? menu : []).map((c) => ({ id: String(c.categoryId), name: c.categoryName }));
            state.foods = (Array.isArray(menu) ? menu : []).flatMap((c) =>
                (c.foods || []).filter((f) => (f.sizes || []).length).map((f) => ({ ...f, categoryId: String(c.categoryId) }))
            );
            state.promotions = Array.isArray(promotions) ? promotions : [];
            state.areas = Array.isArray(areas) ? areas : [];
            state.tables = Array.isArray(tables) ? tables : [];

            renderPromotions();
            renderCategories();
            renderMenu();
            renderAreas();
            renderTables();
        } catch (error) {
            notice.textContent = error.message || "Không thể tải thực đơn và danh sách bàn.";
            notice.className = "v2-notice is-error";
            notice.hidden = false;
        }
    }

    $("#categoryTabs").addEventListener("click", (event) => {
        const btn = event.target.closest("[data-category-id]");
        if (!btn) return;
        state.selectedCategory = btn.dataset.categoryId;
        renderCategories();
        renderMenu();
    });

    $("#menuSearch").addEventListener("input", (event) => {
        state.search = normalizeSearch(event.target.value);
        renderMenu();
    });

    $("#areaFilter").addEventListener("change", (event) => {
        state.selectedArea = event.target.value;
        renderTables();
    });

    renderHeaderActions();
    loadAll();
})();
