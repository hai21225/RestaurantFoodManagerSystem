const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const { execFileSync } = require("node:child_process");

const sourcePath = path.join(__dirname, "../js/common/preparation-timing.js");
const context = { window: {}, Date };
vm.runInNewContext(fs.readFileSync(sourcePath, "utf8"), context);
const { parseUtc, startedForItem, orderedForItem, compareTickets, label } = context.window.restaurantPreparationTiming;
const start = "2026-11-12T09:30:00Z";
const event = (orderItemId, oldStatus, newStatus, createdAt) => ({ orderItemId, oldStatus, newStatus, createdAt });

test("UTC naive timestamps and explicit offsets identify the same instant", () => {
    assert.equal(parseUtc("2026-11-12T09:30:00"), Date.parse(start));
    assert.equal(parseUtc("2026-11-12T16:30:00+07:00"), Date.parse(start));
    assert.equal(parseUtc("2026-11-12T04:30:00-05:00"), Date.parse(start));
    assert.equal(parseUtc("2026-11-12T09:30:00.1234567"), Date.parse("2026-11-12T09:30:00.123Z"));
});

test("UTC naive parsing is independent of the browser/process timezone", () => {
    const script = `const fs=require('node:fs'),vm=require('node:vm');const c={window:{},Date};vm.runInNewContext(fs.readFileSync(process.argv[1],'utf8'),c);process.stdout.write(String(c.window.restaurantPreparationTiming.parseUtc('2026-11-12T09:30:00')));`;
    for (const timezone of ["Asia/Ho_Chi_Minh", "America/New_York"]) {
        const result = execFileSync(process.execPath, ["-e", script, sourcePath], { env: { ...process.env, TZ: timezone }, encoding: "utf8" });
        assert.equal(Number(result), Date.parse(start), timezone);
    }
});

test("invalid/missing dates are rejected instead of normalized into a cooking start", () => {
    for (const value of [null, undefined, "", " ", 0, {}, "invalid", "42", "2026-02-30T10:00:00", "2026-13-01T10:00:00Z", "2026-11-12T24:00:00Z", "2026-11-12T09:61:00Z", "2026-11-12T09:30:60Z", "2026-11-12T09:30:00+24:00"]) assert.equal(parseUtc(value), null, String(value));
    assert.equal(parseUtc("2024-02-29T00:00:00Z"), Date.parse("2024-02-29T00:00:00Z"));
    assert.equal(parseUtc("2026-02-29T00:00:00Z"), null);
});

test("partial completion does not reset the remaining item's original Preparing start", () => {
    const history = [event(4, "Preparing", "Preparing", "2026-11-12T09:35:00Z"), event(9, "Pending", "Completed", "2026-11-12T09:35:00Z"), event(4, "Pending", "Preparing", start)];
    assert.equal(startedForItem({ id: 4, status: "Preparing", orderItemGroupId: "same-group" }, history), "2026-11-12T09:30:00.000Z");
    assert.equal(startedForItem({ id: 9, status: "Completed", orderItemGroupId: "same-group" }, history), null);
});

test("reopen uses the latest genuine transition into Preparing regardless of history order", () => {
    const history = [event(4, "Completed", "Preparing", "2026-11-12T10:10:00Z"), event(4, "Pending", "Preparing", start), event(4, "Preparing", "Completed", "2026-11-12T10:00:00Z"), event(4, "Preparing", "Preparing", "2026-11-12T10:15:00Z")];
    assert.equal(startedForItem({ id: 4, status: "Preparing" }, history), "2026-11-12T10:10:00.000Z");
    assert.equal(startedForItem({ id: 4, status: "Preparing" }, history.slice().reverse()), "2026-11-12T10:10:00.000Z");
});

test("identity is per order item; no group, kitchen-ticket or sent/updated fallback", () => {
    const history = [event(4, "Pending", "Preparing", start), { ...event(null, "Pending", "Preparing", start), kitchenOrderItemId: 9 }];
    assert.equal(startedForItem({ id: 8, status: "Preparing", orderItemGroupId: "same", startedAt: start, sentAt: start, updatedAt: start }, history), null);
    assert.equal(startedForItem({ id: 99, orderItemId: "4", status: "Preparing" }, history), "2026-11-12T09:30:00.000Z");
    assert.equal(startedForItem({ id: 4, orderItemId: 0, status: "Preparing" }, history), null);
    assert.equal(startedForItem({ id: 4, status: "Preparing" }, [{ ...event(4, "Pending", "Preparing", "broken") }]), null);
    assert.equal(startedForItem({ id: 4, status: "Preparing" }, null), null);
});

test("valid explicit backend preparationStartedAt wins; done/pending/cancelled never get an active clock", () => {
    const history = [event(4, "Pending", "Preparing", start)];
    assert.equal(startedForItem({ id: 4, status: "Preparing", preparationStartedAt: "2026-11-12T17:00:00+07:00" }, history), "2026-11-12T10:00:00.000Z");
    assert.equal(startedForItem({ id: 4, status: "Preparing", preparationStartedAt: "invalid" }, history), "2026-11-12T09:30:00.000Z");
    for (const status of ["Pending", "Ready", "Completed", "Cancelled", "Accepted"]) assert.equal(startedForItem({ id: 4, status, preparationStartedAt: start }, history), null, status);
});

