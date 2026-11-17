(function () {
    const { StaffSession, snapshot } = window.restaurantStaff;
    const auth = window.restaurantAuth, session = new StaffSession({ api: window.restaurantApi, auth, storage: window.sessionStorage });
    session.userType = "customer";
    const $ = (id) => document.getElementById(id);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const locked = () => session.busy || session.loading || session.detailLoading || !!session.pending || !session.loaded;
    let selectedId = null, passwordTarget = null, confirmAction = null;
    const detailMobile = window.matchMedia("(max-width: 1100px)");

    function placeDetail(open = false) {
        const panel = $("customerDetailPanel"), sheet = $("customerDetailSheet");
        if (detailMobile.matches) {
            if (panel.parentElement !== sheet) sheet.appendChild(panel);
            if (open && !sheet.open) sheet.showModal();
        } else {
            if (sheet.open) sheet.close();
            $("customerDetailRail").appendChild(panel);
        }
    }

    function closeDetail() {
        if (session.busy) return;
        $("customerDetailSheet").close();
        const selected = $("customerList").querySelector(`[data-view-user="${selectedId}"]`);
        selected?.focus();
    }

    $("customerDetailClose").addEventListener("click", closeDetail);
    detailMobile.addEventListener("change", () => placeDetail());
    placeDetail();

    function message(text = "", error = false, id = "staffNotice") { $(id).textContent = text; $(id).classList.toggle("is-error", error); }
    function clearPasswords() { for (const id of ["staffNewPassword", "staffConfirmPassword"]) $(id).value = ""; }
    function date(value) {
        if (!value) return "Chưa có";
        const parsed = new Date(/Z$|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`);
        return Number.isFinite(parsed.getTime()) ? parsed.toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" }) : "Chưa rõ";
    }
    function money(value) {
        return Number.isFinite(Number(value)) ? `${Number(value).toLocaleString("vi-VN", { maximumFractionDigits: 2 })} đ` : "—";
    }

    function renderDetail() {
        const user = session.detail;
        if (session.detailLoading) { $("customerDetail").innerHTML = '<p class="staff-empty">Đang tải thông tin…</p>'; return; }
        if (!user) { $("customerDetail").innerHTML = '<p class="staff-empty">Chọn Xem / Quản lý để xem thống kê khách hàng, khóa tài khoản hoặc đặt lại mật khẩu.</p>'; return; }
        const reason = session.lockReason(user), summary = session.customerSummary;
        const summaryHtml = summary
            ? `<dl><dt>Số đơn hàng</dt><dd>${esc(summary.orderCount ?? 0)}</dd><dt>Tổng chi tiêu</dt><dd>${esc(money(summary.totalSpent))}</dd><dt>Số lượt đặt bàn</dt><dd>${esc(summary.reservationCount ?? 0)}</dd></dl>`
            : '<p class="staff-caption">Chưa tải được thống kê khách hàng.</p>';
        $("customerDetail").innerHTML = `<div class="staff-actions"><h3>${esc(user.fullName || user.userName)}</h3><button class="secondary-action" data-customer-back>Về danh sách</button></div><p class="staff-caption">@${esc(user.userName)} · #${user.userId}</p>
            <dl><dt>Email</dt><dd>${esc(user.email)}</dd><dt>Điện thoại</dt><dd>${esc(user.phone || "Chưa có")}</dd><dt>Trạng thái</dt><dd>${user.isActive ? "Đang hoạt động" : "Tạm khóa"}</dd><dt>Tạo lúc</dt><dd>${esc(date(user.createdAt))}</dd><dt>Cập nhật</dt><dd>${esc(date(user.updatedAt))}</dd></dl>
            <div class="staff-role-section"><h3>Thống kê khách hàng</h3>${summaryHtml}</div>
            <div class="staff-actions"><button class="secondary-action" data-customer-active ${locked() || reason ? "disabled" : ""}>${user.isActive ? "Khóa tài khoản" : "Mở khóa tài khoản"}</button><button class="secondary-action" data-customer-password ${locked() ? "disabled" : ""}>Đặt lại mật khẩu</button></div>
            ${reason ? `<p class="staff-caption">${esc(reason)}</p>` : ""}`;
    }

    function render() {
        const allowed = session.hasAccess(); $("staffContent").hidden = !allowed; $("staffAccess").hidden = allowed; if (!allowed) return;
        $("staffRefresh").disabled = session.busy || session.loading || session.detailLoading;
        $("customerDetailClose").disabled = session.busy;
        $("staffPending").hidden = !session.pending;
        if (session.pending) {
            const names = { active: "Khóa/mở", resetPassword: "Đặt lại mật khẩu" };
            $("staffPendingSummary").textContent = `${names[session.pending.kind] || session.pending.kind} · @${session.pending.targetName || "chưa rõ"} · ${session.pending.accepted ? "Đã nhận phản hồi thành công, cần tải lại." : "Chưa rõ kết quả ghi."}`;
        }
        $("staffReview").disabled = session.busy || session.loading; $("staffAcknowledge").hidden = !session.pending?.reviewed;
        $("staffAcknowledge").disabled = session.busy || session.loading || !session.loaded;
        $("staffPasswordSubmit").disabled = locked();
        $("staffConfirmSubmit").disabled = session.busy || session.loading;
        $("staffPasswordForm").querySelectorAll("input").forEach((field) => { field.disabled = session.busy || session.loading || !!session.pending; });
        if (!session.loaded) {
            $("staffSummary").innerHTML = ""; $("customerCount").textContent = "—";
            $("customerList").innerHTML = `<p class="staff-empty">${session.loading || session.busy ? "Đang tải khách hàng…" : "Chưa tải được danh sách. Bấm Làm mới để thử lại."}</p>`;
            $("customerDetail").innerHTML = '<p class="staff-empty">Tải lại danh sách trước khi quản lý khách hàng.</p>'; return;
        }
        $("staffSummary").innerHTML = [
            ["Tổng khách hàng", session.users.length],
            ["Đang hoạt động", session.users.filter((u) => u.isActive).length],
            ["Tạm khóa", session.users.filter((u) => !u.isActive).length]
        ].map(([name, count]) => `<article><span>${name}</span><strong>${count}</strong></article>`).join("");
        const query = $("customerSearch").value.trim().toLocaleLowerCase("vi"), state = $("customerStatusFilter").value;
        const users = session.users.filter((u) => `${u.fullName} ${u.userName} ${u.email} ${u.phone}`.toLocaleLowerCase("vi").includes(query) && (state === "all" || u.isActive === (state === "active")));
        $("customerCount").textContent = `${users.length} người`;
        $("customerList").innerHTML = users.length ? users.map((u) => {
            const name = u.fullName || u.userName;
            const initials = name.trim().split(/\s+/).filter(Boolean).slice(-2).map((part) => part[0]).join("").toLocaleUpperCase("vi");
            return `<article class="staff-row staff-directory-row${u.userId === selectedId ? " is-selected" : ""}"><div class="staff-person"><span class="staff-avatar" aria-hidden="true">${esc(initials)}</span><div><strong>${esc(name)}</strong><span>@${esc(u.userName)} · #${u.userId}</span><span class="staff-contact">${esc(u.email)}${u.phone ? ` · ${esc(u.phone)}` : ""}</span></div></div><div class="staff-row-meta"><span class="staff-badge ${u.isActive ? "" : "is-locked"}">${u.isActive ? "Đang hoạt động" : "Tạm khóa"}</span></div><button class="secondary-action" data-view-user="${u.userId}" aria-label="Quản lý ${esc(name)}" ${session.busy || session.loading ? "disabled" : ""}>Quản lý <span aria-hidden="true">→</span></button></article>`;
        }).join("") : '<p class="staff-empty">Không có khách hàng phù hợp. Thay đổi bộ lọc tìm kiếm.</p>';
        renderDetail();
    }

    async function run(action, success = "", target = "staffNotice") {
        try {
            const promise = action(); render(); const result = await promise; if (success) message(success);
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
            placeDetail(true); $("customerDetailTitle").focus({ preventScroll: true });
        }
        return out;
    }

    function confirm(title, text, action) { $("staffConfirmTitle").textContent = title; $("staffConfirmText").textContent = text; message("", false, "staffConfirmMessage"); confirmAction = action; $("staffConfirmDialog").showModal(); }

    $("staffPasswordForm").addEventListener("submit", async (event) => {
        event.preventDefault(); if (locked() || !passwordTarget || !$("staffPasswordForm").reportValidity()) return;
        const data = { newPassword: $("staffNewPassword").value, confirmPassword: $("staffConfirmPassword").value };
        const out = await run(() => session.command("resetPassword", passwordTarget.user.userId, data, passwordTarget.expected), "Đã đặt lại mật khẩu. JWT cũ chưa được backend thu hồi.", "staffPasswordMessage");
        clearPasswords();
        if (out.ok) { $("staffPasswordDialog").close(); await selectUser(passwordTarget.user.userId); }
    });

    $("staffConfirmForm").addEventListener("submit", async (event) => {
        event.preventDefault(); if (session.busy || session.loading || !confirmAction) return;
        const out = await run(confirmAction, "Đã thực hiện và tải lại dữ liệu.", "staffConfirmMessage");
        if (out.ok) { confirmAction = null; $("staffConfirmDialog").close(); }
    });

    $("customerList").addEventListener("click", (event) => { const button = event.target.closest("[data-view-user]"); if (button) selectUser(button.dataset.viewUser); });

    $("customerDetail").addEventListener("click", (event) => {
        const button = event.target.closest("button"), user = session.detail; if (!button || !user) return;
        if (button.hasAttribute("data-customer-back")) { if (detailMobile.matches) closeDetail(); else { $("customerListTitle").scrollIntoView({ block: "start" }); $("customerListTitle").focus({ preventScroll: true }); } return; }
        if (locked()) return;
        const expected = snapshot(user), context = `${user.fullName || user.userName} · @${user.userName} · #${user.userId}`;
        if (button.hasAttribute("data-customer-active")) {
            return confirm(
                user.isActive ? "Khóa tài khoản?" : "Mở khóa tài khoản?",
                `${context}. ${user.isActive ? "Chặn đăng nhập mới; JWT cũ chưa bị thu hồi." : "Cho phép đăng nhập lại bằng mật khẩu hiện có."}`,
                async () => { await session.command("active", user.userId, { isActive: !user.isActive }, expected); await session.selectUser(user.userId); }
            );
        }
        if (button.hasAttribute("data-customer-password")) {
            passwordTarget = { user, expected }; clearPasswords(); message("", false, "staffPasswordMessage");
            $("staffPasswordContext").textContent = context; $("staffPasswordDialog").showModal(); $("staffNewPassword").focus();
        }
    });

    $("staffRefresh").addEventListener("click", () => run(() => session.refresh(), "Đã tải danh sách mới nhất."));
    $("staffReview").addEventListener("click", () => run(async () => { const done = await session.reviewPending(); message(done ? "Đã đối chiếu thao tác có phản hồi thành công." : "Đã tải danh sách mới. Kiểm tra tài khoản trước khi mở khóa."); }));
    $("staffAcknowledge").addEventListener("click", () => confirm("Đã đối chiếu xong?", "Chỉ mở khóa sau khi đối chiếu. Không có API đọc mật khẩu; kiểm tra kết quả đặt lại trước khi thực hiện thao tác khác. Xác nhận không gửi lại yêu cầu ghi.", () => session.acknowledgeReview()));
    $("customerFilters").addEventListener("submit", (event) => event.preventDefault());
    for (const id of ["customerSearch", "customerStatusFilter"]) $(id).addEventListener("input", render);
    document.querySelectorAll("[data-staff-close]").forEach((button) => button.addEventListener("click", () => { if (session.busy) return; clearPasswords(); confirmAction = null; $(button.dataset.staffClose).close(); }));
    document.querySelectorAll("dialog").forEach((dialog) => dialog.addEventListener("cancel", (event) => { if (session.busy) event.preventDefault(); else { clearPasswords(); confirmAction = null; } }));

    auth.ready.then(async () => {
        render();
        if (!session.hasAccess()) return;
        await run(() => session.load("customer"));
    });
})();
