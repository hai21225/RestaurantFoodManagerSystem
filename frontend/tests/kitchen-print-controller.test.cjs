const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

class Element {
    constructor() {
        this.innerHTML = ""; this.textContent = ""; this.value = ""; this.checked = false;
        this.disabled = false; this.open = false; this.dataset = {}; this.handlers = new Map();
        const classes = new Set();
        this.classList = { add: (v) => classes.add(v), remove: (v) => classes.delete(v), contains: (v) => classes.has(v), toggle: (v, on) => on ? classes.add(v) : classes.delete(v) };
    }
    addEventListener(type, fn) { const rows = this.handlers.get(type) || []; rows.push(fn); this.handlers.set(type, rows); }
    async emit(type, event = {}) { for (const fn of this.handlers.get(type) || []) await fn({ preventDefault() {}, ...event }); }
    showModal() { this.open = true; }
    close() { this.open = false; for (const fn of this.handlers.get("close") || []) fn({}); }
}
const copy = (value) => JSON.parse(JSON.stringify(value));
const settle = () => new Promise((resolve) => setImmediate(resolve));
const ticket = () => ({ kitchenOrderId: 10, orderId: 42, tableNumber: "B3", sentAt: "2026-11-12T05:00:00Z", status: "Preparing", waiterName: "Phục vụ A",
    order: { status: "Preparing", note: "Ghi chú đơn", items: [{ id: 99, status: "Pending", foodName: "DÒNG PHIẾU KHÁC" }] },
    items: [{ id: 1, orderItemId: 11, status: "Pending", foodName: "Món chờ", quantity: 2, sizeName: "Vừa" },
        { id: 2, orderItemId: 12, status: "Preparing", foodName: "Món đang nấu", quantity: 3, sizeName: "Lớn" },
        { id: 3, orderItemId: 13, status: "Completed", foodName: "Món xong", quantity: 1 },
        { id: 4, orderItemId: 14, status: "Cancelled", foodName: "Món hủy", quantity: 1, cancelReason: "Hỏng" }] });

function page({ roles = ["Chef"], data = ticket(), printError = false } = {}) {
    const ids = ["kitchenPrintDialog", "kitchenPrintPreview", "kitchenPrintPaper", "kitchenPrintScope", "kitchenPrintSize", "kitchenPrintReprint", "kitchenPrintMessage", "kitchenPrintRefresh", "kitchenPrintButton", "kitchenPrintClose", "kitchenPrintCancel", "kitchenPrintTitle", "kitchenAnnouncement"];
    const elements = new Map(ids.map((id) => [id, new Element()]));
    const $ = (id) => { assert.ok(elements.has(id), `Known element #${id}`); return elements.get(id); };
    const body = new Element(), events = new Map(), reads = [], finds = [], prints = [];
    let server = copy(data), failure = false, gate = null, release, callbacks = 0, controller;
    const auth = { hasRole: (...allowed) => roles.some((role) => allowed.includes(role)) };
    const session = { loaded: true, loading: false, busy: false, pending: null, record: copy(data),
        find(id, itemId) { finds.push([id, itemId]); return { ticket: this.record?.kitchenOrderId === id ? this.record : undefined }; } };
    const window = {
        addEventListener(type, fn) { const handlers = events.get(type) || []; handlers.push(fn); events.set(type, handlers); },
        print() {
            prints.push({ html: $("kitchenPrintPaper").innerHTML, size: $("kitchenPrintPaper").dataset.paperSize, printingClass: body.classList.contains("printing-kitchen") });
            if (printError) throw new Error("Printer dialog unavailable");
        }
    };
    const context = { window, document: { body, getElementById: $ }, Date };
    for (const file of ["preparation-timing.js", "kitchen-printing.js", "kitchen-print-controller.js"]) vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../js/common", file), "utf8"), context);
    controller = window.restaurantKitchenPrintController.create({ session, auth,
        async refresh(id) {
            reads.push(id); session.loading = true; session.loaded = false;
            if (gate) await gate;
            session.loading = false;
            if (failure) { $("kitchenAnnouncement").textContent = "Máy chủ gặp lỗi. Phiếu cũ chỉ để xem."; return; }
            session.record = copy(server); session.loaded = true;
        },
        onStateChange() { callbacks++; controller?.syncControls(); }
    });
    return { $, body, session, reads, finds, prints, controller,
        callbacks: () => callbacks,
        data(value) { server = copy(value); }, fail(value) { failure = value; },
        roles(value) { roles = value; },
        hold() { gate = new Promise((resolve) => { release = resolve; }); },
        release() { const done = release; gate = null; done(); },
        async event(type) { for (const fn of events.get(type) || []) await fn({}); await settle(); }
    };
}

