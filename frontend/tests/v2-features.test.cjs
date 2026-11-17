const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

class NavElement {
    constructor() {
        this.children = [];
        this.parent = null;
        this.dataset = {};
        this.attributes = {};
        this.hidden = false;
        this.className = '';
        this.text = '';
    }
    get textContent() { return this.text + this.children.map((c) => c.textContent).join(''); }
    set textContent(value) { this.text = value; this.children = []; }
    get classList() {
        const element = this;
        return {
            contains: (name) => element.className.split(/\s+/).includes(name),
            add: (name) => { if (!element.classList.contains(name)) element.className += ' ' + name; },
            toggle: (name, enabled) => {
                const selected = enabled ?? !element.classList.contains(name);
                element.className = element.className.split(/\s+/).filter((i) => i && i !== name).concat(selected ? [name] : []).join(' ');
                return selected;
            }
        };
    }
    append(...children) {
        for (const child of children) {
            if (child.parent) child.parent.children = child.parent.children.filter((i) => i !== child);
            child.parent = this;
            this.children.push(child);
        }
    }
    appendChild(child) { this.append(child); }
    after(child) {
        child.parent = this.parent;
        this.parent.children.splice(this.parent.children.indexOf(this) + 1, 0, child);
    }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    removeAttribute(name) { delete this.attributes[name]; }
    addEventListener() {}
    all() { return this.children.flatMap((c) => [c, ...c.all()]); }
    querySelector(selector) {
        const target = selector.match(/\[data-page(\$?)="([^"]+)"\]/);
        return this.all().find((item) => target && (target[1] ? item.dataset.page?.endsWith(target[2]) : item.dataset.page === target[2])) || null;
    }
    querySelectorAll(selector) {
        if (selector === ':scope > .nav-item') return this.children.filter((c) => c.classList.contains('nav-item'));
        return [];
    }
}

function makeStorage(initial = {}) {
    const data = new Map(Object.entries(initial));
    return {
        getItem: (k) => data.get(k) ?? null,
        setItem: (k, v) => data.set(k, String(v)),
        removeItem: (k) => data.delete(k)
    };
}

async function openNavPage(role, currentPage = 'account.html') {
    const navigation = new NavElement();
    const entries = [
        ['Tổng quan', '../index.html'], ['Bàn ăn', 'tables.html'], ['Đặt món', 'order.html'],
        ['Bếp', 'kitchen.html'], ['Thanh toán', 'payment.html'], ['Món ăn', 'foods.html'],
        ['Nhân viên', 'employees.html'], ['Phân quyền', 'roles.html'], ['Kho hàng', 'inventory.html'],
        ['Nhập hàng', 'purchases.html'], ['Báo cáo', 'reports.html'], ['Tài khoản', 'account.html']
    ];
    for (const [label, page] of entries) {
        const item = new NavElement();
        item.textContent = label;
        item.className = 'nav-item';
        item.dataset.page = page;
        navigation.append(item);
    }

    const bottomNav = new NavElement();
    const bottomEntries = [
        ['Tổng quan', '../index.html'], ['Bàn ăn', 'tables.html'], ['Đặt món', 'order.html'],
        ['Bếp', 'kitchen.html'], ['Thanh toán', 'payment.html']
    ];
    for (const [label, page] of bottomEntries) {
        const item = new NavElement();
        item.textContent = label;
        item.className = 'bottom-nav-item';
        item.dataset.page = page;
        bottomNav.append(item);
    }

    const user = { userId: 7, userName: 'v2user', fullName: 'V2 User', roles: [role] };
    const localStorage = makeStorage({ 'restaurant.token': 'tok.1.2', 'restaurant.currentUser': JSON.stringify(user) });
    const sessionStorage = makeStorage();
    const events = new EventTarget();
    const elements = new Map([['sidebarNav', navigation], ['bottomNav', bottomNav]]);

    const window = {
        localStorage, sessionStorage,
        location: Object.assign(new URL(`http://localhost:5500/pages/${currentPage}`), { replace() {} }),
        matchMedia: () => ({ matches: false, addEventListener() {} }),
        addEventListener: events.addEventListener.bind(events),
        dispatchEvent: events.dispatchEvent.bind(events),
        atob,
        setTimeout: (fn) => fn()
    };
    const document = {
        documentElement: new NavElement(),
        body: Object.assign(new NavElement(), { dataset: { page: currentPage }, appendChild() {} }),
        currentScript: { src: 'http://localhost:5500/js/common/auth.js' },
        getElementById: (id) => {
            if (!elements.has(id)) elements.set(id, new NavElement());
            return elements.get(id);
        },
        querySelector: (selector) => selector === '.main-nav' ? navigation : selector === '.bottom-nav' ? bottomNav : null,
        createElement: (tag) => {
            const el = new NavElement();
            el.tagName = tag;
            return el;
        },
        addEventListener: () => {}
    };

    const context = vm.createContext({
        window, document, location: window.location, URL, Headers, CustomEvent,
        Response,
        fetch: async () => new Response(JSON.stringify(user), { status: 200, headers: { 'content-type': 'application/json' } })
    });

    for (const file of ['auth.js', 'api.js', 'navigation.js']) {
        document.currentScript.src = 'http://localhost:5500/js/common/' + file;
        vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common', file), 'utf8'), context, { filename: file });
    }
    await window.restaurantAuth.ready;

    return { navigation, bottomNav };
}

