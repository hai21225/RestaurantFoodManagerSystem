(function () {
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
