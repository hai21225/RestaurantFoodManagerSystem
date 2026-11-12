const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const settle = () => new Promise(resolve => setImmediate(resolve));
const copy = value => JSON.parse(JSON.stringify(value));

class Element {
    constructor() {
        this.handlers = new Map(); this.attributes = new Map(); this.value = ''; this.innerHTML = ''; this.textContent = '';
        this.disabled = false; this.hidden = false; this.open = false; this.checked = true; this.children = [];
        const classes = new Set();
        this.classList = {
            add: key => classes.add(key), remove: key => classes.delete(key), contains: key => classes.has(key),
            toggle(key, enabled) { if (enabled) classes.add(key); else classes.delete(key); }
        };
    }
    addEventListener(name, handler) { this.handlers.set(name, handler); }
    emit(name, event = {}) { return this.handlers.get(name)?.({ preventDefault() {}, ...event }); }
    setAttribute(key, value) { this.attributes.set(key, value); }
    querySelectorAll() { return []; }
    querySelector() { return null; }
    appendChild(child) {
        if (child.parentElement) child.parentElement.children = child.parentElement.children.filter(node => node !== child);
        this.children.push(child); child.parentElement = this;
    }
    focus() { this.focused = true; }
    scrollIntoView() {}
    reset() {}
    showModal() { this.open = true; }
    close() { this.open = false; }
}
async function page(name, { mobile = false, role = 'Admin' } = {}) {
    const nodes = new Map();
    const node = id => { if (!nodes.has(id)) nodes.set(id, new Element()); return nodes.get(id); };
    const requests = [], downloads = [], writes = [], buttons = ['day', 'week', 'month'].map(period => {
        const button = new Element(); button.dataset = { reportPeriod: period }; return button;
    });
    node('reportPeriod').value = 'day'; node('reportDate').value = '2026-10-31';
    for (const id of ['employeeRoleFilter', 'employeeStatusFilter']) node(id).value = 'all';
    node('employeeDetailRail').appendChild(node('employeeDetailPanel'));
    const user = { userId: 3, userName: 'vanan', fullName: 'Nguyễn Văn An', email: 'an@example.com', phone: '0901234567', isActive: true, roles: ['Waiter'] };
    let hold, finish;
    const api = { async get(route) {
        requests.push(route);
        if (route.startsWith('/reports/revenue')) {
            if (hold) await hold;
            return { period: new URL('http://fixture' + route).searchParams.get('period'), from: '2026-10-30T17:00:00Z', to: '2026-10-31T17:00:00Z', totalRevenue: 87913, orderCount: 3, averagePerOrder: 12345.67,
                groups: [{ label: '23:00', revenue: 5000, orderCount: 1 }] };
        }
        if (route === '/reports/dashboard') return { revenueToday: 87913, paidOrderCountToday: 3, openOrderCount: 8, lowStockCount: 2, businessDate: '2026-10-31', generatedAt: '2026-10-31T03:00:00Z',
            tables: { total: 2, available: 1, occupied: 1, maintenance: 0 }, kitchen: { open: 2, pending: 1, preparing: 1, ready: 0 }, recentOrders: [] };
        if (route === '/restaurant-tables') return [];
        if (route === '/users') return [copy(user)];
        if (route === '/users/3') return copy(user);
        if (route === '/roles') return [{ roleId: 1, roleName: 'Waiter', roleDescription: 'Phục vụ tại bàn' }];
        throw new Error('Unexpected GET ' + route);
    }, async download(route) { downloads.push(route); return 'report.csv'; } };
    for (const method of ['post', 'put', 'patch', 'delete']) api[method] = async (...args) => { writes.push({ method, args }); throw new Error('Unexpected write'); };
    const auth = { ready: Promise.resolve(), hasRole: (...roles) => roles.includes(role), getUser: () => ({ userId: 9, roles: [role] }) };
    const values = new Map();
    const media = { matches: mobile, addEventListener() {} };
    const location = { href: `http://localhost:5500/pages/${name}.html`, search: '?date=2026-10-31' };
    const window = { restaurantApi: api, restaurantAuth: auth, location, history: { replaceState() {} }, matchMedia: () => media,
        sessionStorage: { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) } };
    const document = { getElementById: node, querySelectorAll: selector => selector === '[data-report-period]' ? buttons : [] };
    const context = vm.createContext({ window, document, location, URL, URLSearchParams, TextEncoder });
    const common = name === 'employees' ? 'staff-session' : 'reporting-session';
    for (const file of [`../js/common/${common}.js`, `../js/pages/${name}.js`]) vm.runInContext(fs.readFileSync(path.join(__dirname, file), 'utf8'), context);
    await settle();
    return { node, buttons, requests, downloads, writes,
        hold() { hold = new Promise(resolve => { finish = resolve; }); }, release() { hold = null; finish(); } };
}

