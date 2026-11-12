const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

// Exercise page event handlers without an API or database connection.
async function page(name, query = "", { failPurchase = false, failRecipe = false } = {}) {
    const html = fs.readFileSync(path.join(__dirname, `../pages/${name}.html`), "utf8");
    const elements = new Map();
    let focused = "", instance;
    class Element {
        constructor(id) {
            this.id = id; this.value = ""; this.innerHTML = ""; this.textContent = ""; this.disabled = false;
            this.hidden = /\bhidden\b/.test(html.match(new RegExp(`<[^>]*\\bid="${id}"[^>]*>`))?.[0] || "");
            this.handlers = {}; this.attributes = {}; this.dataset = {}; this.open = false;
            const classes = new Set();
            this.classList = { add: (v) => classes.add(v), remove: (v) => classes.delete(v), contains: (v) => classes.has(v), toggle: (v, on) => on ? classes.add(v) : classes.delete(v) };
            this.elements = { namedItem: (key) => formFields[key] };
        }
        addEventListener(type, fn) { (this.handlers[type] ||= []).push(fn); }
        async emit(type, event = {}) { for (const fn of this.handlers[type] || []) await fn(event); await settle(); }
        setAttribute(k, v) { this.attributes[k] = v; }
        scrollIntoView() { this.scrolled = true; }
        focus() { focused = this.id; }
        showModal() { this.open = true; }
        close() { this.open = false; }
        reset() { if (this.id === "ledgerFilters") for (const field of Object.values(formFields)) field.value = ""; }
        querySelector(selector) {
            const attribute = selector.match(/^\[([^\]]+)\]$/)?.[1];
            return attribute && this.innerHTML.includes(attribute) ? new Element("generated") : null;
        }
        querySelectorAll() { return []; }
    }
    const formFields = Object.fromEntries(["ingredientId", "referenceType", "referenceId", "type", "from", "to"].map((key) => [key, { value: "" }]));
    for (const [, id] of html.matchAll(/\bid="([^"]+)"/g)) elements.set(id, new Element(id));
    if (elements.has("ledgerIngredient")) formFields.ingredientId = elements.get("ledgerIngredient");
    const $ = (id) => { assert.ok(elements.has(id), `Page element #${id} exists`); return elements.get(id); };
    if (elements.has("inventoryView")) $("inventoryView").value = "all";
    const documentHandlers = {};
    const detail = (id) => ({ id, supplierId: 1, supplierName: "NCC A", status: id === 11 ? "Received" : "Draft", totalAmount: 120000, createdBy: 1, orderedAt: "2026-11-11T10:00:00Z", items: [] });
    class WarehouseSession {
        constructor() {
            instance = this;
            this.loaded = true; this.loading = false; this.busy = false; this.pending = null;
            this.inventory = [{ ingredientId: 2, quanlity: 12.5, updatedAt: "2026-11-11T10:00:00Z" }, { ingredientId: 3, quanlity: 2, updatedAt: null }];
            this.ingredients = [{ ingredientId: 2, name: "Gạo <A>", unit: "kg", minimumStock: 5, stockStatus: "Available", isActive: true }, { ingredientId: 3, name: "Thịt bò", unit: "kg", minimumStock: 4, stockStatus: "Spoiled", isActive: true }];
            this.lowStock = [{ ingredientId: 3, stockStatus: "Spoiled", shortage: 2, unit: "kg" }];
            this.ledgerFilters = {}; this.ledger = { items: [], page: 1, pageSize: 20, total: 0 };
            this.purchases = [detail(10), detail(11)]; this.suppliers = [{ supplierId: 1, name: "NCC A", isActive: true }];
            this.purchase = null; this.reads = []; this.commands = [];
        }
        assertAccess() {}
        async refresh() {}
        async loadLedger(filters) { this.reads.push(["ledger", filters]); this.ledgerFilters = filters; }
        async selectPurchase(id) {
            this.reads.push(["purchase", id]); this.purchase = null; this.loading = true;
            try {
                if (failPurchase) throw new Error("Không tải được chi tiết phiếu nhập.");
                this.purchase = detail(id); return this.purchase;
            } finally { this.loading = false; }
        }
        async command(...args) { this.commands.push(args); throw Error("Writes must not be called by a view action"); }
    }
    class CatalogSession {
        constructor() {
            instance = this;
            this.loaded = true; this.loading = false; this.detailLoading = false; this.busy = false; this.pending = null;
            this.ingredients = []; this.detail = null; this.reads = []; this.commands = [];
            this.foods = [{ foodId: 1, foodName: "Cơm gà", isActive: true }];
            this.sizes = [{ sizeId: 11, foodId: 1, foodName: "Cơm gà", sizeName: "Phần", isActive: true, foodIsActive: true }];
            this.recipes = [{ recipeId: 31, sizeId: 11, name: "Công thức cơm gà", version: "1", isActive: true, ingredientCount: 0 }];
        }
        hasAccess() { return true; }
        async refresh() {}
        activeRecipe(id) { return this.recipes.find(recipe => recipe.sizeId === id && recipe.isActive); }
        async selectRecipe(id) {
            this.reads.push(["recipe", id]); this.detail = null; this.detailLoading = true;
            try {
                if (failRecipe) throw new Error("Không tải được định lượng công thức.");
                this.detail = { ...this.recipes.find(recipe => recipe.recipeId === id), ingredients: [] };
                return this.detail;
            } finally { this.detailLoading = false; }
        }
        async command(...args) { this.commands.push(args); throw Error("Writes must not be called by a view action"); }
    }
    const esc = (v) => String(v ?? "").replace(/</g, "&lt;").replace(/>/g, "&gt;");
    const ui = { createPage(session, render) {
        function message(text = "", error = false, target = "uiMessage") { $(target).textContent = text; $(target).classList.toggle("is-error", error); }
        return { $, esc, quantity: (v) => String(v ?? "—"), time: (v) => v || "—", money: (v) => `${v}đ`, locked: () => session.busy || session.loading || !!session.pending || !session.loaded,
            render, message, async run(fn) { try { const out = fn(); render(); return await out; } catch (error) { message(error.message, true); } finally { render(); } } };
    } };
    const url = `http://localhost/pages/${name}.html${query}`;
    const window = { restaurantWarehouse: { WarehouseSession, stockSnapshot: () => "snapshot", supplierSnapshot: () => "snapshot", purchaseSnapshot: () => "snapshot" }, restaurantWarehouseUi: ui,
        restaurantCatalog: { CatalogSession, ingredientSnapshot: () => "snapshot", recipeSnapshot: () => "snapshot", stockStatuses: { Available: "Bình thường" } },
        restaurantAuth: { ready: Promise.resolve(), getUser: () => ({ userId: 1 }) }, location: { search: query, href: url }, history: { replaceState() {} }, matchMedia: () => ({ matches: true }) };
    const document = { getElementById: $, querySelectorAll: () => [], addEventListener(type, fn) { (documentHandlers[type] ||= []).push(fn); } };
    class FormData { constructor() { this.entries = Object.entries(formFields).map(([key, field]) => [key, field.value]); } [Symbol.iterator]() { return this.entries[Symbol.iterator](); } }
    vm.runInNewContext(fs.readFileSync(path.join(__dirname, `../js/pages/${name}.js`), "utf8"), { window, document, URL, URLSearchParams, FormData, Intl, Date });
    await settle();
    return { $, session: instance, formFields, focused: () => focused, async clickDocument(button) { for (const fn of documentHandlers.click || []) await fn({ target: { closest: () => button } }); await settle(); } };
}
async function settle() { await new Promise((resolve) => setImmediate(resolve)); }
const target = (selector, dataset) => ({ closest: (value) => value === selector ? { dataset } : null });