test("elapsed label uses MM:SS then H:MM:SS and never invents a duration for future/unknown starts", () => {
    const epoch = Date.parse(start);
    assert.equal(label(start, epoch), "00:00");
    assert.equal(label(start, epoch + 62399), "01:02");
    assert.equal(label(start, epoch + 3599999), "59:59");
    assert.equal(label(start, epoch + 3600000), "1:00:00");
    assert.equal(label(start, epoch + 25 * 3600000 + 125000), "25:02:05");
    assert.equal(label(start, epoch - 1), null);
    for (const value of [null, undefined, "", "broken", "2026-02-30T00:00:00"]) assert.equal(label(value, epoch), null);
    for (const now of [NaN, Infinity, -Infinity, "123", null, Number.MAX_VALUE]) assert.equal(label(start, now), null);
});

test("ordered time comes from the earliest genuine creation audit for that exact item", () => {
    const history = [event(4, "", "Pending", "2026-11-12T10:00:00Z"), event(4, "Pending", "Pending", "2026-11-12T09:00:00Z"),
        event(7, "", "Pending", "2026-11-12T08:00:00Z"), event(4, "", "Pending", start), event(4, "Pending", "Preparing", "2026-11-12T09:35:00Z")];
    assert.equal(orderedForItem({ id: 4, status: "Preparing" }, history), "2026-11-12T09:30:00.000Z");
    assert.equal(orderedForItem({ id: 99, orderItemId: "4", status: "Pending" }, history.slice().reverse()), "2026-11-12T09:30:00.000Z");
    assert.equal(orderedForItem({ id: 4 }, [event(4, "", "Pending", "invalid"), ...history]), "2026-11-12T09:30:00.000Z");
});

test("explicit orderedAt/placedAt fields take precedence without using item createdAt", () => {
    const history = [event(4, "", "Pending", start)];
    assert.equal(orderedForItem({ id: 4, orderedAt: "2026-11-12T17:00:00+07:00", placedAt: start }, history), "2026-11-12T10:00:00.000Z");
    assert.equal(orderedForItem({ id: 4, orderedAt: "invalid", placedAt: "2026-11-12T09:40:00" }, history), "2026-11-12T09:40:00.000Z");
    assert.equal(orderedForItem({ id: 4, orderedAt: null, placedAt: "invalid" }, history), "2026-11-12T09:30:00.000Z");
});

test("legacy and split lines with no creation audit stay unknown instead of inheriting group or clone timestamps", () => {
    const history = [event(4, "", "Pending", start), event(9, "Pending", "Completed", "2026-11-12T10:00:00Z"),
        event(10, "Pending", "Pending", "2026-11-12T10:10:00Z"), event(11, null, "Pending", start), event(12, undefined, "Pending", start)];
    for (const id of [9, 10, 11, 12]) assert.equal(orderedForItem({ id, orderItemGroupId: "same", createdAt: "2026-11-12T10:00:00Z", sentAt: start, updatedAt: start }, history), null);
    assert.equal(orderedForItem({ id: 4 }, []), null);
    assert.equal(orderedForItem({ id: 4 }, null), null);
    assert.equal(orderedForItem(null, history), null);
    assert.equal(orderedForItem({ id: 4, orderItemId: 0 }, history), null);
});

test("FIFO defaults to oldest sentAt regardless of priority, then numeric ticket id, with unknown dates last", () => {
    const rows = [{ kitchenOrderId: 3, priority: 5, sentAt: "2026-11-12T10:00:00Z" },
        { kitchenOrderId: "10", priority: 0, sentAt: start }, { kitchenOrderId: "2", priority: 0, sentAt: start },
        { kitchenOrderId: 1, priority: 10, sentAt: null }, { kitchenOrderId: 4, priority: 0, sentAt: "invalid" }];
    const before = JSON.stringify(rows);
    assert.deepEqual(rows.slice().sort(compareTickets).map(row => Number(row.kitchenOrderId)), [2, 10, 3, 1, 4]);
    assert.deepEqual(rows.slice().sort((a, b) => compareTickets(a, b, "unknown-mode")).map(row => Number(row.kitchenOrderId)), [2, 10, 3, 1, 4]);
    assert.equal(JSON.stringify(rows), before, "Comparator does not modify input tickets");
});

test("priority mode groups positive priority tickets, keeps FIFO within a group and does not invent numeric priority ranking", () => {
    const rows = [{ kitchenOrderId: 1, priority: 0, sentAt: "2026-11-12T08:00:00Z" },
        { kitchenOrderId: 2, priority: 1, sentAt: start }, { kitchenOrderId: 3, priority: 5, sentAt: "2026-11-12T10:00:00Z" },
        { kitchenOrderId: 4, priority: -1, sentAt: "2026-11-12T08:30:00Z" }, { kitchenOrderId: 5, priority: 9, sentAt: "invalid" }];
    assert.deepEqual(rows.slice().sort((a, b) => compareTickets(a, b, "priority")).map(row => row.kitchenOrderId), [2, 3, 1, 4, 5]);
    assert.ok(compareTickets(rows[1], rows[2], "priority") < 0, "Priority 1 does not jump behind priority 5 merely because its number is smaller");
});

test("ticket comparator remains deterministic and antisymmetric with malformed dates, IDs and priorities", () => {
    const rows = [null, {}, { kitchenOrderId: "bad", priority: true }, { kitchenOrderId: "2", priority: "3", sentAt: start },
        { kitchenOrderId: 1, priority: Infinity, sentAt: start }, { kitchenOrderId: 4, priority: 1, sentAt: "2026-02-30T09:00:00Z" }];
    for (const mode of ["oldest", "priority"]) for (const a of rows) for (const b of rows) {
        const result = compareTickets(a, b, mode), reverse = compareTickets(b, a, mode);
        assert.equal(Number.isFinite(result), true);
        assert.equal(result === 0 ? reverse === 0 : Math.sign(result) === -Math.sign(reverse), true);
    }
});
