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

    const items = Array.from(navigation.querySelectorAll(":scope > .nav-item"));
    const managementItems = items.slice(6);

    if (!managementItems.length) {
        return;
    }

    const group = document.createElement("div");
    const toggle = document.createElement("button");
    const submenu = document.createElement("div");
    const hasActiveItem = managementItems.some((item) => item.classList.contains("is-active"));
    const openByDefault = hasActiveItem && !window.matchMedia("(max-width: 760px)").matches;

    group.className = `nav-group${openByDefault ? " is-open" : ""}`;
    toggle.className = "nav-group-toggle";
    toggle.type = "button";
    toggle.setAttribute("aria-expanded", String(openByDefault));
    toggle.innerHTML = '<span>Quản trị</span><span class="nav-group-chevron" aria-hidden="true">⌄</span>';
    submenu.className = "nav-submenu";

    managementItems.forEach((item) => submenu.appendChild(item));
    group.append(toggle, submenu);
    navigation.appendChild(group);

    function closeManagementMenu() {
        group.classList.remove("is-open");
        toggle.setAttribute("aria-expanded", "false");
    }

    toggle.addEventListener("click", () => {
        const open = group.classList.toggle("is-open");
        toggle.setAttribute("aria-expanded", String(open));
    });

    navigation.addEventListener("click", (event) => {
        const item = event.target.closest(".nav-item[data-page]");
        if (!item || !navigation.contains(item)) {
            return;
        }

        const targetPage = item.dataset.page;
        if (!targetPage) {
            return;
        }

        closeManagementMenu();
        const targetUrl = new URL(targetPage, window.location.href).href;
        if (targetUrl !== window.location.href) {
            window.location.href = targetUrl;
        }
    });

    document.addEventListener("click", (event) => {
        if (window.matchMedia("(max-width: 760px)").matches && !group.contains(event.target)) {
            closeManagementMenu();
        }
    });

    document.addEventListener("keydown", (event) => {
        if (event.key === "Escape" && group.classList.contains("is-open")) {
            closeManagementMenu();
            toggle.focus();
        }
    });
})();