test("inventory deep links open history and retain document filters", async () => {
    const { $, formFields, session } = await page("inventory", "?referenceType=PurchaseOrder&referenceId=4&ingredientId=2");
    assert.equal($("stockWorkspace").hidden, true);
    assert.equal($("ledgerSection").hidden, false);
    assert.equal($("ledgerTab").attributes["aria-selected"], "true");
    assert.equal(formFields.referenceType.value, "PurchaseOrder");
    assert.equal(formFields.referenceId.value, "4");
    assert.equal(formFields.ingredientId.value, "2");
    assert.equal(session.commands.length, 0);
});

test("selecting stock opens the ingredient detail sheet without sending a command", async () => {
    const { $, session, focused } = await page("inventory");
    await $("inventoryList").emit("click", { target: target("[data-stock]", { stock: "3" }) });
    assert.match($("stockDetail").innerHTML, /Thịt bò/);
    assert.match($("stockDetail").innerHTML, /data-adjust="3"/);
    assert.equal($("stockDetailPanel").classList.contains("is-open"), true);
    assert.equal(focused(), "closeStockDetail");
    assert.equal(session.commands.length, 0);
    assert.equal(session.reads.length, 0);
    assert.match($("inventoryList").innerHTML, /Gạo &lt;A&gt;/);
    await $("closeStockDetail").emit("click");
    assert.equal($("stockDetailPanel").classList.contains("is-open"), false);
});