test("open reads the full fresh mixed ticket and previews without printing; sync does not recurse", async () => {
    const p = page();
    assert.equal(p.callbacks(), 0, "create does not callback before the caller has assigned controller");
    assert.equal(await p.controller.open(10), true);
    assert.deepEqual(p.reads, [10]); assert.deepEqual(p.finds, [[10, null]]);
    assert.match(p.$("kitchenPrintPreview").innerHTML, /Món chờ/);
    assert.match(p.$("kitchenPrintPreview").innerHTML, /Món đang nấu/);
    assert.doesNotMatch(p.$("kitchenPrintPreview").innerHTML, /Món xong|Món hủy|DÒNG PHIẾU KHÁC/);
    assert.match(p.$("kitchenPrintMessage").textContent, /Kiểm tra nội dung.*Mở hộp thoại in/);
    assert.equal(p.prints.length, 0); assert.equal(p.$("kitchenPrintPaper").innerHTML, "");
    assert.equal(p.controller.isOpen(), true); assert.equal(p.controller.isBusy(), false);
    const callbacks = p.callbacks(); p.controller.syncControls(); p.controller.syncControls();
    assert.equal(p.callbacks(), callbacks);
});

test("print preflight refreshes; changed data requires a second explicit click before window.print", async () => {
    const p = page(); await p.controller.open(10);
    const changed = ticket(); changed.items[0].quantity = 4; p.data(changed);
    await p.$("kitchenPrintButton").emit("click");
    assert.equal(p.prints.length, 0); assert.deepEqual(p.reads, [10, 10]);
    assert.match(p.$("kitchenPrintMessage").textContent, /Phiếu vừa thay đổi/);
    assert.match(p.$("kitchenPrintPreview").innerHTML, /kitchen-slip-quantity">4 /);
    await p.$("kitchenPrintButton").emit("click");
    assert.equal(p.prints.length, 1); assert.deepEqual(p.reads, [10, 10, 10]);
    assert.equal(p.prints[0].printingClass, true); assert.equal(p.prints[0].size, "80");
    assert.match(p.prints[0].html, /kitchen-slip-quantity">4 /);
    assert.equal((p.prints[0].html.match(/<article class="kitchen-slip"/g) || []).length, 2, "Both dishes reach print as independent slips");
    assert.equal(p.controller.isBusy(), true);
    await p.event("afterprint");
    assert.equal(p.$("kitchenPrintPaper").innerHTML, "");
    assert.equal(p.body.classList.contains("printing-kitchen"), false);
    assert.equal(p.controller.isBusy(), false); assert.equal(p.controller.isOpen(), true);
    assert.equal(p.$("kitchenPrintMessage").textContent, "Hộp thoại in đã đóng. Kiểm tra máy in trước khi in thêm bản.");
    assert.doesNotMatch(p.$("kitchenPrintMessage").textContent, /thành công|đã in/i);
});

test("scope, explicit reprint and 58mm update only preview; paper preference survives close and cleanup clears Ctrl+P source", async () => {
    const p = page(); await p.controller.open(10);
    p.$("kitchenPrintScope").value = "all"; await p.$("kitchenPrintScope").emit("change");
    p.$("kitchenPrintReprint").checked = true; await p.$("kitchenPrintReprint").emit("change");
    p.$("kitchenPrintSize").value = "58"; await p.$("kitchenPrintSize").emit("change");
    assert.match(p.$("kitchenPrintPreview").innerHTML, /BẢN IN LẠI/);
    assert.match(p.$("kitchenPrintPreview").innerHTML, /ĐÃ HỦY — KHÔNG LÀM/);
    assert.match(p.$("kitchenPrintPreview").innerHTML, /ĐÃ XONG/);
    assert.equal(p.reads.length, 1); assert.equal(p.prints.length, 0);
    await p.$("kitchenPrintButton").emit("click");
    assert.equal(p.prints[0].size, "58");
    assert.match(p.prints[0].html, /BẢN IN LẠI/);
    p.controller.close();
    assert.equal(p.controller.isOpen(), false); assert.equal(p.controller.isBusy(), false);
    assert.equal(p.$("kitchenPrintPaper").innerHTML, ""); assert.equal(p.$("kitchenPrintPreview").innerHTML, "");
    assert.equal(p.body.classList.contains("printing-kitchen"), false);
    await p.event("afterprint"); assert.equal(p.$("kitchenPrintMessage").textContent, "");
    await p.controller.open(10);
    assert.equal(p.$("kitchenPrintSize").value, "58"); assert.equal(p.$("kitchenPrintReprint").checked, false);
});

test("failed GET keeps old preview but prevents print; explicit refresh can recover even though loaded is false", async () => {
    const p = page(); await p.controller.open(10);
    const before = p.$("kitchenPrintPreview").innerHTML;
    p.fail(true); await p.$("kitchenPrintButton").emit("click");
    assert.equal(p.$("kitchenPrintPreview").innerHTML, before);
    assert.equal(p.prints.length, 0); assert.equal(p.$("kitchenPrintButton").disabled, true);
    assert.match(p.$("kitchenPrintMessage").textContent, /Máy chủ gặp lỗi/);
    assert.equal(p.$("kitchenPrintRefresh").disabled, false);
    p.fail(false); await p.$("kitchenPrintRefresh").emit("click");
    assert.equal(p.$("kitchenPrintButton").disabled, false);
    assert.match(p.$("kitchenPrintMessage").textContent, /Chưa gửi lệnh in/);
    await p.$("kitchenPrintButton").emit("click"); assert.equal(p.prints.length, 1);
});

test("role/session guards and empty active scope prevent printing; closed orders default to all", async () => {
    for (const options of [{ roles: ["Waiter"] }, { roles: ["Cashier"] }, { roles: [] }]) {
        const p = page(options); assert.equal(await p.controller.open(10), false);
        assert.equal(p.reads.length, 0); assert.equal(p.prints.length, 0);
    }
    const locked = page(); locked.session.pending = {};
    assert.equal(await locked.controller.open(10), false); assert.equal(locked.reads.length, 0);
    const done = ticket(); done.order.status = "Paid";
    const p = page({ data: done }); await p.controller.open(10);
    assert.equal(p.$("kitchenPrintScope").value, "all");
    p.$("kitchenPrintScope").value = "active"; await p.$("kitchenPrintScope").emit("change");
    assert.equal(p.$("kitchenPrintButton").disabled, true);
    await p.$("kitchenPrintButton").emit("click"); assert.equal(p.prints.length, 0);
    assert.equal(p.reads.length, 1);
});

test("closing or losing auth during a pending GET never reopens/repopulates the preview", async () => {
    const p = page(); p.hold();
    const opening = p.controller.open(10);
    assert.equal(p.controller.isBusy(), true);
    p.controller.close(); p.release(); await opening;
    assert.equal(p.controller.isOpen(), false); assert.equal(p.controller.isBusy(), false);
    assert.equal(p.$("kitchenPrintPreview").innerHTML, ""); assert.equal(p.$("kitchenPrintPaper").innerHTML, "");
    await p.controller.open(10); p.hold();
    const printing = p.$("kitchenPrintButton").emit("click");
    p.roles([]); await p.event("restaurant:auth-changed"); p.release(); await printing;
    assert.equal(p.prints.length, 0); assert.equal(p.controller.isOpen(), false);
    assert.equal(p.$("kitchenPrintPreview").innerHTML, "");
    assert.equal(p.body.classList.contains("printing-kitchen"), false);
});

test("Escape/pagehide and print errors clear source without claiming success", async () => {
    const p = page(); await p.controller.open(10);
    await p.$("kitchenPrintDialog").emit("cancel");
    assert.equal(p.controller.isOpen(), false); assert.equal(p.$("kitchenPrintPreview").innerHTML, "");
    await p.controller.open(10); await p.$("kitchenPrintButton").emit("click");
    await p.event("pagehide");
    assert.equal(p.$("kitchenPrintPaper").innerHTML, ""); assert.equal(p.body.classList.contains("printing-kitchen"), false);
    const error = page({ printError: true }); await error.controller.open(10);
    await error.$("kitchenPrintButton").emit("click");
    assert.match(error.$("kitchenPrintMessage").textContent, /Không mở được hộp thoại in/);
    assert.doesNotMatch(error.$("kitchenPrintMessage").textContent, /Printer dialog unavailable/);
    assert.equal(error.$("kitchenPrintPaper").innerHTML, "");
    assert.equal(error.body.classList.contains("printing-kitchen"), false); assert.equal(error.controller.isBusy(), false);
});

test("ticket removed before preview reports a visible error and never prints an old snapshot", async () => {
    const p = page(); p.data(null);
    assert.equal(await p.controller.open(10), false);
    assert.match(p.$("kitchenAnnouncement").textContent, /Không tìm thấy phiếu bếp/);
    assert.equal(p.$("kitchenAnnouncement").classList.contains("is-error"), true);
    assert.equal(p.controller.isOpen(), false);
    assert.equal(p.$("kitchenPrintPreview").innerHTML, "");
    assert.equal(p.$("kitchenPrintPaper").innerHTML, "");
    assert.equal(p.prints.length, 0);
});
