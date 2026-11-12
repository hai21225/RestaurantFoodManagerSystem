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
            if (auth.isPageAllowed()) {
                const notice = window.sessionStorage.getItem("restaurant.accessNotice");
                if (notice) { sessionMessage.textContent = notice; window.sessionStorage.removeItem("restaurant.accessNotice"); }
            }
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
        navigation.appendChild(item);
    });
    if (/\/order-tools\.html$/.test(location.pathname)) navigation.querySelector('[data-page="order.html"]')?.classList.add("is-active");
    const items = Array.from(navigation.querySelectorAll(":scope > .nav-item"));
    const managementItems = items.slice(6);
    items.forEach((item) => { if (item.classList.contains("is-active")) item.setAttribute("aria-current", "page"); });
    // Nút Tổng quan đang chọn trong HTML cũ chưa có URL.
    if (items[0] && !items[0].dataset.page) items[0].dataset.page = new URL("../../index.html", document.currentScript.src).href;
    // Giữ phiên bản HTML mới khi điều hướng từ trang đã được trình duyệt lưu đệm.
    items.forEach((item) => {
        if (!item.dataset.page) return;
        const target = new URL(item.dataset.page, location.href);
        if (/\/(index|pages\/(tables|order|order-tools|kitchen|payment|foods|inventory|purchases|recipes|administration|employees|roles|account|reports))\.html$/.test(target.pathname)) {
            target.searchParams.set("ui", "20261112-print1");
            item.dataset.page = target.href;
        }
    });

    const mobile = window.matchMedia("(max-width: 760px)");
    const groups = [];

    function setOpen(group, open) {
        open = !group.root.hidden && (group.single || open);
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
        const group = { root, toggle, submenu, entries, alwaysDesktop, title, name }; groups.push(group);
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
        groups.forEach((group) => {
            const visible = group.entries.filter((item) => !item.hidden);
            const active = visible.find((item) => item.classList.contains("is-active"));
            const personalOnly = visible.length === 1 && new URL(visible[0].dataset.page, location.href).pathname.endsWith("/account.html");
            const title = personalOnly ? "Tài khoản" : auth?.hasRole("Admin") ? group.title : group.alwaysDesktop ? "Công việc" : "Quản lý kho";
            // Thanh dưới trên điện thoại cho biết màn đang mở; popup vẫn chứa cả nhóm.
            group.name.textContent = mobile.matches && active ? active.textContent.trim() : title;
            group.submenu.setAttribute("aria-label", `Các mục ${title}`);
            group.toggle.classList.toggle("is-active", Boolean(active));
            setOpen(group, !mobile.matches && (group.alwaysDesktop || Boolean(active)));
        });
    }
    function renderNavigation() {
        items.forEach((item) => {
            item.hidden = !auth?.isPageAllowed() || !auth.canAccessPage(new URL(item.dataset.page, location.href).href);
        });
        groups.forEach((group) => {
            const visible = group.entries.filter((item) => !item.hidden);
            group.root.hidden = !visible.length;
            group.single = visible.length === 1;
            group.root.classList.toggle("nav-group-single", group.single);
            group.toggle.hidden = group.single;
        });
        navigation.classList.toggle("nav-one-group", groups.filter((group) => !group.root.hidden).length === 1);
        const visibleCount = items.filter((item) => !item.hidden).length;
        navigation.classList.toggle("nav-service-direct", Boolean(document.body?.classList.contains("service-workspace"))
            && auth?.isPageAllowed() && visibleCount >= 2 && visibleCount <= 4);
        applyLayout();
    }
    renderNavigation();
    auth?.ready?.then(() => { renderNavigation(); if (operator && auth) renderOperatorAfterReady(); });
    function renderOperatorAfterReady() {
        const notice = window.sessionStorage.getItem("restaurant.accessNotice");
        if (notice && auth.isPageAllowed()) {
            operator.querySelector(".operator-session-message").textContent = notice;
            window.sessionStorage.removeItem("restaurant.accessNotice");
        }
    }
    window.addEventListener("restaurant:auth-changed", renderNavigation);
    // Bàn/Bếp cũ nạp navigation trước api.js, khi đó auth.ready chưa được tạo.
    // Luôn cập nhật sau lúc bootstrap thực sự chốt quyền, kể cả thứ tự script cũ.
    window.addEventListener("restaurant:page-access-changed", () => {
        renderNavigation();
        if (operator && auth) renderOperatorAfterReady();
    });
    mobile.addEventListener("change", applyLayout);
    window.addEventListener("restaurant:admin-module-changed", applyLayout);

    navigation.addEventListener("click", (event) => {
        const item = event.target.closest(".nav-item[data-page]");
        if (!item || !navigation.contains(item) || item.hidden || item.disabled || !auth?.canAccessPage(new URL(item.dataset.page, location.href).href)) return;
        closeMenus();
        const targetUrl = new URL(item.dataset.page, window.location.href).href;
        if (targetUrl !== window.location.href) window.location.href = targetUrl;
    });

    document.addEventListener("click", (event) => {
        if (mobile.matches && !groups.some((group) => group.root.contains(event.target))) closeMenus();
    });

    document.addEventListener("keydown", (event) => {
        if (event.key !== "Escape") return;
        const open = groups.find((group) => !group.single && group.root.classList.contains("is-open") && (mobile.matches || !group.alwaysDesktop));
        if (open) { closeMenus(); open.toggle.focus(); }
    });
})();