async function openAuthBrowser({ url = 'http://localhost:5500/pages/reservations.html', token = 'tok.1.2', user = { userId: 10, userName: 'cust', fullName: 'Khách', roles: ['Customer'] }, bootstrap } = {}) {
    const redirects = [];
    const requests = [];
    const localStorage = makeStorage(token ? { 'restaurant.token': token, 'restaurant.currentUser': JSON.stringify(user) } : {});
    const sessionStorage = makeStorage();
    const events = new EventTarget();
    const handler = bootstrap || (async () => new Response(JSON.stringify(user), { status: 200, headers: { 'content-type': 'application/json' } }));

    const window = {
        localStorage, sessionStorage,
        location: Object.assign(new URL(url), { replace: (v) => redirects.push(String(v)) }),
        addEventListener: events.addEventListener.bind(events),
        dispatchEvent: events.dispatchEvent.bind(events),
        atob,
        setTimeout: (fn) => fn()
    };
    const document = {
        currentScript: { src: 'http://localhost:5500/js/common/auth.js' },
        body: { appendChild() {} },
        createElement: () => ({ click() {}, remove() {} })
    };
    const context = vm.createContext({
        window, document, URL, Headers, Response, CustomEvent,
        fetch: async (reqUrl, options) => {
            requests.push({ url: String(reqUrl), ...options });
            return handler(reqUrl, options);
        }
    });
    for (const file of ['auth.js', 'api.js']) {
        vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common', file), 'utf8'), context, { filename: file });
    }
    await window.restaurantAuth.ready;
    return { window, auth: window.restaurantAuth, api: window.restaurantApi, redirects, requests };
}

function findNavItem(navigation, pageName) {
    return navigation.all().find((item) => {
        const page = item.dataset?.page || '';
        return page === pageName || page.includes(`/${pageName}`) || page.includes(`${pageName}?`);
    }) || null;
}

test('navigation.js: Đặt món (order.html) is never visible in sidebar for any role', async () => {
    for (const role of ['Admin', 'Waiter', 'Cashier', 'Chef', 'WarehouseManager', 'Customer']) {
        const { navigation } = await openNavPage(role);
        const sidebarOrder = findNavItem(navigation, 'order.html');
        assert.equal(sidebarOrder?.hidden, true, `sidebar order.html should be hidden for ${role}`);
    }
});

test('navigation.js: Đặt bàn (reservations.html) is visible for Admin, Waiter, Cashier, Customer and hidden for Chef, WarehouseManager', async () => {
    for (const role of ['Admin', 'Waiter', 'Cashier', 'Customer']) {
        const { navigation } = await openNavPage(role);
        const item = findNavItem(navigation, 'reservations.html');
        assert.ok(item, `reservations.html nav item should exist for ${role}`);
        assert.equal(item.hidden, false, `reservations.html should be visible for ${role}`);
    }
    for (const role of ['Chef', 'WarehouseManager']) {
        const { navigation } = await openNavPage(role);
        const item = findNavItem(navigation, 'reservations.html');
        assert.ok(!item || item.hidden === true, `reservations.html should not be visible for ${role}`);
    }
});

