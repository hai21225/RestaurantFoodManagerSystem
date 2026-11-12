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
    if (options.service) document.body.className = 'service-workspace';
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
    assert.equal(navigation.classList.contains('nav-service-direct'), false);
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

test('Pilot Phục vụ có menu trực tiếp đúng quyền; Admin vẫn giữ đủ nhóm, trang cũ không đổi', async () => {
    const waiter = await openPage('Waiter', true, { screen:'order', service:true, mobile:true, inspect:true });
    assert.equal(waiter.navigation.classList.contains('nav-service-direct'), true);
    assert.deepEqual(waiter.navigation.all().filter(x => x.classList.contains('nav-item') && !x.hidden).map(x => x.textContent), ['Bàn ăn','Đặt món','Tài khoản']);
    const admin = await openPage('Admin', true, { screen:'tables', service:true, mobile:true, inspect:true });
    assert.equal(admin.navigation.classList.contains('nav-service-direct'), false);
    assert.equal(admin.navigation.all().filter(x => x.classList.contains('nav-item') && !x.hidden).length, 17);
    const old = await openPage('Waiter', true, { screen:'tables', mobile:true, inspect:true });
    assert.equal(old.navigation.classList.contains('nav-service-direct'), false);
});

test('Pilot Tiện ích đơn của Thu ngân không mở thêm menu POS/Bếp', async () => {
    const p = await openPage('Cashier', true, { screen:'order-tools', service:true, mobile:true, inspect:true });
    assert.equal(p.navigation.classList.contains('nav-service-direct'), true);
    assert.deepEqual(p.navigation.all().filter(x => x.classList.contains('nav-item') && !x.hidden).map(x => x.textContent), ['Thanh toán','Tài khoản']);
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


test('Liên kết pilot giữ phiên bản HTML khi chuyển màn và không thêm lựa chọn bàn ngầm', async () => {
    const p = await openPage('Waiter',true,{screen:'order',service:true,inspect:true});
    const links=p.navigation.all().filter(x=>x.classList.contains('nav-item')&&!x.hidden);
    for(const label of ['Bàn ăn','Đặt món']) {
        const target=new URL(links.find(x=>x.textContent===label).dataset.page);
        assert.equal(target.searchParams.get('ui'),'20261112-print1');
        assert.equal(target.searchParams.has('tableId'),false);
        assert.equal(p.window.restaurantAuth.canAccessPage(target.href),true);
    }
    const account = new URL(links.find(x=>x.textContent==='Tài khoản').dataset.page);
    assert.equal(account.pathname, '/pages/account.html');
    assert.equal(account.searchParams.get('ui'), '20261112-print1');
});

test('Sahara Bếp/Thu ngân dùng tab trực tiếp và giữ đúng quyền sau chuẩn hóa URL', async () => {
    for (const [role, expected] of [['Chef', ['Bếp', 'Tài khoản']], ['Cashier', ['Thanh toán', 'Tài khoản']]]) {
        const p = await openPage(role, true, { service:true, mobile:true, inspect:true });
        assert.equal(p.navigation.classList.contains('nav-service-direct'), true);
        const visible = p.navigation.all().filter(x=>x.classList.contains('nav-item')&&!x.hidden);
        assert.deepEqual(visible.map(x=>x.textContent), expected);
        for (const link of visible) {
            const target = new URL(link.dataset.page);
            assert.equal(target.searchParams.get('ui'), '20261112-print1');
            assert.equal(p.window.restaurantAuth.canAccessPage(target.href), true);
        }
    }
});

test('URL Sahara của kho/quản trị giữ module tab và không thêm lựa chọn đơn/bàn', async () => {
    const p = await openPage('Admin', true, { service:true, inspect:true });
    const links = p.navigation.all().filter(x=>x.classList.contains('nav-item')&&!x.hidden);
    for (const label of ['Tổng quan','Kho hàng','Nhập hàng','Nguyên liệu / Công thức','Nhân viên','Báo cáo','Khuyến mãi']) {
        const target = new URL(links.find(x=>x.textContent===label).dataset.page);
        assert.equal(target.searchParams.get('ui'), '20261112-print1');
        assert.equal(target.searchParams.has('tableId'), false);
        assert.equal(target.searchParams.has('orderId'), false);
        if (label === 'Khuyến mãi') assert.equal(target.searchParams.get('tab'), 'promotions');
    }
});
