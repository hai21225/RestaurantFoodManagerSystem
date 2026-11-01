(function () {
    const { WriteGuard, fail, snapshot, text } = window.restaurantAdmin;
    class AccountSession extends WriteGuard {
        constructor(options) { super({ ...options, key: "restaurant.accountPending" }); this.profile = null; this.loaded = false; this.loading = false; }
        async load() {
            this.loaded = false;
            const profile = await this.api.get("/users/me");
            if (profile?.userId !== this.auth.getUser()?.userId || !Array.isArray(profile.roles) || !profile.isActive) throw fail("Hồ sơ không còn hợp lệ. Hãy đăng nhập lại.", 401);
            this.profile = profile; this.auth.updateUser(profile); this.loaded = true;
        }
        async refresh() {
            if (this.busy || this.loading) return;
            this.loading = true; try { await this.load(); } finally { this.loading = false; }
        }
        async update(data, expected) {
            if (!this.loaded || this.loading) throw fail("Chờ tải xong hồ sơ.");
            const body = { fullName: text(data.fullName, 150, true), email: text(data.email, 150, true), phone: text(data.phone, 20) };
            if (!/^[^\s@<>]+@[^\s@<>]+$/.test(body.email)) throw fail("Email không hợp lệ.", 400);
            return this.write("Cập nhật hồ sơ cá nhân", async () => { await this.load(); if (snapshot(this.profile) !== expected) throw fail("Hồ sơ đã đổi. Tải lại và nhập thay đổi mới."); }, () => this.api.put("/users/me", body), () => this.load());
        }
        async changePassword(data) {
            if (!this.loaded || this.loading) throw fail("Chờ tải xong hồ sơ.");
            // Chỉ giữ mật khẩu trong phạm vi lệnh hiện tại, không đưa vào pending/storage/log.
            const body = { currentPassword: String(data.currentPassword || ""), newPassword: String(data.newPassword || ""), confirmNewPassword: String(data.confirmNewPassword || "") };
            const bytes = new TextEncoder().encode(body.newPassword).length;
            if (!body.currentPassword || !body.newPassword.trim() || bytes < 6 || bytes > 72 || body.newPassword !== body.confirmNewPassword) throw fail("Nhập mật khẩu hiện tại; mật khẩu mới từ 6–72 byte UTF-8 và xác nhận phải khớp.", 400);
            return this.write("Đổi mật khẩu: đăng nhập lại bằng mật khẩu mới để đối chiếu nếu mất phản hồi", async () => { if (!this.auth.getToken()) throw fail("Hãy đăng nhập lại.", 401); }, () => this.api.post("/auth/change-password", body), async () => {});
        }
    }
    window.restaurantAccount = { AccountSession };
})();
