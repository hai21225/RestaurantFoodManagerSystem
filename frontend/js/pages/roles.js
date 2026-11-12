(function () {
    const { StaffSession, roleLabels } = window.restaurantStaff;
    const auth = window.restaurantAuth, session = new StaffSession({ api: window.restaurantApi, auth, storage: window.sessionStorage });
    const $ = (id) => document.getElementById(id);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    let busy = false;
    async function load() {
        if (busy || !session.hasAccess()) return; busy = true; $("rolesRefresh").disabled = true; $("staffNotice").textContent = ""; $("rolesCount").textContent = "Đang tải…"; $("rolesList").innerHTML = "";
        try {
            const roles = await session.loadRoles(); $("rolesCount").textContent = `${roles.length} vai trò từ máy chủ`;
            $("rolesList").innerHTML = roles.map((role) => `<article class="staff-role-editorial"><span class="role-letter" aria-hidden="true">${esc((roleLabels[role.roleName] || role.roleName).split(/\s+/).map((part) => part[0]).slice(0, 2).join(""))}</span><div class="role-editorial-content"><div class="role-editorial-title"><h3>${esc(roleLabels[role.roleName] || role.roleName)}</h3><span>#${role.roleId} · ${esc(role.roleName)}</span></div><p>${esc(role.roleDescription || "Máy chủ chưa có mô tả cho vai trò này.")}</p></div><a class="secondary-action" href="employees.html?role=${encodeURIComponent(role.roleName)}">Xem nhân viên <span aria-hidden="true">→</span></a></article>`).join("") || '<p class="staff-empty">Máy chủ chưa trả vai trò nào.</p>';
        } catch (error) { $("rolesCount").textContent = "Chưa tải được danh sách"; $("staffNotice").textContent = error.message; $("staffNotice").classList.add("is-error"); }
        finally { busy = false; $("rolesRefresh").disabled = false; }
    }
    $("rolesRefresh").addEventListener("click", () => { $("staffNotice").classList.remove("is-error"); load(); });
    auth.ready.then(() => { const allowed = session.hasAccess(); $("staffContent").hidden = !allowed; $("staffAccess").hidden = allowed; if (allowed) load(); });
})();