test('navigation.js: Gọi món QR and Thực đơn are visible for Customer', async () => {
    const { navigation } = await openNavPage('Customer');
    const qrItem = findNavItem(navigation, 'qr-order.html');
    const menuItem = findNavItem(navigation, 'public-menu.html');
    assert.ok(qrItem, 'qr-order.html nav item should exist for Customer');
    assert.equal(qrItem.hidden, false, 'qr-order.html should be visible for Customer');
    assert.match(qrItem.textContent, /Gọi món QR/);
    assert.ok(menuItem, 'public-menu.html nav item should exist for Customer');
    assert.equal(menuItem.hidden, false, 'public-menu.html should be visible for Customer');
    assert.match(menuItem.textContent, /Thực đơn/);
});

test('auth.js: Customer home is pages/reservations.html and page access matrix is enforced', async () => {
    const b = await openAuthBrowser({ url: 'http://localhost:5500/pages/reservations.html' });
    assert.equal(b.auth.getHomeUrl(), 'http://localhost:5500/pages/reservations.html');

    for (const allowed of ['reservations.html', 'qr-order.html', 'public-menu.html', 'account.html']) {
        assert.equal(b.auth.canAccessPage(`http://localhost:5500/pages/${allowed}`), true, `Customer should be allowed on ${allowed}`);
    }
    for (const denied of ['tables.html', 'kitchen.html', 'payment.html', 'employees.html', 'reports.html']) {
        assert.equal(b.auth.canAccessPage(`http://localhost:5500/pages/${denied}`), false, `Customer should be denied on ${denied}`);
    }
});

test('api.js: public routes are anonymous without token and do not redirect', async () => {
    const b = await openAuthBrowser({
        url: 'http://localhost:5500/pages/public-menu.html',
        token: null,
        bootstrap: async (reqUrl) => new Response(JSON.stringify({ ok: true, url: String(reqUrl) }), {
            status: 200,
            headers: { 'content-type': 'application/json' }
        })
    });
    assert.equal(b.redirects.length, 0);

    const publicRoutes = [
        '/menu',
        '/foods',
        '/categories',
        '/public/tables',
        '/public/table-areas',
        '/public/promotions',
        '/auth/customer/register'
    ];

    for (const route of publicRoutes) {
        const res = route === '/auth/customer/register'
            ? await b.api.post(route, { fullName: 'Khách', userName: 'khach1', password: 'Password123' })
            : await b.api.get(route);
        assert.equal(res.ok, true, `${route} should succeed anonymously`);
    }

    assert.equal(b.requests.length, publicRoutes.length);
    for (const req of b.requests) {
        assert.equal(req.headers.get('Authorization'), null, `${req.url} should not send Authorization header`);
    }
    assert.equal(b.redirects.length, 0);
});

test('api.js: unauthenticated index.html redirects to public-menu.html', async () => {
    const b = await openAuthBrowser({
        url: 'http://localhost:5500/index.html',
        token: null
    });
    assert.equal(b.redirects.length, 1);
    assert.ok(b.redirects[0].startsWith('http://localhost:5500/pages/public-menu.html'), `Expected redirect to public-menu.html, got ${b.redirects[0]}`);
});