test("ingredient history switches workspace and reads only the selected ingredient", async () => {
    const { $, session, formFields } = await page("inventory");
    await $("stockDetail").emit("click", { target: target("[data-history]", { history: "2" }) });
    assert.equal($("ledgerIngredient").value, "2");
    assert.equal($("ledgerSection").hidden, false);
    assert.equal($("stockWorkspace").hidden, true);
    assert.equal(session.reads[0][0], "ledger");
    assert.equal(session.reads[0][1].ingredientId, "2");
    assert.equal(session.commands.length, 0);
    assert.equal(formFields.referenceId.value, "");
});

test("inventory tab keyboard controls change panels without loading or writing data", async () => {
    const { $, session } = await page("inventory");
    let prevented = false;
    await $("stockTab").emit("keydown", { key: "ArrowRight", preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    assert.equal($("ledgerSection").hidden, false);
    await $("ledgerTab").emit("keydown", { key: "Home", preventDefault() {} });
    assert.equal($("stockWorkspace").hidden, false);
    assert.equal(session.reads.length, 0);
    assert.equal(session.commands.length, 0);
});

test("purchase and supplier tabs separate workspaces without issuing new API actions", async () => {
    const { $, session } = await page("purchases");
    session.reads.length = 0;
    await $("suppliersTab").emit("click");
    assert.equal($("purchasesWorkspace").hidden, true);
    assert.equal($("suppliersWorkspace").hidden, false);
    assert.equal($("suppliersTab").attributes["aria-selected"], "true");
    await $("purchasesTab").emit("click");
    assert.equal($("suppliersWorkspace").hidden, true);
    assert.equal(session.reads.length, 0);
    assert.equal(session.commands.length, 0);
});

test("selecting a received purchase reads detail and keeps receive/cancel disabled", async () => {
    const p = await page("purchases");
    p.session.reads.length = 0;
    await p.clickDocument({ dataset: { purchase: "11" } });
    assert.equal(p.session.reads[0][0], "purchase");
    assert.equal(p.session.reads[0][1], 11);
    assert.equal(p.$("purchaseDetailPanel").classList.contains("is-open"), true);
    assert.match(p.$("purchaseDetail").innerHTML, /data-receive="11" disabled/);
    assert.match(p.$("purchaseDetail").innerHTML, /data-cancel-purchase="11" disabled/);
    assert.equal(p.session.commands.length, 0);
});

test("failed purchase detail GET closes the sheet and brings its page error into view", async () => {
    const p = await page("purchases", "", { failPurchase: true });
    await p.clickDocument({ dataset: { purchase: "11" } });
    assert.equal(p.$("purchaseDetailPanel").classList.contains("is-open"), false);
    assert.match(p.$("uiMessage").textContent, /Không tải được chi tiết phiếu nhập/);
    assert.equal(p.$("uiMessage").scrolled, true);
    assert.equal(p.focused(), "uiMessage");
    assert.equal(p.session.commands.length, 0);
});

test("failed active recipe detail GET closes its sheet and exposes the load error", async () => {
    const p = await page("recipes", "?tab=recipes", { failRecipe: true });
    await p.$("recipeRows").emit("click", { target: target("button", { viewSize: "11" }) });
    assert.equal(p.$("recipeDetailPanel").classList.contains("is-open"), false);
    assert.match(p.$("catalogNotice").textContent, /Không tải được định lượng công thức/);
    assert.equal(p.$("catalogNotice").scrolled, true);
    assert.equal(p.focused(), "catalogNotice");
    assert.equal(p.session.commands.length, 0);
});

test("successful recipe selection keeps the sheet open with the requested version", async () => {
    const p = await page("recipes", "?tab=recipes");
    await p.$("recipeRows").emit("click", { target: target("button", { viewSize: "11" }) });
    assert.equal(p.$("recipeDetailPanel").classList.contains("is-open"), true);
    assert.match(p.$("recipeDetail").innerHTML, /Công thức cơm gà/);
    assert.match(p.$("recipeDetail").innerHTML, /Phiên bản 1/);
    assert.equal(p.session.commands.length, 0);
});
