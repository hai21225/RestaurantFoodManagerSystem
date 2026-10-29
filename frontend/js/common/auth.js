(function () {
    const tokenKey = "restaurant.token";
    const userKey = "restaurant.currentUser";
    const appUrl = new URL("../../", document.currentScript.src);
    const loginUrl = new URL("pages/login.html", appUrl);
    const publicPages = [loginUrl.pathname, new URL("pages/setup.html", appUrl).pathname];
    let redirecting = false;

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
            ["restaurant.currentRole", "restaurant.selectedTable", "restaurant.lastOrder", "restaurant.posPending"].forEach((key) => storage.removeItem(key));
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
        const fallback = new URL("index.html", appUrl).href;
        const value = new URL(window.location.href).searchParams.get("returnUrl");
        if (!value) return fallback;
        try {
            const target = new URL(value, appUrl);
            if (target.origin !== appUrl.origin || !target.pathname.startsWith(appUrl.pathname)
                || publicPages.includes(target.pathname) || !target.pathname.endsWith(".html")) return fallback;
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
        hasRole: (...roles) => (getUser()?.roles || []).some((role) => roles.includes(role)),
        logout: () => { clearSession(); redirectToLogin(); }
    };

    window.addEventListener("storage", (event) => {
        if (event.key === tokenKey || event.key === userKey || event.key === null) {
            if (requireSession()) notify();
        }
    });
    window.addEventListener("pageshow", requireSession);
    requireSession();
})();
