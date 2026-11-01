(function () {
    const pendingKey = "restaurant.staffPending";
    const fail = (message, status = 409) => Object.assign(new Error(message), { status });
    const roleLabels = { Admin: "Quản trị", Waiter: "Phục vụ", Chef: "Bếp", Cashier: "Thu ngân", WarehouseManager: "Quản lý kho" };
    const kinds = ["create", "update", "active", "assignRole", "removeRole", "resetPassword"];
    const validId = (id) => ["number", "string"].includes(typeof id) && Number.isInteger(Number(id)) && Number(id) > 0 && Number(id) <= 2147483647;
    const snapshot = (user) => JSON.stringify([user.userId, user.userName, user.fullName, user.email, user.phone, user.isActive, [...user.roles].sort(), user.updatedAt || null]);
    function contactBody(data) {
        const fullName = String(data.fullName || "").trim(), email = String(data.email || "").trim(), phone = String(data.phone || "").trim();
        if (!fullName || fullName.length > 150) throw fail("Họ tên từ 1–150 ký tự.", 400);
        if (email.length > 150 || !/^[^\s@<>]+@[^\s@<>]+$/.test(email)) throw fail("Nhập email hợp lệ, tối đa 150 ký tự.", 400);
        if (phone.length > 20) throw fail("Số điện thoại tối đa 20 ký tự.", 400);
        return { fullName, email, phone };
    }
    function passwordBody(data) {
        const newPassword = String(data.newPassword || ""), confirmPassword = String(data.confirmPassword || "");
        const bytes = new TextEncoder().encode(newPassword).length;
        if (!newPassword.trim() || bytes < 6 || bytes > 72) throw fail("Mật khẩu từ 6–72 byte UTF-8; chữ tiếng Việt có thể chiếm nhiều byte.", 400);
        if (newPassword !== confirmPassword) throw fail("Mật khẩu xác nhận không khớp.", 400);
        return { newPassword, confirmPassword };
    }
    const checkUsers = (users) => Array.isArray(users) && users.every((u) => u && validId(u.userId) && typeof u.userName === "string" && typeof u.isActive === "boolean" && Array.isArray(u.roles) && u.roles.every((r) => typeof r === "string"));
    class StaffSession {
        constructor({ api, auth, storage }) {
            this.api = api; this.auth = auth; this.storage = storage;
            this.users = []; this.roles = []; this.detail = null; this.detailVersion = 0;
            this.loading = false; this.detailLoading = false; this.loaded = false; this.busy = false;
            this.pending = null; this.needsLogin = false;
            try {
                const saved = JSON.parse(storage.getItem(pendingKey));
                if (saved?.userId === auth.getUser()?.userId && kinds.includes(saved.kind)) this.pending = { ...saved, reviewed: false };
                else if (saved) storage.removeItem(pendingKey);
            } catch (_) { storage.removeItem(pendingKey); }
        }
        hasAccess() { return this.auth.hasRole("Admin"); }
        assertAccess() { if (!this.hasAccess()) throw fail("Chỉ Quản trị viên được quản lý nhân viên và vai trò.", 403); }
        async loadRoles() {
            this.assertAccess();
            const roles = await this.api.get("/roles");
            if (!Array.isArray(roles) || roles.some((r) => !validId(r.roleId) || typeof r.roleName !== "string" || !r.roleName.trim()) || new Set(roles.map((r) => r.roleName)).size !== roles.length) throw fail("Không đọc được đầy đủ danh sách vai trò.", 502);
            return roles;
        }
        async load() {
            this.assertAccess(); this.loaded = false;
            const [users, roles] = await Promise.all([this.api.get("/users"), this.loadRoles()]);
            if (!checkUsers(users)) throw fail("Không đọc được đầy đủ danh sách nhân viên.", 502);
            this.users = users; this.roles = roles; this.loaded = true;
        }
        async refresh() {
            this.assertAccess(); if (this.busy || this.loading) return;
            this.loading = true;
            try { await this.load(); this.detail = null; ++this.detailVersion; }
            finally { this.loading = false; }
        }
        async userDetails(id) {
            this.assertAccess(); if (!validId(id)) throw fail("Mã nhân viên không hợp lệ.", 400);
            const user = await this.api.get(`/users/${Number(id)}`);
            if (!checkUsers([user]) || user.userId !== Number(id)) throw fail("Chi tiết nhân viên chưa đầy đủ.", 502);
            return user;
        }
        async selectUser(id) {
            if (this.busy || this.loading) return null;
            const version = ++this.detailVersion; this.detailLoading = true; this.detail = null;
            try {
                const user = await this.userDetails(id);
                if (version !== this.detailVersion) return null;
                this.detail = user; return user;
            } catch (error) { if (version === this.detailVersion) throw error; return null; }
            finally { if (version === this.detailVersion) this.detailLoading = false; }
        }
        otherAdmin(id) { return this.users.some((u) => u.userId !== id && u.isActive && u.roles.includes("Admin")); }
        lockReason(user) {
            if (!user.isActive) return "";
            if (user.userId === this.auth.getUser()?.userId) return "Không thể tự khóa tài khoản đang sử dụng.";
            if (user.roles.includes("Admin") && !this.otherAdmin(user.userId)) return "Không thể khóa Admin đang hoạt động cuối cùng.";
            return "";
        }
        roleRemovalReason(user, roleName) {
            return roleName === "Admin" && !this.otherAdmin(user.userId) ? "Không thể thu hồi vai trò Admin cuối cùng đang hoạt động." : "";
        }
        savePending() { this.storage.setItem(pendingKey, JSON.stringify(this.pending)); }
        clearPending() { this.pending = null; this.storage.removeItem(pendingKey); }
        isOwnAccessChange(intent) { return intent?.id === this.auth.getUser()?.userId && ["assignRole", "removeRole", "resetPassword"].includes(intent.kind); }
        async command(kind, id = null, data = {}, expected = null) {
            this.assertAccess();
            if (this.busy || this.loading || this.detailLoading || this.pending || !this.loaded) throw fail("Chờ tải dữ liệu hoặc đối chiếu thao tác trước khi tiếp tục.");
            if (!kinds.includes(kind) || (kind === "create" ? id !== null : !validId(id))) throw fail("Thao tác hoặc mã nhân viên không hợp lệ.", 400);
            if (id !== null) id = Number(id);
            const actorId = this.auth.getUser()?.userId;
            this.busy = true; this.needsLogin = false;
            try {
                await this.load();
                const user = id ? await this.userDetails(id) : null;
                if (this.auth.getUser()?.userId !== actorId || !this.hasAccess()) throw fail("Phiên đăng nhập đã đổi. Tải lại trang trước khi thao tác.", 403);
                if (expected && expected !== snapshot(user)) throw fail("Nhân viên vừa thay đổi. Mở lại thông tin trước khi thao tác.");
                let path = id ? `/users/${id}` : "/users", method = "post", body;
                const roleName = String(data.roleName || "").trim();
                if (["create", "assignRole", "removeRole"].includes(kind) && roleName && !this.roles.some((r) => r.roleName === roleName)) throw fail("Chọn vai trò trong danh sách hệ thống.", 400);
                if (kind === "create") {
                    const userName = String(data.userName || "").trim();
                    if (!userName || userName.length > 100) throw fail("Tên đăng nhập từ 1–100 ký tự.", 400);
                    const credentials = passwordBody({ newPassword: data.password, confirmPassword: data.confirmPassword });
                    body = { ...contactBody(data), userName, password: credentials.newPassword, confirmPassword: credentials.confirmPassword, roleName: roleName || null };
                } else if (kind === "update") { method = "put"; body = contactBody(data); }
                else if (kind === "active") {
                    if (typeof data.isActive !== "boolean") throw fail("Trạng thái tài khoản không hợp lệ.", 400);
                    const reason = data.isActive ? "" : this.lockReason(user); if (reason) throw fail(reason);
                    path += `/active?active=${data.isActive}`; method = "patch";
                } else if (["assignRole", "removeRole"].includes(kind)) {
                    if (!roleName) throw fail("Chọn vai trò cần thao tác.", 400);
                    if (kind === "assignRole") {
                        if (user.roles.includes(roleName)) throw fail("Nhân viên đã có vai trò này.");
                        path += "/roles"; body = { roleName };
                    } else {
                        if (!user.roles.includes(roleName)) throw fail("Nhân viên không còn vai trò này.");
                        const reason = this.roleRemovalReason(user, roleName); if (reason) throw fail(reason);
                        path += `/roles/${encodeURIComponent(roleName)}`; method = "delete";
                    }
                } else { path += "/reset-password"; body = passwordBody(data); }
                // Chỉ lưu dấu theo dõi; tuyệt đối không lưu mật khẩu hoặc body chứa mật khẩu.
                this.pending = { kind, id, userId: this.auth.getUser().userId, targetName: user?.userName || body.userName, roleName: roleName || null, accepted: false, reviewed: false };
                try { this.savePending(); } catch (_) { this.pending = null; throw fail("Không lưu được dấu theo dõi thao tác. Kiểm tra bộ nhớ trình duyệt.", 400); }
                const result = method === "delete" ? await this.api.delete(path) : await this.api[method](path, body);
                this.pending.accepted = true; this.savePending();
                await this.load(); this.detail = null; ++this.detailVersion;
                this.needsLogin = this.isOwnAccessChange(this.pending); this.clearPending();
                return result;
            } catch (error) {
                if (this.pending && !this.pending.accepted && error.status >= 400 && error.status < 500) this.clearPending();
                this.loaded = false; throw error;
            } finally { this.busy = false; }
        }
        async reviewPending() {
            this.assertAccess(); if (!this.pending || this.busy || this.loading) return;
            if (this.pending.userId !== this.auth.getUser()?.userId) throw fail("Yêu cầu thuộc phiên đăng nhập khác.", 403);
            this.busy = true;
            try {
                await this.load(); this.detail = null; ++this.detailVersion;
                if (this.pending.accepted) { this.needsLogin = this.isOwnAccessChange(this.pending); this.clearPending(); return true; }
                this.pending.reviewed = true; this.savePending(); return false;
            } finally { this.busy = false; }
        }
        acknowledgeReview() {
            this.assertAccess();
            if (this.pending?.userId !== this.auth.getUser()?.userId) throw fail("Yêu cầu thuộc phiên đăng nhập khác.", 403);
            if (!this.loaded || !this.pending?.reviewed || this.busy || this.loading) throw fail("Tải dữ liệu mới và đối chiếu trước khi mở khóa.");
            this.needsLogin = this.isOwnAccessChange(this.pending); this.clearPending();
        }
    }
    window.restaurantStaff = { StaffSession, contactBody, passwordBody, snapshot, roleLabels, pendingKey };
})();
