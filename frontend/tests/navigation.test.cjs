const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

// DOM tối thiểu để chạy ba script thật với đúng thứ tự tải và sự kiện bootstrap.
class Element {
    constructor() { this.children = []; this.parent = null; this.dataset = {}; this.attributes = {}; this.hidden = false; this.className = ''; this.text = ''; }
    get textContent() { return this.text + this.children.map(child => child.textContent).join(''); }
    set textContent(value) { this.text = value; this.children = []; }
    get classList() {
        const element = this;
        return {
            contains: name => element.className.split(/\s+/).includes(name),
            add: name => { if (!element.classList.contains(name)) element.className += ' ' + name; },
            toggle: (name, enabled) => {
                const selected = enabled ?? !element.classList.contains(name);
                element.className = element.className.split(/\s+/).filter(item => item && item !== name).concat(selected ? [name] : []).join(' ');
                return selected;
            }
        };
    }
    append(...children) {
        for (const child of children) {
            if (child.parent) child.parent.children = child.parent.children.filter(item => item !== child);
            child.parent = this; this.children.push(child);
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
    all() { return this.children.flatMap(child => [child, ...child.all()]); }
    querySelector(selector) {
        const target = selector.match(/\[data-page(\$?)="([^"]+)"\]/);
        return this.all().find(item => target && (target[1] ? item.dataset.page?.endsWith(target[2]) : item.dataset.page === target[2])) || null;
    }
    querySelectorAll(selector) {
        assert.equal(selector, ':scope > .nav-item');
        return this.children.filter(child => child.classList.contains('nav-item'));
    }
}
const storage = initial => {
    const data = new Map(Object.entries(initial || {}));
    return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, String(value)), removeItem: key => data.delete(key) };
};

async function openPage(role, apiFirst = false, options = {}) {
    const navigation = new Element();
    const entries = [['Tổng quan','../index.html'],['Bàn ăn','tables.html'],['Đặt món','order.html'],['Bếp','kitchen.html'],
        ['Thanh toán','payment.html'],['Món ăn','foods.html'],['Nhân viên','employees.html'],['Phân quyền','roles.html'],
        ['Kho hàng','inventory.html'],['Nhập hàng','purchases.html'],['Báo cáo','reports.html'],['Tài khoản','account.html']];
    for (const [label, page] of entries) {
        const item = new Element(); item.textContent = label; item.className = 'nav-item'; item.dataset.page = page; navigation.append(item);
    }
    const homes = { Waiter:'tables',Chef:'kitchen',Cashier:'payment',WarehouseManager:'inventory',Admin:'account' };
    const screen = options.screen || homes[role];
    for (const item of navigation.children) {
        if (item.dataset.page === `${screen}.html`) item.classList.add('is-active');
    }
    const user = { userId: 9, userName: 'fixture', roles: [role] };
    const events = new EventTarget();
    const media = { matches: Boolean(options.mobile), addEventListener(_name, listener) { this.change = listener; } };
    const window = {
        localStorage: storage({ 'restaurant.token':'fixture-token', 'restaurant.currentUser':JSON.stringify(user) }),
        sessionStorage: storage(), location: Object.assign(new URL(`http://localhost:5500/pages/${screen}.html${options.query || ''}`), {replace() { throw new Error('Unexpected redirect'); }}),
        atob, matchMedia: () => media,
        addEventListener: events.addEventListener.bind(events), dispatchEvent: events.dispatchEvent.bind(events)
    };
    const document = { documentElement:new Element(), body:new Element(), currentScript:{src:''},
        querySelector: selector => selector === '.main-nav' ? navigation : null,
        createElement: () => new Element(), addEventListener() {} };
    let finishProfile;
    const context = vm.createContext({window, document, location:window.location, URL, Headers, Response, CustomEvent,
        fetch: () => new Promise(resolve => { finishProfile = resolve; })});
    const run = file => {
        document.currentScript.src = 'http://localhost:5500/js/common/' + file;
        vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common', file), 'utf8'), context, {filename:file});
    };
    run('auth.js');
    for (const file of apiFirst ? ['api.js','navigation.js'] : ['navigation.js','api.js']) run(file);
    assert.equal(navigation.all().filter(item => item.classList.contains('nav-item') && !item.hidden).length, 0);
    finishProfile(new Response(JSON.stringify(user), {headers:{'content-type':'application/json'}}));
    await window.restaurantAuth.ready;
    if (options.inspect) return { navigation, window, media };
    return navigation.all().filter(item => item.classList.contains('nav-item') && !item.hidden).map(item => item.textContent);
}

for (const [role, expected] of [
    ['Waiter',['Bàn ăn','Đặt món','Tài khoản']],
    ['Chef',['Bếp','Tài khoản']],
    ['Cashier',['Thanh toán','Tài khoản']],
    ['WarehouseManager',['Món ăn','Kho hàng','Nguyên liệu / Công thức','Nhập hàng','Tài khoản','Danh mục / Topping']]
]) {
    test(`${role}: navigation nạp trước API vẫn hiện đủ mục đúng quyền sau /users/me`, async () => {
        assert.deepEqual(await openPage(role), expected);
    });
}
test('Admin nạp menu trước API vẫn có toàn bộ 17 mục sau bootstrap', async () => {
    assert.equal((await openPage('Admin')).length, 17);
});
test('thứ tự chuẩn auth → api → navigation vẫn giữ đủ màn Phục vụ', async () => {
    assert.deepEqual(await openPage('Waiter',true), ['Bàn ăn','Đặt món','Tài khoản']);
});

test('Thanh dưới hiện màn đang mở, đổi đúng khi xoay màn và vẫn giữ danh sách đúng quyền', async () => {
    for (const [role, screen, expected, desktop] of [
        ['Waiter', 'tables', 'Bàn ăn', 'Công việc'],
        ['Waiter', 'order', 'Đặt món', 'Công việc'],
        ['Waiter', 'order-tools', 'Đặt món', 'Công việc'],
        ['WarehouseManager', 'inventory', 'Kho hàng', 'Quản lý kho']
    ]) {
        const p = await openPage(role, false, { screen, mobile: true, inspect: true });
        const group = p.navigation.children.find(item => item.all().some(child => child.id === (role === 'WarehouseManager' ? 'nav-management-toggle' : 'nav-overview-toggle')));
        const toggle = group.children[0];
        assert.equal(toggle.children[0].textContent, expected);
        assert.equal(toggle.attributes['aria-expanded'], 'false');
        p.media.matches = false; p.media.change();
        assert.equal(toggle.children[0].textContent, desktop);
        p.media.matches = true; p.media.change();
        assert.equal(toggle.children[0].textContent, expected);
        const visible = p.navigation.all().filter(item => item.classList.contains('nav-item') && !item.hidden).map(item => item.textContent);
        if (role === 'Waiter') assert.deepEqual(visible, ['Bàn ăn', 'Đặt món', 'Tài khoản']);
    }
});

test('Đổi module quản trị ngay trong trang cập nhật nhãn thanh dưới', async () => {
    const p = await openPage('Admin', true, { screen: 'administration', query: '?tab=categories', mobile: true, inspect: true });
    const group = p.navigation.children.find(item => item.all().some(child => child.id === 'nav-management-toggle'));
    assert.equal(group.children[0].children[0].textContent, 'Danh mục / Topping');
    p.window.location.search = '?tab=promotions';
    p.window.dispatchEvent(new CustomEvent('restaurant:admin-module-changed'));
    assert.equal(group.children[0].children[0].textContent, 'Khuyến mãi');
});