test('Dashboard hero và chỉ số phụ dùng nguyên số liệu server, không thêm tăng trưởng/ca giả', async () => {
    const p = await page('dashboard');
    const html = p.node('metricGrid').innerHTML;
    assert.match(html, /dashboard-revenue-hero/);
    assert.match(html, /87[.,]913đ/);
    assert.equal([...html.matchAll(/class="dashboard-key-metric"/g)].length, 3);
    assert.match(html, /Đơn đang mở/); assert.match(html, />8<\/strong>/);
    assert.doesNotMatch(html, /tăng trưởng|ca làm|%/i);
    assert.deepEqual(p.requests, ['/reports/dashboard', '/restaurant-tables']);
    assert.equal(p.writes.length, 0);
});

test('Nút kỳ báo cáo tải đúng API, khóa bấm kép và không xuất khi ngày chưa áp dụng', async () => {
    const p = await page('reports');
    assert.equal(p.buttons[0].attributes.get('aria-pressed'), 'true');
    p.hold(); p.buttons[1].emit('click');
    assert.match(p.requests.at(-1), /period=week&date=2026-10-31$/);
    assert.ok(p.buttons.every(button => button.disabled));
    const requests = p.requests.length;
    p.buttons[2].emit('click'); assert.equal(p.requests.length, requests);
    p.release(); await settle();
    assert.equal(p.buttons[1].attributes.get('aria-pressed'), 'true');
    assert.equal(p.node('exportRevenue').disabled, false);
    assert.match(p.node('revenueSummary').innerHTML, /87[.,]913đ/);
    p.node('reportDate').value = '2026-11-01'; p.node('reportDate').emit('input');
    assert.equal(p.node('exportRevenue').disabled, true);
    await p.node('exportRevenue').emit('click');
    assert.equal(p.downloads.length, 0); assert.equal(p.writes.length, 0);
});

test('Danh bạ hiển thị tên/liên hệ/vai trò thật, chọn mobile mở sheet và đóng không ghi dữ liệu', async () => {
    const p = await page('employees', { mobile: true });
    assert.match(p.node('employeeList').innerHTML, /staff-avatar[^>]*>VA</);
    assert.match(p.node('employeeList').innerHTML, /an@example\.com/);
    assert.match(p.node('employeeList').innerHTML, /Phục vụ/);
    assert.equal(p.node('employeeDetailPanel').parentElement, p.node('employeeDetailSheet'));
    p.node('employeeList').emit('click', { target: { closest: () => ({ dataset: { viewUser: '3' } }) } });
    await settle();
    assert.equal(p.requests.at(-1), '/users/3');
    assert.equal(p.node('employeeDetailSheet').open, true);
    assert.match(p.node('employeeDetail').innerHTML, /Nguyễn Văn An/);
    p.node('employeeDetailClose').emit('click');
    assert.equal(p.node('employeeDetailSheet').open, false);
    assert.equal(p.writes.length, 0);
});
