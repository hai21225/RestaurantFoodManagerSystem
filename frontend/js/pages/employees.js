(function () {
    const { StaffSession, snapshot, roleLabels } = window.restaurantStaff;
    const auth = window.restaurantAuth, session = new StaffSession({ api: window.restaurantApi, auth, storage: window.sessionStorage });
    const $ = (id) => document.getElementById(id);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const label = (role) => roleLabels[role] || role;
    const roleOptions = (roles) => roles.map((r) => `<option value="${esc(r.roleName)}">${esc(label(r.roleName))} · ${esc(r.roleName)}</option>`).join("");
    const locked = () => session.busy || session.loading || session.detailLoading || !!session.pending || !session.loaded;
    let selectedId = null, editing = null, passwordTarget = null, confirmAction = null;
    const requestedRole = new URLSearchParams(location.search).get("role");
    const detailMobile = window.matchMedia("(max-width: 1100px)");
    function placeDetail(open = false) {
        const panel = $("employeeDetailPanel"), sheet = $("employeeDetailSheet");
        if (detailMobile.matches) {
            if (panel.parentElement !== sheet) sheet.appendChild(panel);
            if (open && !sheet.open) sheet.showModal();
        } else {
            if (sheet.open) sheet.close();
            $("employeeDetailRail").appendChild(panel);
        }
    }
    function closeDetail() {
        if (session.busy) return;
        $("employeeDetailSheet").close();
        const selected = $("employeeList").querySelector(`[data-view-user="${selectedId}"]`);
        selected?.focus();
    }
    $("employeeDetailClose").addEventListener("click", closeDetail);
    detailMobile.addEventListener("change", () => placeDetail());
    placeDetail();
    function message(text = "", error = false, id = "staffNotice") { $(id).textContent = text; $(id).classList.toggle("is-error", error); }
    function clearPasswords() { for (const id of ["employeePassword", "employeeConfirmPassword", "staffNewPassword", "staffConfirmPassword"]) $(id).value = ""; }
    function date(value) {
        if (!value) return "Chưa có";
        const parsed = new Date(/Z$|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`);
        return Number.isFinite(parsed.getTime()) ? parsed.toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" }) : "Chưa rõ";
    }
    function renderDetail() {
        const user = session.detail;
        if (session.detailLoading) { $("employeeDetail").innerHTML = '<p class="staff-empty">Đang tải thông tin…</p>'; return; }
        if (!user) { $("employeeDetail").innerHTML = '<p class="staff-empty">Chọn Xem / Quản lý để sửa hồ sơ, khóa tài khoản hoặc gán vai trò.</p>'; return; }
        const reason = session.lockReason(user), available = session.roles.filter((r) => !user.roles.includes(r.roleName));
        $("employeeDetail").innerHTML = `<div class="staff-actions"><h3>${esc(user.fullName || user.userName)}</h3><button class="secondary-action" data-staff-back>Về danh sách</button></div><p class="staff-caption">@${esc(user.userName)} · #${user.userId}${user.userId === auth.getUser()?.userId ? " · Tài khoản của bạn" : ""}</p>
            <dl><dt>Email</dt><dd>${esc(user.email)}</dd><dt>Điện thoại</dt><dd>${esc(user.phone || "Chưa có")}</dd><dt>Trạng thái</dt><dd>${user.isActive ? "Đang hoạt động" : "Tạm khóa"}</dd><dt>Tạo lúc</dt><dd>${esc(date(user.createdAt))}</dd><dt>Cập nhật</dt><dd>${esc(date(user.updatedAt))}</dd></dl>
            <div class="staff-actions"><button class="primary-action" data-staff-edit ${locked() ? "disabled" : ""}>Sửa thông tin</button><button class="secondary-action" data-staff-active ${locked() || reason ? "disabled" : ""}>${user.isActive ? "Khóa tài khoản" : "Mở khóa tài khoản"}</button><button class="secondary-action" data-staff-password ${locked() ? "disabled" : ""}>Đặt lại mật khẩu</button></div>
            ${reason ? `<p class="staff-caption">${esc(reason)}</p>` : ""}<div class="staff-role-section"><h3>Vai trò đã gán</h3><ul class="staff-role-list">${user.roles.map((role) => {
                const reason = session.roleRemovalReason(user, role);
                return `<li><div><strong>${esc(label(role))}</strong><small>${esc(role)}</small>${reason ? `<p class="staff-caption">${esc(reason)}</p>` : ""}</div><button class="secondary-action" data-staff-remove-role="${esc(role)}" aria-label="Thu hồi ${esc(label(role))}" ${locked() || reason ? "disabled" : ""}>Thu hồi</button></li>`;
            }).join("") || '<li>Chưa gán vai trò.</li>'}</ul>
            ${available.length ? `<form id="staffAssignForm" class="staff-assign"><label class="form-field"><span>Gán thêm vai trò</span><select id="staffAssignRole" required><option value="">Chọn vai trò</option>${roleOptions(available)}</select></label><button class="primary-action" type="submit" ${locked() ? "disabled" : ""}>Gán vai trò</button></form>` : '<p class="staff-caption">Nhân viên đã có đủ các vai trò hệ thống.</p>'}
            <p class="staff-caption">Gán/thu hồi một vai trò giữ nguyên các vai trò khác. Thay đổi vai trò của bạn sẽ yêu cầu đăng nhập lại.</p></div>`;
    }
    function render() {
        const allowed = session.hasAccess(); $("staffContent").hidden = !allowed; $("staffAccess").hidden = allowed; if (!allowed) return;
        $("staffRefresh").disabled = session.busy || session.loading || session.detailLoading;
        $("employeeDetailClose").disabled = session.busy;
        $("addEmployeeButton").disabled = locked(); $("staffPending").hidden = !session.pending;
        if (session.pending) {
            const names = { create: "Tạo nhân viên", update: "Sửa hồ sơ", active: "Khóa/mở", assignRole: "Gán vai trò", removeRole: "Thu vai trò", resetPassword: "Đặt lại mật khẩu" };
            $("staffPendingSummary").textContent = `${names[session.pending.kind]} · @${session.pending.targetName || "chưa rõ"}${session.pending.roleName ? ` · ${label(session.pending.roleName)}` : ""} · ${session.pending.accepted ? "Đã nhận phản hồi thành công, cần tải lại." : "Chưa rõ kết quả ghi."}`;
        }
        $("staffReview").disabled = session.busy || session.loading; $("staffAcknowledge").hidden = !session.pending?.reviewed;
        $("staffAcknowledge").disabled = session.busy || session.loading || !session.loaded;
        for (const id of ["employeeSubmit", "staffPasswordSubmit"]) $(id).disabled = locked();
        $("staffConfirmSubmit").disabled = session.busy || session.loading;
        for (const id of ["employeeForm", "staffPasswordForm"]) $(id).querySelectorAll("input,select").forEach((field) => { field.disabled = session.busy || session.loading || !!session.pending || (field.id === "employeeUserName" && !!editing); });
        if (!session.loaded) {
            $("staffSummary").innerHTML = ""; $("employeeCount").textContent = "—";
            $("employeeList").innerHTML = `<p class="staff-empty">${session.loading || session.busy ? "Đang tải nhân viên…" : "Chưa tải được danh sách. Bấm Làm mới để thử lại."}</p>`;
            $("employeeDetail").innerHTML = '<p class="staff-empty">Tải lại danh sách trước khi quản lý nhân viên.</p>'; return;
        }
        const roleValue = $("employeeRoleFilter").value;
        $("employeeRoleFilter").innerHTML = '<option value="all">Tất cả vai trò</option><option value="none">Chưa gán vai trò</option>' + roleOptions(session.roles);
        $("employeeRoleFilter").value = roleValue === "none" || session.roles.some((r) => r.roleName === roleValue) ? roleValue : "all";
        $("staffSummary").innerHTML = [["Tổng nhân viên", session.users.length], ["Đang hoạt động", session.users.filter((u) => u.isActive).length], ["Admin đang hoạt động", session.users.filter((u) => u.isActive && u.roles.includes("Admin")).length], ["Tạm khóa", session.users.filter((u) => !u.isActive).length]].map(([name, count]) => `<article><span>${name}</span><strong>${count}</strong></article>`).join("");
        const query = $("employeeSearch").value.trim().toLocaleLowerCase("vi"), role = $("employeeRoleFilter").value, state = $("employeeStatusFilter").value;
        const users = session.users.filter((u) => `${u.fullName} ${u.userName} ${u.email} ${u.phone}`.toLocaleLowerCase("vi").includes(query) && (role === "all" || (role === "none" ? !u.roles.length : u.roles.includes(role))) && (state === "all" || u.isActive === (state === "active")));
        $("employeeCount").textContent = `${users.length} người`;
        $("employeeList").innerHTML = users.length ? users.map((u) => {
            const name = u.fullName || u.userName;
            const initials = name.trim().split(/\s+/).filter(Boolean).slice(-2).map((part) => part[0]).join("").toLocaleUpperCase("vi");
            return `<article class="staff-row staff-directory-row${u.userId === selectedId ? " is-selected" : ""}"><div class="staff-person"><span class="staff-avatar" aria-hidden="true">${esc(initials)}</span><div><strong>${esc(name)}</strong><span>@${esc(u.userName)} · #${u.userId}</span><span class="staff-contact">${esc(u.email)}${u.phone ? ` · ${esc(u.phone)}` : ""}</span></div></div><div class="staff-row-meta"><span class="staff-role-label">${esc(u.roles.map(label).join(", ") || "Chưa gán vai trò")}</span><span class="staff-badge ${u.isActive ? "" : "is-locked"}">${u.isActive ? "Đang hoạt động" : "Tạm khóa"}</span></div><button class="secondary-action" data-view-user="${u.userId}" aria-label="Quản lý ${esc(name)}" ${session.busy || session.loading ? "disabled" : ""}>Quản lý <span aria-hidden="true">→</span></button></article>`;
        }).join("") : '<p class="staff-empty">Không có nhân viên phù hợp. Thay đổi bộ lọc hoặc thêm nhân viên.</p>';
        renderDetail();
    }
    async function run(action, success = "", target = "staffNotice") {
        try {
            const promise = action(); render(); const result = await promise; if (success) message(success);
            if (session.needsLogin) { clearPasswords(); auth.logout(); }
            return { ok: true, result };
        } catch (error) {
            message(error.message, true, target); if (target !== "staffNotice") message(error.message, true);
            if (session.pending) { clearPasswords(); document.querySelectorAll("dialog[open]").forEach((d) => d.close()); confirmAction = null; }
            else if ([400, 404, 409].includes(error.status) && session.hasAccess()) { try { await session.refresh(); } catch (_) { /* Giữ thông báo lỗi chính. */ } }
            return { ok: false, error };
        } finally { render(); }
    }
    async function selectUser(id) {
        selectedId = Number(id); const out = await run(() => session.selectUser(selectedId));
        if (out.ok && session.detail && detailMobile.matches) {
            placeDetail(true); $("employeeDetailTitle").focus({ preventScroll: true });
        }
        return out;
    }
    function confirm(title, text, action) { $("staffConfirmTitle").textContent = title; $("staffConfirmText").textContent = text; message("", false, "staffConfirmMessage"); confirmAction = action; $("staffConfirmDialog").showModal(); }
    function openEmployee(user = null) {
        if (locked()) return; editing = user ? { user, expected: snapshot(user) } : null;
        $("employeeForm").reset(); clearPasswords(); message("", false, "employeeFormMessage");
        $("employeeFullName").value = user?.fullName || ""; $("employeeUserName").value = user?.userName || ""; $("employeeEmail").value = user?.email || ""; $("employeePhone").value = user?.phone || "";
        $("employeeInitialRole").innerHTML = '<option value="">Chưa gán vai trò</option>' + roleOptions(session.roles);
        for (const id of ["employeeInitialRoleField", "employeePasswordField", "employeeConfirmField"]) $(id).hidden = !!user;
        $("employeePassword").required = !user; $("employeeConfirmPassword").required = !user;
        $("employeeDialogTitle").textContent = user ? "Sửa thông tin nhân viên" : "Thêm nhân viên";
        $("employeeDialogContext").textContent = user ? `@${user.userName} · Tên đăng nhập giữ nguyên. Vai trò và khóa tài khoản xử lý riêng.` : "Tạo tài khoản đăng nhập; chọn vai trò ban đầu nếu cần.";
        $("employeeSubmit").textContent = user ? "Lưu thông tin" : "Tạo nhân viên"; render(); $("employeeDialog").showModal(); $("employeeFullName").focus();
    }
    async function writeAndSelect(kind, id, data, expected, success, target) {
        const out = await run(() => session.command(kind, id, data, expected), success, target);
        if (out.ok && !session.needsLogin) {
            if (kind === "update" && id === auth.getUser()?.userId && out.result?.userId === id) auth.updateUser(out.result);
            const next = id || session.users.find((u) => u.userName.toLowerCase() === String(data.userName).trim().toLowerCase())?.userId;
            if (next) await selectUser(next);
        }
        return out;
    }
    $("employeeForm").addEventListener("submit", async (event) => {
        event.preventDefault(); if (locked() || !$("employeeForm").reportValidity()) return;
        const data = { fullName: $("employeeFullName").value, email: $("employeeEmail").value, phone: $("employeePhone").value, userName: $("employeeUserName").value, roleName: $("employeeInitialRole").value, password: $("employeePassword").value, confirmPassword: $("employeeConfirmPassword").value };
        const out = await writeAndSelect(editing ? "update" : "create", editing?.user.userId || null, data, editing?.expected, "Đã lưu nhân viên và tải lại dữ liệu.", "employeeFormMessage");
        clearPasswords(); if (out.ok) $("employeeDialog").close();
    });
    $("staffPasswordForm").addEventListener("submit", async (event) => {
        event.preventDefault(); if (locked() || !passwordTarget || !$("staffPasswordForm").reportValidity()) return;
        const data = { newPassword: $("staffNewPassword").value, confirmPassword: $("staffConfirmPassword").value };
        const out = await writeAndSelect("resetPassword", passwordTarget.user.userId, data, passwordTarget.expected, "Đã đặt lại mật khẩu. JWT cũ chưa được backend thu hồi.", "staffPasswordMessage");
        clearPasswords(); if (out.ok) $("staffPasswordDialog").close();
    });
    $("staffConfirmForm").addEventListener("submit", async (event) => {
        event.preventDefault(); if (session.busy || session.loading || !confirmAction) return;
        const out = await run(confirmAction, "Đã thực hiện và tải lại dữ liệu.", "staffConfirmMessage"); if (out.ok) { confirmAction = null; $("staffConfirmDialog").close(); }
    });
    $("employeeList").addEventListener("click", (event) => { const button = event.target.closest("[data-view-user]"); if (button) selectUser(button.dataset.viewUser); });
    $("employeeDetail").addEventListener("click", (event) => {
        const button = event.target.closest("button"), user = session.detail; if (!button || !user) return;
        if (button.hasAttribute("data-staff-back")) { if (detailMobile.matches) closeDetail(); else { $("employeeListTitle").scrollIntoView({ block: "start" }); $("employeeListTitle").focus({ preventScroll: true }); } return; }
        if (locked()) return;
        const expected = snapshot(user), context = `${user.fullName || user.userName} · @${user.userName} · #${user.userId}`;
        if (button.hasAttribute("data-staff-edit")) return openEmployee(user);
        if (button.hasAttribute("data-staff-active")) return confirm(user.isActive ? "Khóa tài khoản?" : "Mở khóa tài khoản?", `${context}. ${user.isActive ? "Chặn đăng nhập mới; JWT cũ chưa bị thu hồi." : "Cho phép đăng nhập lại bằng mật khẩu hiện có."}`, async () => { await session.command("active", user.userId, { isActive: !user.isActive }, expected); await session.selectUser(user.userId); });
        if (button.hasAttribute("data-staff-password")) { passwordTarget = { user, expected }; clearPasswords(); message("", false, "staffPasswordMessage"); $("staffPasswordContext").textContent = context; $("staffPasswordDialog").showModal(); $("staffNewPassword").focus(); return; }
        if (button.dataset.staffRemoveRole) {
            const roleName = button.dataset.staffRemoveRole;
            confirm("Thu hồi vai trò?", `${context}. Thu hồi ${label(roleName)} (${roleName}), giữ các vai trò còn lại. Người dùng cần đăng nhập lại để cập nhật JWT.`, async () => { await session.command("removeRole", user.userId, { roleName }, expected); if (!session.needsLogin) await session.selectUser(user.userId); });
        }
    });
    $("employeeDetail").addEventListener("submit", (event) => {
        if (event.target.id !== "staffAssignForm") return; event.preventDefault(); const user = session.detail; if (!user || locked() || !event.target.reportValidity()) return;
        const roleName = $("staffAssignRole").value, expected = snapshot(user);
        confirm("Gán thêm vai trò?", `@${user.userName} · #${user.userId} sẽ có thêm ${label(roleName)} (${roleName}). ${roleName === "Admin" ? "Quyền Quản trị cho phép quản lý toàn hệ thống. " : ""}Người dùng cần đăng nhập lại để nhận JWT mới.`, async () => { await session.command("assignRole", user.userId, { roleName }, expected); if (!session.needsLogin) await session.selectUser(user.userId); });
    });
    $("staffRefresh").addEventListener("click", () => run(() => session.refresh(), "Đã tải danh sách mới nhất.")); $("addEmployeeButton").addEventListener("click", () => openEmployee());
    $("staffReview").addEventListener("click", () => run(async () => { const done = await session.reviewPending(); message(done ? "Đã đối chiếu thao tác có phản hồi thành công." : "Đã tải danh sách mới. Kiểm tra tài khoản, vai trò và kết quả thao tác trước khi mở khóa."); }));
    $("staffAcknowledge").addEventListener("click", () => confirm("Đã đối chiếu xong?", "Chỉ mở khóa sau khi đối chiếu. Không có API đọc mật khẩu; kiểm tra kết quả đặt lại trước khi thực hiện thao tác khác. Xác nhận không gửi lại yêu cầu ghi.", () => session.acknowledgeReview()));
    $("employeeFilters").addEventListener("submit", (event) => event.preventDefault()); for (const id of ["employeeSearch", "employeeRoleFilter", "employeeStatusFilter"]) $(id).addEventListener("input", render);
    document.querySelectorAll("[data-staff-close]").forEach((button) => button.addEventListener("click", () => { if (session.busy) return; clearPasswords(); confirmAction = null; $(button.dataset.staffClose).close(); }));
    document.querySelectorAll("dialog").forEach((dialog) => dialog.addEventListener("cancel", (event) => { if (session.busy) event.preventDefault(); else { clearPasswords(); confirmAction = null; } }));
    auth.ready.then(async () => { render(); if (!session.hasAccess()) return; const out = await run(() => session.refresh()); if (out.ok && requestedRole && session.roles.some((r) => r.roleName === requestedRole)) { $("employeeRoleFilter").value = requestedRole; render(); } });
})();
