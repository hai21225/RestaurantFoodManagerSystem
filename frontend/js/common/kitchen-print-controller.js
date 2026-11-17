(function () {
    "use strict";

    function create({ session, auth, refresh, onStateChange }) {
        const $ = (id) => document.getElementById(id);
        const dialog = $("kitchenPrintDialog"), preview = $("kitchenPrintPreview"), paper = $("kitchenPrintPaper");
        const scopeInput = $("kitchenPrintScope"), sizeInput = $("kitchenPrintSize"), reprintInput = $("kitchenPrintReprint");
        const refreshButton = $("kitchenPrintRefresh"), printButton = $("kitchenPrintButton");
        let current = null, busy = false, printing = false, nonce = 0, size = "80", expectedCloseEvents = 0;
        const authorized = () => Boolean(auth.hasRole("Chef", "Admin"));
        const sessionAvailable = () => authorized() && !session.busy && !session.loading && !session.pending;
        const orderClosed = (ticket) => ["paid", "cancelled"].includes(String(ticket.order?.status || "").toLowerCase());
        const activeItems = (ticket) => orderClosed(ticket) ? [] : (Array.isArray(ticket.items) ? ticket.items : [])
            .filter((item) => item && ["pending", "preparing"].includes(String(item.status || "").toLowerCase()));
        const printable = () => current && (current.scope === "all" ? current.ticket.items?.length > 0 : activeItems(current.ticket).length > 0);

        function message(text) { $("kitchenPrintMessage").textContent = text; }
        function clearPaper() { document.body.classList.remove("printing-kitchen"); paper.innerHTML = ""; }
        function syncControls() {
            const blocked = busy || printing || !sessionAvailable();
            refreshButton.disabled = blocked || !current || !dialog.open;
            printButton.disabled = blocked || !current?.valid || !session.loaded || !dialog.open || !printable();
            for (const input of [scopeInput, sizeInput, reprintInput]) input.disabled = busy || printing || !authorized() || !current;
            // Luôn cho đóng để hủy hiệu lực mọi GET/in đang chờ.
            $("kitchenPrintClose").disabled = false; $("kitchenPrintCancel").disabled = false;
        }
        function changed() { syncControls(); if (typeof onStateChange === "function") onStateChange(); }
        function close(closeNative = true) {
            nonce++; current = null; busy = false; printing = false;
            clearPaper(); preview.innerHTML = ""; message("");
            if (closeNative && dialog.open) { expectedCloseEvents++; dialog.close(); }
            changed();
        }
        function freshTicket(id) {
            const ticket = session.find(id, null)?.ticket;
            if (!ticket || Number(ticket.kitchenOrderId) !== id) throw new Error("Không tìm thấy phiếu bếp vừa chọn trong dữ liệu mới.");
            const copy = JSON.parse(JSON.stringify(ticket));
            // Thời gian lập bản in thuộc controller, không tham gia so sánh nghiệp vụ của phiếu.
            delete copy.generatedAt;
            return { ticket: copy, signature: JSON.stringify(copy) };
        }
        function renderPreview() {
            if (!current) return;
            scopeInput.value = current.scope; sizeInput.value = size; reprintInput.checked = current.reprint;
            preview.dataset.paperSize = size; paper.dataset.paperSize = size;
            $("kitchenPrintTitle").textContent = `Phiếu bếp #${current.id} · Bàn ${current.ticket.tableNumber || "—"}`;
            preview.innerHTML = window.restaurantKitchenPrinting.renderSlip(current.ticket, {
                scope: current.scope, isReprint: current.reprint, generatedAt: current.generatedAt
            });
        }
        function unavailable(error) {
            if (current) current.valid = false;
            clearPaper();
            const announcement = $("kitchenAnnouncement")?.textContent?.trim();
            const reason = error?.message || announcement || "Chưa tải được phiếu mới nhất.";
            message(`${reason} Bản xem cũ chỉ để đối chiếu; hãy tải lại trước khi in.`);
            if (!dialog.open && $("kitchenAnnouncement")) {
                $("kitchenAnnouncement").textContent = reason;
                $("kitchenAnnouncement").classList.toggle("is-error", true);
            }
        }
        function validOperation(token) { return token === nonce && authorized(); }

        async function open(ticketId) {
            const id = typeof ticketId === "number" || typeof ticketId === "string" ? Number(ticketId) : NaN;
            if (!Number.isSafeInteger(id) || id <= 0 || !session.loaded || !sessionAvailable() || busy || printing || dialog.open) return false;
            const token = ++nonce;
            busy = true; changed();
            try {
                await refresh(id);
                if (!validOperation(token)) return false;
                if (!session.loaded || !sessionAvailable()) { unavailable(); return false; }
                const fresh = freshTicket(id);
                current = { id, ...fresh, valid: true, generatedAt: Date.now(),
                    scope: activeItems(fresh.ticket).length ? "active" : "all", reprint: false };
                clearPaper(); renderPreview();
                message("Kiểm tra nội dung từng phiếu món, sau đó bấm Mở hộp thoại in và chọn máy in bếp đúng khổ giấy.");
                dialog.showModal();
                return true;
            } catch (error) {
                if (!validOperation(token)) return false;
                unavailable(error);
                if (!dialog.open) { current = null; preview.innerHTML = ""; }
                return false;
            } finally {
                if (token === nonce) { busy = false; changed(); }
            }
        }

        async function refreshPreview() {
            if (!current || !dialog.open || !sessionAvailable() || busy || printing) return false;
            const token = ++nonce, id = current.id;
            busy = true; changed();
            try {
                await refresh(id);
                if (!validOperation(token) || !current || !dialog.open) return false;
                if (!session.loaded || !sessionAvailable()) { unavailable(); return false; }
                Object.assign(current, freshTicket(id), { valid: true, generatedAt: Date.now() });
                renderPreview(); message("Đã tải lại phiếu để đối chiếu. Chưa gửi lệnh in.");
                return true;
            } catch (error) {
                if (validOperation(token) && current) unavailable(error);
                return false;
            } finally {
                if (token === nonce) { busy = false; changed(); }
            }
        }

        async function print() {
            if (!current?.valid || !dialog.open || !session.loaded || !sessionAvailable() || busy || printing || !printable()) return false;
            const token = ++nonce, id = current.id;
            busy = true; changed();
            try {
                await refresh(id);
                if (!validOperation(token) || !current || !dialog.open) return false;
                if (!session.loaded || !sessionAvailable()) { unavailable(); return false; }
                const fresh = freshTicket(id), different = fresh.signature !== current.signature;
                Object.assign(current, fresh, { valid: true, generatedAt: Date.now() });
                renderPreview();
                if (different) {
                    message("Phiếu vừa thay đổi. Bản xem đã được cập nhật; hãy kiểm tra rồi bấm Mở hộp thoại in lần nữa.");
                    return false;
                }
                if (!printable()) { message("Không có món trong phạm vi đã chọn để in."); return false; }
                paper.dataset.paperSize = size;
                paper.innerHTML = window.restaurantKitchenPrinting.renderSlip(current.ticket, {
                    scope: current.scope, isReprint: current.reprint, generatedAt: current.generatedAt
                });
                printing = true; document.body.classList.add("printing-kitchen"); changed();
                try { window.print(); }
                catch (error) {
                    printing = false; clearPaper();
                    message("Không mở được hộp thoại in. Hãy thử lại trên trình duyệt có hỗ trợ in hoặc lưu PDF.");
                    return false;
                }
                return true;
            } catch (error) {
                if (validOperation(token) && current) unavailable(error);
                return false;
            } finally {
                if (token === nonce) { busy = false; changed(); }
            }
        }

        function optionsChanged() {
            if (!current || !dialog.open || busy || printing || !authorized()) return;
            current.scope = scopeInput.value === "all" ? "all" : "active";
            current.reprint = reprintInput.checked === true;
            size = sizeInput.value === "58" ? "58" : "80";
            clearPaper(); renderPreview(); syncControls();
        }
        for (const input of [scopeInput, sizeInput, reprintInput]) input.addEventListener("change", optionsChanged);
        refreshButton.addEventListener("click", refreshPreview);
        printButton.addEventListener("click", print);
        $("kitchenPrintClose").addEventListener("click", () => close());
        $("kitchenPrintCancel").addEventListener("click", () => close());
        dialog.addEventListener("cancel", (event) => { event.preventDefault(); close(); });
        dialog.addEventListener("close", () => {
            if (expectedCloseEvents) { expectedCloseEvents--; return; }
            if (!dialog.open) close(false);
        });
        window.addEventListener("afterprint", () => {
            if (!printing) return;
            printing = false; clearPaper();
            if (current && dialog.open) message("Hộp thoại in đã đóng. Kiểm tra máy in trước khi in thêm bản.");
            changed();
        });
        window.addEventListener("restaurant:auth-changed", () => close());
        window.addEventListener("pagehide", () => close());
        // Không gọi onStateChange trong create: caller còn đang gán biến controller.
        syncControls();
        return { open, close, cleanup: close, syncControls, isOpen: () => Boolean(dialog.open), isBusy: () => busy || printing };
    }

    window.restaurantKitchenPrintController = Object.freeze({ create });
})();
