(function () {
    const auth = window.restaurantAuth;
    const session = new window.restaurantAccount.AccountSession({ api: window.restaurantApi, auth, storage: window.sessionStorage });
    const $ = (id) => document.getElementById(id);
    let expected = null;
    const locked = () => session.busy || session.loading || !session.loaded || !!session.pending;
    function message(value, error = false) { $("accountNotice").textContent = value; $("accountNotice").classList.toggle("is-error", error); }
    function render(fill = false) {
        const p = session.profile;
        $("accountName").textContent = p?.fullName || "Tài khoản cá nhân";
        $("accountIdentity").textContent = p ? `#${p.userId} · ${p.userName}` : "Chờ tải hồ sơ";
        $("accountRoles").textContent = p?.roles?.map((r) => ({ Admin: "Quản trị", Waiter: "Phục vụ", Chef: "Bếp", Cashier: "Thu ngân", WarehouseManager: "Quản lý kho" })[r] || r).join(", ") || "—";
        if (fill && p) { for (const name of ["fullName", "email", "phone"]) $("profileForm").elements[name].value = p[name] || ""; expected = window.restaurantAdmin.snapshot(p); }
        for (const form of [$("profileForm"), $("passwordForm")]) form.querySelectorAll("input,button").forEach((el) => { el.disabled = locked(); });
        $("accountRefresh").disabled = session.busy || session.loading;
        $("accountPending").hidden = !session.pending;
        $("accountPendingText").textContent = session.pending ? `${session.pending.description}. Không gửi lại yêu cầu. Nếu vừa đổi mật khẩu, đăng xuất và kiểm tra đăng nhập bằng mật khẩu mới.` : "";
        $("accountReview").disabled = session.busy || session.loading; $("accountAck").hidden = !session.pending?.reviewed;
    }
    async function run(action, success, fill = false) {
        try { const p = action(); render(); await p; message(success || ""); render(fill); }
        catch (e) { message(e.message, true); render(); }
    }
    $("accountRefresh").addEventListener("click", () => run(() => session.refresh(), "Đã tải hồ sơ mới nhất.", true));
    $("profileForm").addEventListener("submit", (e) => { e.preventDefault(); if (!locked()) run(() => session.update(Object.fromEntries(new FormData(e.currentTarget)), expected), "Đã lưu hồ sơ và cập nhật tên trên giao diện.", true); });
    $("passwordForm").addEventListener("submit", async (e) => {
        e.preventDefault(); if (locked()) return;
        const data = Object.fromEntries(new FormData(e.currentTarget));
        try { const p = session.changePassword(data); render(); await p; message("Đã đổi mật khẩu. Hãy đăng xuất rồi đăng nhập bằng mật khẩu mới. Các phiên khác hiện chưa được hệ thống thu hồi ngay."); }
        catch (error) { message(error.message, true); }
        finally { $("passwordForm").reset(); render(); }
    });
    $("accountReview").addEventListener("click", () => run(() => session.review(() => session.load()), "Đã tải hồ sơ để đối chiếu. Kết quả đổi mật khẩu phải kiểm tra bằng lần đăng nhập mới.", true));
    $("accountAck").addEventListener("click", () => { try { session.acknowledge(); render(); message("Đã xác nhận đối chiếu. Không tự gửi lại thao tác cũ."); } catch (e) { message(e.message, true); } });
    $("accountLogout").addEventListener("click", auth.logout);
    auth.ready.then(() => run(() => session.refresh(), "", true));
})();
