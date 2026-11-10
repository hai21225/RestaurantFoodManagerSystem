(function () {
    const tokenKey = "restaurant.token";
    const userKey = "restaurant.currentUser";
    const appUrl = new URL("../../", document.currentScript.src);
    const loginUrl = new URL("pages/login.html", appUrl);
    const publicPages = [loginUrl.pathname, new URL("pages/setup.html", appUrl).pathname];
    let redirecting = false;
    let pageAccess = "checking";
    const pageRoles = {
        "index.html": ["Admin"],
        "pages/tables.html": ["Admin", "Waiter"],
        "pages/order.html": ["Admin", "Waiter"],
        "pages/order-tools.html": ["Admin", "Waiter", "Cashier"],
        "pages/kitchen.html": ["Admin", "Chef"],
        "pages/payment.html": ["Admin", "Cashier"],
        "pages/foods.html": ["Admin", "WarehouseManager"],
        "pages/inventory.html": ["Admin", "WarehouseManager"],
        "pages/purchases.html": ["Admin", "WarehouseManager"],
        "pages/recipes.html": ["Admin", "WarehouseManager"],
        "pages/employees.html": ["Admin"],
        "pages/roles.html": ["Admin"],
        "pages/reports.html": ["Admin"]
    };
    const catalogTabs = ["categories", "toppings", "sizes", "links"];
    const adminTabs = ["areas", "tables", "promotions", "settings"];

    function setPageAccess(state) {
        const changed = pageAccess !== state;
        pageAccess = state;
        document.documentElement?.setAttribute("data-page-access", state);
        if (changed) window.dispatchEvent(new CustomEvent("restaurant:page-access-changed"));
    }

    function resolvePage(value) {
        try {
            const target = new URL(value, appUrl);
            if (target.origin !== appUrl.origin || !target.pathname.startsWith(appUrl.pathname)) return null;
            const page = target.pathname.slice(appUrl.pathname.length) || "index.html";
            return { target, page };
        } catch (_) { return null; }
    }

    function canAccessPage(value, user = getUser()) {
        const resolved = resolvePage(value);
        if (!resolved) return false;
        const { target, page } = resolved;
        if (publicPages.includes(target.pathname)) return true;
        if (!user?.userId) return false;
        if (page === "pages/account.html") return true;
        const roles = Array.isArray(user.roles) ? user.roles : [];
        if (page === "pages/administration.html") {
            const tab = target.searchParams.get("tab") || "categories";
            if (catalogTabs.includes(tab)) return roles.includes("Admin") || roles.includes("WarehouseManager");
            return adminTabs.includes(tab) && roles.includes("Admin");
        }
        return Boolean(pageRoles[page]?.some((role) => roles.includes(role)));
    }

    function getHomeUrl() {
        const roles = Array.isArray(getUser()?.roles) ? getUser().roles : [];
        const homes = [["Admin", "index.html"], ["Waiter", "pages/tables.html"], ["Chef", "pages/kitchen.html"],
            ["Cashier", "pages/payment.html"], ["WarehouseManager", "pages/inventory.html"]];
        return new URL(homes.find(([role]) => roles.includes(role))?.[1] || "pages/account.html", appUrl).href;
    }

    function enforcePageAccess() {
        if (isPublicPage()) { setPageAccess("allowed"); return true; }
        if (!getToken() || !getUser()) { setPageAccess("denied"); redirectToLogin(); return false; }
        if (canAccessPage(window.location.href)) { setPageAccess("allowed"); return true; }
        setPageAccess("denied");
        if (!redirecting) {
            redirecting = true;
            window.sessionStorage.setItem("restaurant.accessNotice", "Bạn không có quyền mở màn hình vừa chọn. Đã đưa bạn về màn làm việc của mình.");
            window.location.replace(getHomeUrl());
        }
        return false;
    }

    function blockPageAccess(message) {
        if (isPublicPage()) return;
        setPageAccess("denied");
        if (!document.documentElement || document.getElementById("pageAccessGate")) return;
        const panel = document.createElement("section"); panel.id = "pageAccessGate"; panel.className = "page-access-gate";
        panel.setAttribute("role", "alert");
        const title = document.createElement("h1"); title.textContent = "Chưa thể mở màn hình";
        const text = document.createElement("p"); text.textContent = message || "Không xác minh được quyền truy cập. Hãy tải lại trang.";
        const retry = document.createElement("button"); retry.type = "button"; retry.textContent = "Tải lại trang";
        retry.addEventListener("click", () => window.location.reload());
        const logout = document.createElement("button"); logout.type = "button"; logout.textContent = "Đăng xuất";
        logout.addEventListener("click", () => { clearSession(); redirectToLogin(); });
        panel.append(title, text, retry, logout); document.body.appendChild(panel);
    }

    function getToken() {
        return window.localStorage.getItem(tokenKey);
    }

    function getUser() {
        if (!getToken()) return null;
        try {
            return JSON.parse(window.localStorage.getItem(userKey)) || null;
        } catch (_) {
            return null;
        }
    }

    function notify() {
        window.dispatchEvent(new CustomEvent("restaurant:auth-changed"));
    }

    function clearOrderContext() {
        [window.localStorage, window.sessionStorage].forEach((storage) => {
            ["restaurant.currentRole", "restaurant.selectedTable", "restaurant.lastOrder", "restaurant.posPending", "restaurant.kitchenPending", "restaurant.paymentPending", "restaurant.paymentMomo", "restaurant.warehousePending", "restaurant.catalogPending", "restaurant.staffPending", "restaurant.adminPending", "restaurant.accountPending", "restaurant.orderExtrasPending"].forEach((key) => storage.removeItem(key));
        });
    }

    function updateUser(data) {
        // Các trang POS/nhập hàng cũ còn đọc userId từ sessionStorage.
        const { token, ...user } = data;
        window.localStorage.setItem(userKey, JSON.stringify(user));
        window.sessionStorage.setItem(userKey, JSON.stringify(user));
        notify();
    }

    function setSession(data) {
        if (!data?.token || !data.userId) throw new Error("Máy chủ chưa trả token đăng nhập. Hãy kiểm tra cấu hình JWT.");
        clearOrderContext();
        window.localStorage.setItem(tokenKey, data.token);
        updateUser(data);
    }

    function clearSession() {
        window.localStorage.removeItem(tokenKey);
        window.localStorage.removeItem(userKey);
        window.sessionStorage.removeItem(userKey);
        clearOrderContext();
        notify();
    }

    function isPublicPage() {
        return publicPages.includes(window.location.pathname);
    }

    function redirectToLogin() {
        if (isPublicPage() || redirecting) return;
        redirecting = true;
        const target = new URL(loginUrl);
        target.searchParams.set("returnUrl", window.location.href);
        window.location.replace(target.href);
    }

    function getReturnUrl() {
        // Chỉ quay lại một trang trong ứng dụng, không chuyển tới URL bên ngoài.
        const fallback = getHomeUrl();
        const value = new URL(window.location.href).searchParams.get("returnUrl");
        if (!value) return fallback;
        try {
            const target = new URL(value, appUrl);
            if (target.origin !== appUrl.origin || !target.pathname.startsWith(appUrl.pathname)
                || publicPages.includes(target.pathname) || !canAccessPage(target.href)) return fallback;
            return target.href;
        } catch (_) {
            return fallback;
        }
    }

    function requireSession() {
        if (isPublicPage()) return true;
        if (!getToken()) {
            clearSession();
            redirectToLogin();
            return false;
        }
        // exp chỉ phục vụ UX; backend vẫn xác minh chữ ký, hạn dùng và quyền.
        try {
            const payload = getToken().split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
            const claims = JSON.parse(window.atob(payload));
            if (claims.exp && claims.exp * 1000 <= Date.now()) {
                clearSession();
                redirectToLogin();
                return false;
            }
        } catch (_) { /* Token không hợp lệ sẽ được API từ chối bằng 401. */ }
        const user = getUser();
        if (user) window.sessionStorage.setItem(userKey, JSON.stringify(user));
        return true;
    }

    window.restaurantAuth = {
        getToken, getUser, setSession, updateUser, clearSession, requireSession,
        isPublicPage, redirectToLogin, getReturnUrl,
        canAccessPage, getHomeUrl, enforcePageAccess, blockPageAccess,
        isPageAllowed: () => pageAccess === "allowed" && canAccessPage(window.location.href),
        isCheckingPageAccess: () => pageAccess === "checking",
        hasRole: (...roles) => {
            const assigned = getUser()?.roles;
            return Array.isArray(assigned) && assigned.some((role) => roles.includes(role));
        },
        logout: () => { clearSession(); redirectToLogin(); }
    };

    window.addEventListener("storage", (event) => {
        if (event.key === tokenKey || event.key === userKey || event.key === null) {
            if (requireSession()) { enforcePageAccess(); notify(); }
        }
    });
    window.addEventListener("pageshow", () => {
        if (!requireSession()) { setPageAccess("denied"); return; }
        // Back/Forward có thể phục hồi DOM cũ sau khi đã đổi tài khoản ở tab khác.
        // Lần mở đầu vẫn phải đợi /users/me, không mở nội dung bằng role trong cache.
        if (!window.restaurantAuth.isCheckingPageAccess()) enforcePageAccess();
    });
    requireSession();
    setPageAccess(isPublicPage() ? "allowed" : "checking");
})();
