(function () {
    const auth = window.restaurantAuth;
    const operator = document.querySelector(".operator");
    if (operator && auth) {
        const logout = document.createElement("button");
        logout.type = "button";
        logout.className = "secondary-action operator-logout";
        logout.textContent = "Đăng xuất";
        logout.addEventListener("click", auth.logout);
        operator.appendChild(logout);
        const sessionMessage = document.createElement("span");
        sessionMessage.className = "operator-session-message";
        sessionMessage.setAttribute("role", "status");
        operator.appendChild(sessionMessage);
        function renderOperator() {
            const user = auth.getUser();
            const roleNames = { Admin: "Quản trị", Waiter: "Phục vụ", Chef: "Bếp", Cashier: "Thu ngân", WarehouseManager: "Quản lý kho" };
            operator.querySelector(".operator-role").textContent = (user?.roles || []).map((role) => roleNames[role] || role).join(", ") || "Chưa có vai trò";
            operator.querySelector("strong").textContent = user?.fullName || user?.userName || "Chưa đăng nhập";
            logout.hidden = !user;
            sessionMessage.textContent = "";
        }
        window.addEventListener("restaurant:auth-changed", renderOperator);
        window.addEventListener("restaurant:auth-error", (event) => { sessionMessage.textContent = event.detail; });
        renderOperator();
    }
    const navigation = document.querySelector(".main-nav");

    if (!navigation) {
        return;
    }

    const catalogUrl = new URL("../../pages/recipes.html", document.currentScript.src);
    const catalogItem = document.createElement("button");
    catalogItem.type = "button"; catalogItem.className = "nav-item nav-catalog";
    catalogItem.dataset.page = catalogUrl.href; catalogItem.textContent = "Nguyên liệu / Công thức";
    if (window.location.pathname === catalogUrl.pathname) catalogItem.classList.add("is-active");
    const inventoryItem = navigation.querySelector('[data-page$="inventory.html"]');
    if (inventoryItem) inventoryItem.after(catalogItem); else navigation.appendChild(catalogItem);
    const renderCatalogEntry = () => { catalogItem.hidden = !auth?.hasRole("WarehouseManager", "Admin"); };
    renderCatalogEntry(); auth?.ready?.then(renderCatalogEntry);
    window.addEventListener("restaurant:auth-changed", renderCatalogEntry);

    const adminModules = [
        { tab: "categories", label: "Danh mục / Topping", modules: ["categories", "toppings", "sizes", "links"], roles: ["Admin", "WarehouseManager"] },
        { tab: "areas", label: "Khu vực / Bàn", modules: ["areas", "tables"], roles: ["Admin"] },
        { tab: "promotions", label: "Khuyến mãi", modules: ["promotions"], roles: ["Admin"] },
        { tab: "settings", label: "Thiết lập quán", modules: ["settings"], roles: ["Admin"] }
    ];
    adminModules.forEach((entry) => {
        const target = new URL("../../pages/administration.html", document.currentScript.src);
        target.searchParams.set("tab", entry.tab);
        const item = document.createElement("button"); item.type = "button"; item.className = "nav-item";
        item.dataset.page = target.href; item.textContent = entry.label;
        const selected = new URL(location.href).searchParams.get("tab") || "categories";
        if (location.pathname === target.pathname && entry.modules.includes(selected)) item.classList.add("is-active");
        window.addEventListener("restaurant:admin-module-changed", () => {
            const active = location.pathname === target.pathname && entry.modules.includes(new URL(location.href).searchParams.get("tab") || "categories");
            item.classList.toggle("is-active", active);
            if (active) item.setAttribute("aria-current", "page"); else item.removeAttribute("aria-current");
        });
        const visible = () => { item.hidden = !auth?.hasRole(...entry.roles); };
        visible(); auth?.ready?.then(visible); window.addEventListener("restaurant:auth-changed", visible);
        navigation.appendChild(item);
    });
    if (/\/order-tools\.html$/.test(location.pathname)) navigation.querySelector('[data-page="order.html"]')?.classList.add("is-active");
    const items = Array.from(navigation.querySelectorAll(":scope > .nav-item"));
    const reportsItem = items.find((item) => /(?:^|\/)reports\.html$/.test(item.dataset.page || ""));
    const managementItems = items.slice(6);

    // Nhân viên/vai trò theo đúng policy AdminOnly, không hiện nút quản trị cho vai trò khác.
    const staffItems = items.filter((item) => /(?:^|\/)(?:employees|roles)\.html$/.test(item.dataset.page || ""));
    const renderStaffEntries = () => staffItems.forEach((item) => { item.hidden = !auth?.hasRole("Admin"); });
    renderStaffEntries(); auth?.ready?.then(renderStaffEntries);
    window.addEventListener("restaurant:auth-changed", renderStaffEntries);

    // Báo cáo thuộc nhóm Quản trị, chỉ hiển thị cho Admin.
    if (reportsItem) {
        const renderReportsEntry = () => { reportsItem.hidden = !auth?.hasRole("Admin"); };
        renderReportsEntry();
        auth?.ready?.then(renderReportsEntry);
        window.addEventListener("restaurant:auth-changed", renderReportsEntry);
    }
    items.forEach((item) => { if (item.classList.contains("is-active")) item.setAttribute("aria-current", "page"); });

    const mobile = window.matchMedia("(max-width: 760px)");
    const groups = [];
    // Trang chủ trước đây không có data-page; vẫn phải chọn được và đóng popup.
    if (items[0] && !items[0].dataset.page) items[0].dataset.page = new URL("../../index.html", document.currentScript.src).href;

    function setOpen(group, open) {
        group.root.classList.toggle("is-open", open);
        group.toggle.setAttribute("aria-expanded", String(open));
        group.submenu.hidden = !open;
    }

    function closeMenus() {
        groups.forEach((group) => setOpen(group, !mobile.matches && group.alwaysDesktop));
    }

    function createGroup(title, id, entries, alwaysDesktop = false) {
        const root = document.createElement("div"), toggle = document.createElement("button"), submenu = document.createElement("div");
        root.className = `nav-group${alwaysDesktop ? " nav-group-overview" : ""}`;
        toggle.className = "nav-group-toggle";
        toggle.classList.toggle("is-active", entries.some((item) => item.classList.contains("is-active")));
        toggle.type = "button";
        toggle.id = `${id}-toggle`;
        toggle.setAttribute("aria-controls", `${id}-items`);
        toggle.setAttribute("aria-expanded", "false");
        const name = document.createElement("span"), chevron = document.createElement("span");
        name.textContent = title;
        chevron.className = "nav-group-chevron"; chevron.textContent = "⌄"; chevron.setAttribute("aria-hidden", "true");
        toggle.append(name, chevron);
        submenu.id = `${id}-items`; submenu.className = "nav-submenu"; submenu.hidden = true;
        submenu.setAttribute("role", "group"); submenu.setAttribute("aria-label", `Các mục ${title}`);
        entries.forEach((item) => submenu.appendChild(item));
        root.append(toggle, submenu); navigation.appendChild(root);
        const group = { root, toggle, submenu, entries, alwaysDesktop }; groups.push(group);
        toggle.addEventListener("click", () => {
            if (!mobile.matches && alwaysDesktop) return;
            const open = !root.classList.contains("is-open");
            if (mobile.matches) closeMenus();
            setOpen(group, open);
        });
    }

    createGroup("Tổng quan", "nav-overview", items.slice(0, 6), true);
    if (managementItems.length) createGroup("Quản trị", "nav-management", managementItems);

    function applyLayout() {
        // Đổi kích thước/xoay màn hình: không để hai popup chồng lên nhau hoặc giữ menu bị ẩn.
        groups.forEach((group) => setOpen(group, !mobile.matches && (group.alwaysDesktop || group.entries.some((item) => item.classList.contains("is-active")))));
    }
    applyLayout();
    mobile.addEventListener("change", applyLayout);

    navigation.addEventListener("click", (event) => {
        const item = event.target.closest(".nav-item[data-page]");
        if (!item || !navigation.contains(item) || item.hidden || item.disabled) return;
        closeMenus();
        const targetUrl = new URL(item.dataset.page, window.location.href).href;
        if (targetUrl !== window.location.href) window.location.href = targetUrl;
    });

    document.addEventListener("click", (event) => {
        if (mobile.matches && !groups.some((group) => group.root.contains(event.target))) closeMenus();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") return;
        const open = groups.find((group) => group.root.classList.contains("is-open") && (mobile.matches || !group.alwaysDesktop));
        if (open) { closeMenus(); open.toggle.focus(); }
    });
})();
