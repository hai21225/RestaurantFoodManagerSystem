const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Exercise the real page and PaymentSession with GET fixtures; writes are forbidden.
class Element {
    constructor() {
        this.handlers = new Map(); this.hidden = false; this.textContent = ''; this.innerHTML = '';
        this.value = ''; this.open = false; this.disabled = false; this.attributes = new Map();
        const classes = new Set();
        this.classList = { contains: value => classes.has(value), toggle(value, enabled) { enabled ? classes.add(value) : classes.delete(value); } };
    }
    addEventListener(name, handler) { this.handlers.set(name, handler); }
    setAttribute(name, value) { this.attributes.set(name, String(value)); }
    getAttribute(name) { return this.attributes.get(name); }
    emit(name, event = {}) { return this.handlers.get(name)?.({ preventDefault() {}, currentTarget: this, ...event }); }
    focus() { this.focused = true; }
    scrollIntoView() { this.scrolled = true; }
    showModal() { this.open = true; }
    close() { this.open = false; }
}
const settle = () => new Promise(resolve => setImmediate(resolve));
const copy = value => JSON.parse(JSON.stringify(value));
function storage() { const data = new Map(); return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) }; }
async function page({ empty = true, failQueue = false, failLedger = false, hold = false, holdLedger = false, hash = '', mobile = false } = {}) {
    const nodes = new Map(), node = id => { if (!nodes.has(id)) nodes.set(id, new Element()); return nodes.get(id); };
    node('queueStatus').value = 'all';
    node('paymentQueue').open = true;
    const calls = [], writes = [], server = { empty, failQueue, failLedger, hold: null };
    let release, releaseLedger;
    if (hold) server.hold = new Promise(resolve => { release = () => { server.hold = null; resolve(); }; });
    if (holdLedger) server.holdLedger = new Promise(resolve => { releaseLedger = () => { server.holdLedger = null; resolve(); }; });
    const order = { orderId: 42, tableId: 3, status: 'Ready', subtotal: 40000, totalAmount: 40000, discountAmount: 0, taxAmount: 0, serviceChargeAmount: 0, items: [{ foodName: 'Cơm gà', sizeName: 'Phần', quantity: 1, unitPrice: 40000, status: 'Completed' }] };
    const payment = { paymentId: 9, orderId: 80, paymentMethod: 'Cash', status: 'Paid', amount: 50000, transactionCode: 'TEST-9' };
    const api = { async get(route) {
        calls.push(route);
        if (server.hold) await server.hold;
        if (route.startsWith('/payments?')) {
            if (server.holdLedger) await server.holdLedger;
            if (server.failLedger) throw Object.assign(new Error('Máy chủ gặp lỗi.'), { status: 503 });
            const query = new URL('http://fixture' + route).searchParams;
            return { items: server.empty ? [] : [copy(payment)], total: server.empty ? 0 : 25, page: Number(query.get('page')), pageSize: 20 };
        }
        if (route === '/restaurant-tables' || route.startsWith('/orders?')) {
            if (server.failQueue) throw Object.assign(new Error('Máy chủ gặp lỗi.'), { status: 503 });
            if (route === '/restaurant-tables') return [{ tableId: 3, tableNumber: '3', status: 'Occupied' }];
            const selected = new URL('http://fixture' + route).searchParams.get('status') === 'Ready';
            return { items: !server.empty && selected ? [copy(order)] : [], total: !server.empty && selected ? 1 : 0 };
        }
        if (route.startsWith('/payments/order/')) return [];
        if (route === '/orders/42') return copy(order);
        throw new Error('Unexpected GET ' + route);
    } };
    for (const method of ['post', 'patch', 'delete']) api[method] = async (...args) => { writes.push({ method, args }); throw new Error('Writes forbidden'); };
    const auth = { ready: Promise.resolve(), getUser: () => ({ userId: 8 }), hasRole: (...roles) => roles.includes('Cashier'), getToken: () => 'fixture-only' };
    const documentHandlers = new Map(), windowHandlers = new Map();
    const document = { getElementById: node, addEventListener: (name, handler) => documentHandlers.set(name, handler), hidden: false };
    const window = { restaurantApi: api, restaurantAuth: auth, sessionStorage: storage(), crypto: { randomUUID: () => 'fixture-key' },
        location: { href: 'http://localhost:5500/pages/payment.html' + hash, search: '' },
        history: { replaceState(_state, _unused, url) { window.location.href = String(url); } },
        matchMedia: () => ({ matches: mobile }),
        addEventListener: (name, handler) => windowHandlers.set(name, handler), setInterval() {}, clearInterval() {} };
    class FormData { constructor(form) { return Object.entries(form.fields || {}); } }
    const context = vm.createContext({ window, document, URL, URLSearchParams, FormData });
    for (const file of ['../js/common/payment-session.js', '../js/pages/payment.js']) vm.runInContext(fs.readFileSync(path.join(__dirname, file), 'utf8'), context);
    await settle();
    return { node, calls, writes, server, release, releaseLedger, window, windowHandlers,
        async click(id) { node(id).emit('click'); await settle(); },
        async selectOrder(id) { documentHandlers.get('click')({ target: { closest: selector => selector === '[data-select-order]' ? { dataset: { selectOrder: String(id) } } : null } }); await settle(); }
    };
}

