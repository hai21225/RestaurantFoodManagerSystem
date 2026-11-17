const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// DOM tối thiểu để chạy nguyên script trang và kiểm tra đường ghi thực tế.
class Element {
    constructor() {
        this.handlers = new Map(); this.hidden = false; this.innerHTML = ''; this.textContent = ''; this.open = false;
        this.classList = { add() {}, remove() {} };
        this.elements = { areaId: {}, capacity: {}, tableNumber: { focus() {} } };
    }
    addEventListener(name, handler) { this.handlers.set(name, handler); }
    emit(name, event = {}) { return this.handlers.get(name)?.({ preventDefault() {}, ...event }); }
    querySelectorAll() { return []; }
    checkValidity() { return true; }
    reportValidity() {}
    reset() {}
    focus() {}
    showModal() { this.open = true; }
    close() { this.open = false; }
}

async function page(initialRoles, initiallyAllowed = true) {
    let roles = initialRoles;
    let allowed = initiallyAllowed;
    const nodes = new Map();
    const node = selector => {
        if (!nodes.has(selector)) nodes.set(selector, new Element());
        return nodes.get(selector);
    };
    const events = new Map();
    const writes = [];
    const rows = [
        { tableId: 3, tableNumber: 'THỬ 03', areaId: 1, areaName: 'Sảnh thử', capacity: 4, isActive: true, status: 'Available' },
        { tableId: 4, tableNumber: 'THỬ 04', areaId: 1, areaName: 'Sảnh thử', capacity: 4, isActive: true, status: 'Occupied' },
        { tableId: 5, tableNumber: 'THỬ 05', areaId: 1, areaName: 'Sảnh thử', capacity: 4, isActive: false, status: 'Available' }
    ];
    const auth = { isPageAllowed: () => allowed, hasRole: (...expected) => roles.some(role => expected.includes(role)), ready: Promise.resolve() };
    const api = { get: async route => route === '/restaurant-tables' ? rows : [{ areaId: 1, areaName: 'Sảnh thử', isActive: true }] };
    for (const method of ['post', 'patch', 'delete']) api[method] = async (...args) => { writes.push({ method, args }); };
    const window = {
        restaurantAuth: auth, restaurantApi: api,
        tablesData: { areas: [], tables: [], statuses: ['active','all','empty','serving','inactive','deleted'].map(value => ({ value, label: value })) },
        addEventListener(name, handler) { events.set(name, handler); },
        localStorage: { setItem() {} }, location: { href: 'tables.html' }
    };
    const document = { querySelector: node, getElementById: id => node('#' + id), querySelectorAll: () => [] };
    const context = vm.createContext({ window, document, FormData: class { get(key) { return { tableNumber:'NEW', capacity:'4', areaId:'1' }[key]; } } });
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/pages/tables.js'), 'utf8'), context);
    await new Promise(resolve => setImmediate(resolve));
    return { node, writes, window, changeRoles(nextRoles, pageAllowed = true) {
        roles = nextRoles; allowed = pageAllowed; events.get('restaurant:auth-changed')?.();
    }, action(name) {
        return node('#selectedTable').emit('click', { target: { closest(selector) {
            if (selector === '[data-action="order-table"]') return name === 'order-table' ? {} : null;
            return { dataset: { action: name } };
        } } });
    } };
}

test('Phục vụ không thấy quản lý bàn, bàn đã xóa hoặc số liệu 0đ/chưa có đơn giả', async () => {
    const p = await page(['Waiter']);
    assert.equal(p.node('#addTableButton').hidden, true);
    assert.equal(p.node('#manageTablesButton').hidden, true);
    assert.doesNotMatch(p.node('#selectedTable').innerHTML, /delete-table|maintenance-table|restore-table|0đ|Khách hiện tại/);
    assert.match(p.node('#selectedTable').innerHTML, /order-table/);
    assert.doesNotMatch(p.node('#restaurantMap').innerHTML, /THỬ 05|0 khách|Chưa có đơn|Mở đơn để xem món/);
    assert.doesNotMatch(p.node('#statusFilters').innerHTML, /value="(?:all|deleted)"/);
    p.node('#statusFilters').value = 'empty';
    await p.node('#statusFilters').emit('change');
    assert.match(p.node('#restaurantMap').innerHTML, /THỬ 03/);
    assert.doesNotMatch(p.node('#restaurantMap').innerHTML, /THỬ 04/);
    p.node('#areaFilters').value = '99';
    await p.node('#areaFilters').emit('change');
    assert.equal(p.node('#visibleTableCount').textContent, '0 bàn');
});

test('Phục vụ không mở form quản lý và không gửi POST/PATCH/DELETE kể cả sự kiện bị gọi trực tiếp', async () => {
    const p = await page(['Waiter']);
    await p.node('#addTableButton').emit('click');
    await p.node('#tableForm').emit('submit');
    for (const action of ['delete-table','restore-table','maintenance-table','end-maintenance']) {
        await p.action(action); await p.node('#tableLifecycleConfirm').emit('click');
    }
    assert.equal(p.node('#tableDialog').open, false);
    assert.equal(p.node('#tableLifecycleDialog').open, false);
    assert.equal(p.writes.length, 0);
});

test('Admin vẫn thêm bàn và bảo trì theo API hiện có', async () => {
    const p = await page(['Admin']);
    assert.equal(p.node('#addTableButton').hidden, false);
    assert.equal(p.node('#manageTablesButton').hidden, false);
    assert.match(p.node('#selectedTable').innerHTML, /maintenance-table|delete-table/);
    await p.node('#addTableButton').emit('click');
    assert.equal(p.node('#tableDialog').open, true);
    await p.node('#tableForm').emit('submit');
    await p.action('maintenance-table');
    assert.equal(p.node('#tableLifecycleDialog').open, true);
    await p.node('#tableLifecycleConfirm').emit('click');
    assert.deepEqual(p.writes.map(write => [write.method, write.args[0]]), [
        ['post','/restaurant-tables'], ['patch','/restaurant-tables/3/maintenance?enabled=true']
    ]);
});

test('Quyền đổi khi form đang mở: đóng form và chặn lệnh đang chuẩn bị xác nhận', async () => {
    const p = await page(['Admin']);
    await p.node('#addTableButton').emit('click'); await p.action('delete-table');
    p.changeRoles(['Waiter']);
    assert.equal(p.node('#tableDialog').open, false);
    assert.equal(p.node('#tableLifecycleDialog').open, false);
    await p.node('#tableForm').emit('submit'); await p.node('#tableLifecycleConfirm').emit('click');
    assert.equal(p.writes.length, 0);
});

test('Admin chưa xác minh màn cũng bị chặn; nhiều vai trò giữ quyền hợp lệ của Admin', async () => {
    const p = await page(['Admin'], false);
    await p.node('#tableForm').emit('submit'); await p.node('#tableLifecycleConfirm').emit('click');
    assert.equal(p.writes.length, 0);
    assert.equal(p.node('#addTableButton').hidden, true);
    p.changeRoles(['Cashier','Waiter','Admin']);
    assert.equal(p.node('#addTableButton').hidden, false);
    await p.node('#tableForm').emit('submit'); assert.equal(p.writes.length, 1);
});
