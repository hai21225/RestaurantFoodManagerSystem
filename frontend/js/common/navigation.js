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

    toggle.addEventListener("click", () => {
        const open = group.classList.toggle("is-open");
        toggle.setAttribute("aria-expanded", String(open));
    });
})();