test('initial GET failure shows unknown counts and retry guidance instead of valid empty results', async () => {
    const p = await page({ failQueue: true, failLedger: true });
    assert.equal(p.node('billCount').textContent, '— đơn');
    assert.equal(p.node('ledgerPage').textContent, '— phiếu');
    assert.match(p.node('billList').innerHTML, /Không tải được danh sách đơn.*Bấm Làm mới/);
    assert.match(p.node('paymentRows').innerHTML, /Không tải được danh sách phiếu thu.*Bấm Làm mới/);
    assert.doesNotMatch(p.node('paymentSummary').innerHTML, /<strong>0<\/strong>/);
    assert.doesNotMatch(p.node('billList').innerHTML, /Không có đơn phù hợp/);
    assert.doesNotMatch(p.node('paymentRows').innerHTML, /Không có phiếu thu theo bộ lọc/);
    assert.doesNotMatch(p.node('orderPayments').innerHTML, /Đơn chưa có phiếu thu/);
    assert.equal(p.node('confirmPaymentButton').disabled, true);
    assert.equal(p.node('refreshPaymentsButton').disabled, false);
    assert.equal(p.writes.length, 0);
});

test('successful empty GET is a real zero even without a selected order; retry clears the previous error', async () => {
    const p = await page({ failQueue: true, failLedger: true });
    p.server.failQueue = false; p.server.failLedger = false;
    await p.click('refreshPaymentsButton');
    assert.equal(p.node('billCount').textContent, '0 đơn');
    assert.equal(p.node('ledgerPage').textContent, 'Trang 1 · 0 phiếu');
    assert.match(p.node('billList').innerHTML, /Không có đơn phù hợp/);
    assert.match(p.node('paymentRows').innerHTML, /Không có phiếu thu theo bộ lọc/);
    assert.equal((p.node('paymentSummary').innerHTML.match(/<strong>0<\/strong>/g) || []).length, 3);
    assert.equal(p.node('confirmPaymentButton').disabled, true);
    assert.equal(p.node('paymentNotice').hidden, true);
    assert.equal(p.writes.length, 0);
});

test('loading hides unknown counts, locks commands and never claims an empty queue', async () => {
    const p = await page({ hold: true });
    assert.equal(p.node('billCount').textContent, '— đơn');
    assert.match(p.node('billList').innerHTML, /Đang tải danh sách đơn/);
    assert.match(p.node('paymentRows').innerHTML, /Đang tải danh sách phiếu thu/);
    assert.doesNotMatch(p.node('billList').innerHTML, /Không có đơn phù hợp/);
    assert.equal(p.node('refreshPaymentsButton').disabled, true);
    assert.equal(p.node('confirmPaymentButton').disabled, true);
    p.release(); await settle();
    assert.equal(p.node('billCount').textContent, '0 đơn');
    assert.equal(p.writes.length, 0);
});

test('queue and ledger have independent GET states when only one request fails', async () => {
    const p = await page({ empty: false, failQueue: true });
    assert.equal(p.node('billCount').textContent, '— đơn');
    assert.match(p.node('paymentRows').innerHTML, /TEST-9/);
    assert.doesNotMatch(p.node('paymentRows').innerHTML, /Không tải được/);
    assert.equal(p.node('ledgerPage').textContent, 'Trang 1 · 25 phiếu');
    assert.equal(p.node('confirmPaymentButton').disabled, true);
    assert.equal(p.writes.length, 0);
});

test('a fast queue failure does not unlock actions while the ledger GET is still pending', async () => {
    const p = await page({ empty: false, failQueue: true, holdLedger: true });
    assert.equal(p.node('billCount').textContent, '— đơn');
    assert.match(p.node('billList').innerHTML, /Không tải được danh sách đơn/);
    assert.match(p.node('paymentRows').innerHTML, /Đang tải danh sách phiếu thu/);
    assert.equal(p.node('refreshPaymentsButton').disabled, true);
    assert.equal(p.node('reviewPaymentButton').disabled, true);
    assert.equal(p.node('confirmPaymentButton').disabled, true);
    p.releaseLedger(); await settle();
    assert.equal(p.node('ledgerPage').textContent, 'Trang 1 · 25 phiếu');
    assert.equal(p.node('refreshPaymentsButton').disabled, false);
    assert.equal(p.node('confirmPaymentButton').disabled, true);
    assert.equal(p.writes.length, 0);
});

test('failed refresh retains prior rows as stale/read-only and does not unlock checkout', async () => {
    const p = await page({ empty: false });
    assert.equal(p.node('confirmPaymentButton').disabled, false);
    p.server.failQueue = true; p.server.failLedger = true;
    await p.click('refreshPaymentsButton');
    assert.match(p.node('billList').innerHTML, /Dữ liệu lần trước chỉ để tham khảo/);
    assert.match(p.node('billList').innerHTML, /data-select-order="42" disabled/);
    assert.match(p.node('paymentRows').innerHTML, /TEST-9/);
    assert.match(p.node('paymentRows').innerHTML, /Dữ liệu lần trước chỉ để tham khảo/);
    assert.match(p.node('paymentRows').innerHTML, /data-select-order="80" type="button" disabled/);
    assert.equal(p.node('confirmPaymentButton').disabled, true);
    assert.equal(p.node('ledgerNext').disabled, true);
    assert.equal(p.node('refreshPaymentsButton').disabled, false);
    assert.equal(p.writes.length, 0);
});