test('reporting-session.js: loadTopDishes and loadProfitability work cleanly with mocked api', async () => {
    const calls = [];
    const context = vm.createContext({
        window: {},
        URLSearchParams,
        Intl
    });
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common/reporting-session.js'), 'utf8'), context);
    const { ReportingSession } = context.window.restaurantReporting;

    const api = {
        get: async (route) => {
            calls.push(route);
            if (route.startsWith('/reports/top-dishes?')) {
                return [{ foodId: 1, foodName: 'Phở bò', quantity: 12, revenue: 600000 }];
            }
            if (route.startsWith('/reports/profitability?')) {
                return {
                    from: '2026-11-17T00:00:00Z',
                    to: '2026-11-17T23:59:59Z',
                    totalRevenue: 1000000,
                    totalCogs: 400000,
                    totalGrossProfit: 600000,
                    grossMarginPct: 60,
                    missingRecipeOrCostCount: 0,
                    items: [{ foodId: 1, foodName: 'Phở bò', revenue: 1000000, cogs: 400000, grossProfit: 600000 }]
                };
            }
            throw new Error(`Unexpected GET ${route}`);
        }
    };
    const auth = { hasRole: (r) => r === 'Admin' };
    const session = new ReportingSession({ api, auth });

    const top = await session.loadTopDishes({ period: 'day', date: '2026-11-17' }, 5);
    assert.equal(top.length, 1);
    assert.equal(top[0].foodName, 'Phở bò');
    assert.deepEqual(JSON.parse(JSON.stringify(session.topDishesApplied)), { period: 'day', date: '2026-11-17', limit: 5 });

    const prof = await session.loadProfitability({ period: 'day', date: '2026-11-17' });
    assert.equal(prof.totalGrossProfit, 600000);
    assert.equal(prof.grossMarginPct, 60);
    assert.deepEqual(JSON.parse(JSON.stringify(session.profitabilityApplied)), { period: 'day', date: '2026-11-17' });

    assert.equal(calls[0], '/reports/top-dishes?period=day&date=2026-11-17&limit=5');
    assert.equal(calls[1], '/reports/profitability?period=day&date=2026-11-17');
});

test('payment-session.js: openShift and closeShift work cleanly with mocked api', async () => {
    const { context, PaymentSession, storage, auth } = require('./f67-helpers.cjs');
    const calls = [];
    const api = {
        baseUrl: '/api',
        post: async (route, body) => {
            calls.push(['post', route, body]);
            if (route === '/cashier-shifts/open') {
                return { shiftId: 15, status: 'Open', openingCash: body.openingCash, expectedCash: body.openingCash };
            }
            if (route === '/cashier-shifts/15/close') {
                return { shiftId: 15, status: 'Closed', actualCash: body.actualCash, variance: body.actualCash - 500000 };
            }
            throw new Error(`Unexpected POST ${route}`);
        }
    };
    const session = new PaymentSession({ api, auth: auth('Cashier'), storage: storage() });

    const opened = await session.openShift({ openingCash: 500000, note: 'Ca sáng' });
    assert.equal(opened.shiftId, 15);
    assert.equal(session.currentShift.shiftId, 15);
    assert.equal(session.shiftLoaded, true);

    const closed = await session.closeShift(15, { actualCash: 520000, note: 'Đủ tiền' });
    assert.equal(closed.shiftId, 15);
    assert.equal(closed.variance, 20000);
    assert.equal(session.currentShift, null);

    assert.deepEqual(JSON.parse(JSON.stringify(calls[0])), ['post', '/cashier-shifts/open', { openingCash: 500000, cashierUserId: 9, note: 'Ca sáng' }]);
    assert.deepEqual(JSON.parse(JSON.stringify(calls[1])), ['post', '/cashier-shifts/15/close', { actualCash: 520000, note: 'Đủ tiền' }]);
});

test('order-extras-session.js: Waiter loads active promotions from /management/promotions/active', async () => {
    const { context, storage, auth, copy } = require('./f67-helpers.cjs');
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common/order-extras-session.js'), 'utf8'), context);
    const { OrderExtrasSession } = context.window.restaurantOrderExtras;

    const calls = [];
    const promo = { promotionId: 3, code: 'VIP10', name: 'Giảm 10%', isActive: true };
    const api = {
        baseUrl: '/api',
        get: async (route) => {
            calls.push(route);
            if (route === '/orders/1') return { orderId: 1, tableId: 1, status: 'Ready', items: [], totalAmount: 100000, promotionId: null, note: '', updatedAt: '1' };
            if (route === '/restaurant-tables') return [{ tableId: 1, isActive: true, status: 'Occupied' }];
            if (route === '/management/promotions/active') return copy([promo]);
            throw new Error(`Unexpected GET ${route}`);
        }
    };

    const session = new OrderExtrasSession({ api, auth: auth('Waiter'), storage: storage(), orderId: 1 });
    await session.refresh();
    assert.equal(session.loaded, true);
    assert.equal(session.promotions.length, 1);
    assert.equal(session.promotions[0].code, 'VIP10');
    assert.ok(calls.includes('/management/promotions/active'));
});
