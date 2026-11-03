(function () {
    const auth = window.restaurantAuth, { snapshot } = window.restaurantAdmin;
    const session = new window.restaurantOrderExtras.OrderExtrasSession({ api: window.restaurantApi, auth, storage: sessionStorage, orderId: new URL(location.href).searchParams.get("orderId") });
    const $ = (id) => document.getElementById(id);
    const esc = (v) => String(v ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const money = (v) => `${Number(v).toLocaleString("vi-VN", { maximumFractionDigits: 2 })} đ`;
    let expected = null, command = null;
    const locked = () => !session.loaded || session.loading || session.busy || session.pending || session.otherPending() || !session.canEdit() || ["Paid", "Cancelled"].includes(session.order?.status);
    function message(text = "", error = false) { $("toolsNotice").textContent = text; $("toolsNotice").classList.toggle("is-error", error); }
    function render(fill = false) {
        const o = session.order;
        $("toolsTitle").textContent = o ? `Đơn #${o.orderId} · Bàn ${session.tables.find((t) => t.tableId === o.tableId)?.tableNumber || o.tableId}` : "Chưa chọn đơn";
        $("toolsSummary").innerHTML = o ? `<dl class="admin-details">${[["Trạng thái", ({ Pending: "Chờ xử lý", SentToKitchen: "Đã gửi bếp", Preparing: "Đang chế biến", Ready: "Chờ thanh toán", Completed: "Hoàn tất", Paid: "Đã thu", Cancelled: "Đã hủy" })[o.status] || o.status], ["Tạm tính", money(o.subtotal)], ["Giảm giá", money(o.discountAmount)], ["VAT", money(o.taxAmount)], ["Phí dịch vụ", money(o.serviceChargeAmount)], ["Tổng đơn", money(o.totalAmount)], ["Khuyến mãi", o.promotionId ? `#${o.promotionId} · ${o.promotionDiscountType === "Percent" ? `${o.promotionDiscountValue}%` : money(o.promotionDiscountValue)}` : "Chưa áp dụng"], ["Ghi chú hóa đơn", o.note || "—"]].map(([k, v]) => `<div><dt>${esc(k)}</dt><dd>${esc(v)}</dd></div>`).join("")}</dl>` : '<p class="admin-context">Mở từ một đơn tại màn Đặt món để xem thông tin.</p>';
        if (fill && o) {
            expected = snapshot(o); $("billNote").value = o.note || "";
            $("moveTable").innerHTML = '<option value="">Chọn bàn trống</option>' + session.tables.filter((t) => t.isActive && t.status === "Available" && t.tableId !== o.tableId).map((t) => `<option value="${t.tableId}">Bàn ${esc(t.tableNumber)} · ${esc(t.areaName || "Chưa phân khu")} · ${t.capacity} chỗ</option>`).join("");
            $("promotionList").innerHTML = session.promotions.filter((p) => p.isActive).map((p) => `<option value="${p.id}" label="${esc(`${p.name} · ${p.discountType === "Percent" ? `${p.discountValue}%` : money(p.discountValue)} · ${String(p.startAt).slice(0, 10)}–${String(p.endAt).slice(0, 10)}`)}"></option>`).join("");
            $("backToOrder").href = `order.html?tableId=${o.tableId}`;
            $("goToPayment").href = `payment.html?orderId=${o.orderId}`;
        }
        $("promotionHint").textContent = auth.hasRole("Admin") ? "Chọn mã từ danh sách. Hệ thống kiểm tra thời hạn, phạm vi và điều kiện đơn rồi cập nhật số tiền." : "Hiện chưa có danh sách chọn khuyến mãi cho vai trò Phục vụ. Bạn có thể dùng mã do Quản trị cung cấp.";
        for (const form of [$("noteForm"), $("moveForm"), $("promotionForm")]) form.querySelectorAll("input,textarea,select,button").forEach((e) => { e.disabled = !!locked(); });
        $("removePromotion").disabled = !!locked() || !o?.promotionId;
        $("toolsRefresh").disabled = session.busy || session.loading;
        $("toolsPending").hidden = !session.pending;
        $("toolsPendingText").textContent = session.pending ? `${session.pending.description}. ${session.pending.accepted ? "Máy chủ đã nhận lệnh; cần đọc lại kết quả." : "Chưa rõ kết quả. Không gửi lại lệnh."}` : "";
        $("toolsReview").disabled = session.busy; $("toolsAck").hidden = !session.pending?.reviewed;
        $("toolsBusyHint").textContent = session.otherPending() ? "Đang có yêu cầu Đặt món/Thanh toán chưa đối chiếu. Quay lại màn tương ứng để xử lý trước." : "";
        $("toolsConfirmSubmit").disabled = !!locked();
    }
    async function run(action, success = "", fill = false) {
        try { const p = action(); render(); await p; if (success) message(success); render(fill); }
        catch (e) { message(e.message, true); render(); }
    }
    function confirm(kind, data, description) {
        if (locked()) return;
        command = { kind, data, expected }; $("toolsConfirmText").textContent = description; $("toolsConfirmMessage").textContent = ""; $("toolsConfirm").showModal();
    }
    $("noteForm").addEventListener("submit", (e) => { e.preventDefault(); confirm("note", { note: $("billNote").value }, "Lưu ghi chú hóa đơn? Để trống sẽ xóa ghi chú hiện tại."); });
    $("moveForm").addEventListener("submit", (e) => { e.preventDefault(); confirm("move", { tableId: $("moveTable").value }, "Chuyển toàn bộ đơn sang bàn đã chọn? Máy chủ sẽ kiểm tra lại bàn đích, trả bàn cũ và giữ nguyên món/tiền của đơn."); });
    $("promotionForm").addEventListener("submit", (e) => { e.preventDefault(); confirm("apply", { promotionId: $("promotionId").value }, "Áp dụng khuyến mãi này? Khuyến mãi hiện tại sẽ được thay thế; tổng tiền mới do máy chủ tính."); });
    $("removePromotion").addEventListener("click", () => confirm("remove", { promotionId: session.order?.promotionId }, "Gỡ khuyến mãi đang áp dụng? Máy chủ tính lại tổng đơn."));
    $("toolsConfirmForm").addEventListener("submit", async (e) => {
        e.preventDefault(); if (!command || locked()) return;
        try { const p = session.command(command.kind, command.data, command.expected); render(); await p; $("toolsConfirm").close(); message("Đã cập nhật và đọc lại đơn từ máy chủ."); render(true); }
        catch (error) { $("toolsConfirmMessage").textContent = error.message; message(error.message, true); }
        finally { render(); }
    });
    $("toolsConfirmClose").addEventListener("click", () => $("toolsConfirm").close());
    $("toolsRefresh").addEventListener("click", () => run(() => session.refresh(), "Đã tải lại đơn.", true));
    $("toolsReview").addEventListener("click", () => run(() => session.review(() => session.load()), "Đã tải đơn hiện tại. Đối chiếu bàn, ghi chú và khuyến mãi trước khi tiếp tục.", true));
    $("toolsAck").addEventListener("click", () => { try { session.acknowledge(); render(); } catch (e) { message(e.message, true); } });
    auth.ready.then(() => run(() => session.refresh(), "", true));
})();