test('ledger-only filtering/paging errors keep queue valid; retry can recover without queue requests', async () => {
    const p = await page({ empty: false });
    const queueCalls = () => p.calls.filter(route => route === '/restaurant-tables' || route.startsWith('/orders?')).length;
    const before = queueCalls();
    p.server.failLedger = true;
    p.node('paymentFilters').fields = { status: 'Paid' };
    p.node('paymentFilters').emit('submit'); await settle();
    assert.equal(queueCalls(), before);
    assert.equal(p.node('billCount').textContent, '1 đơn');
    assert.match(p.node('paymentRows').innerHTML, /TEST-9/);
    assert.equal(p.node('ledgerPage').textContent, '— phiếu');
    assert.equal(p.node('confirmPaymentButton').disabled, true);
    p.server.failLedger = false;
    p.node('paymentFilters').emit('submit'); await settle();
    assert.equal(queueCalls(), before);
    assert.equal(p.node('ledgerPage').textContent, 'Trang 1 · 25 phiếu');
    assert.equal(p.node('confirmPaymentButton').disabled, false);
    assert.equal(p.node('paymentNotice').hidden, true);
    p.server.failLedger = true; await p.click('ledgerNext');
    assert.equal(queueCalls(), before);
    assert.match(p.node('paymentRows').innerHTML, /Dữ liệu lần trước chỉ để tham khảo/);
    assert.equal(p.node('ledgerPrev').disabled, true);
    assert.equal(p.node('confirmPaymentButton').disabled, true);
    assert.equal(p.writes.length, 0);
});

test('checkout and ledger are separate workspaces; switching tabs and keyboard navigation perform no API calls', async () => {
    const p = await page();
    const before = [...p.calls];
    assert.equal(p.node('paymentDesk').hidden, false);
    assert.equal(p.node('paymentLedger').hidden, true);
    assert.equal(p.node('checkoutTab').getAttribute('aria-selected'), 'true');
    assert.equal(p.node('paymentMethodSection').hidden, true);
    await p.click('ledgerTab');
    assert.equal(p.node('paymentDesk').hidden, true);
    assert.equal(p.node('paymentLedger').hidden, false);
    assert.equal(p.node('ledgerTab').getAttribute('aria-selected'), 'true');
    assert.equal(p.node('ledgerTab').tabIndex, 0);
    assert.equal(p.node('checkoutTab').tabIndex, -1);
    assert.equal(new URL(p.window.location.href).hash, '#paymentLedger');
    p.node('ledgerTab').emit('keydown', { key: 'ArrowLeft' });
    assert.equal(p.node('checkoutTab').focused, true);
    assert.equal(p.node('paymentDesk').hidden, false);
    p.node('checkoutTab').emit('keydown', { key: 'End' });
    assert.equal(p.node('paymentLedger').hidden, false);
    p.node('ledgerTab').emit('keydown', { key: 'Home' });
    assert.equal(p.node('paymentDesk').hidden, false);
    assert.deepEqual(p.calls, before);
    assert.equal(p.writes.length, 0);
});

test('old checkout/queue/ledger anchors open the matching workspace', async () => {
    for (const hash of ['#paymentQueue', '#paymentCheckout', '#paymentLedger']) {
        const p = await page({ hash, empty: false, mobile: true });
        assert.equal(p.node('paymentLedger').hidden, hash !== '#paymentLedger');
        assert.equal(p.node('paymentDesk').hidden, hash === '#paymentLedger');
        assert.equal(new URL(p.window.location.href).hash, hash);
        if (hash === '#paymentQueue') assert.equal(p.node('paymentQueue').open, true);
        assert.equal(p.writes.length, 0);
    }
});

test('selecting an order from the ledger returns to one invoice workspace and collapses the mobile queue', async () => {
    const p = await page({ empty: false, mobile: true });
    await p.click('ledgerTab');
    await p.selectOrder(42);
    assert.equal(p.node('paymentDesk').hidden, false);
    assert.equal(p.node('paymentLedger').hidden, true);
    assert.equal(p.node('paymentQueue').open, false);
    assert.equal(p.node('paymentCheckout').scrolled, true);
    assert.equal(p.node('paymentMethodSection').hidden, false);
    assert.match(p.node('billDetail').innerHTML, /bill-item-description/);
    assert.match(p.node('billDetail').innerHTML, /Cơm gà/);
    assert.equal(p.node('confirmPaymentButton').disabled, false);
    assert.equal(new URL(p.window.location.href).searchParams.get('orderId'), '42');
    assert.equal(new URL(p.window.location.href).hash, '#paymentCheckout');
    assert.equal(p.writes.length, 0);
});
