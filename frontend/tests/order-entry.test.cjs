const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Chạy script trang và OrderSession thật; DOM/API giả, không ghi database.
class Element {
    constructor() {
        this.handlers = new Map(); this.hidden = false; this.textContent = ''; this.innerHTML = '';
        this.value = ''; this.open = false;
        const classes = new Set();
        this.classList = {
            add: key => classes.add(key), remove: key => classes.delete(key),
            contains: key => classes.has(key),
            toggle(key, enabled) { if (enabled) classes.add(key); else classes.delete(key); }
        };
    }
    addEventListener(name, handler) { this.handlers.set(name, handler); }
    emit(name, event = {}) { return this.handlers.get(name)?.({ preventDefault() {}, ...event }); }
    querySelectorAll() { return []; }
    reset() {}
    focus() {}
    showModal() { this.open = true; }
    close() { this.open = false; }
}
const settle = () => new Promise(resolve => setImmediate(resolve));
function storage(initial = {}) {
    const data = new Map(Object.entries(initial));
    return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) };
}
async function page({ search = '', savedTable, tableStatus = 'Available', active = true, pending, fail = false, menu } = {}) {
    const nodes = new Map();
    const node = selector => {
        if (!nodes.has(selector)) nodes.set(selector, new Element());
        return nodes.get(selector);
    };
    const requests = [], writes = [];
    let failure = fail, hold;
    const auth = { getUser: () => ({ userId: 8, roles: ['Waiter'] }), hasRole: (...roles) => roles.includes('Waiter'), ready: Promise.resolve() };
    const api = { baseUrl: 'fixture', async get(route) {
        requests.push(route);
        if (route === '/restaurant-tables') {
            if (hold) await hold;
            if (failure) throw new Error('Không kết nối được máy chủ.');
            return [{ tableId: 3, tableNumber: '3', isActive: active, status: tableStatus }];
        }
        if (route.startsWith('/orders?')) return { items: [], total: 0 };
        if (route === '/menu') return menu ?? [{ categoryId: 1, categoryName: 'Cơm', foods: [{ foodId: 1, foodName: 'Cơm trắng', sizes: [{ sizeId: 1, sizeName: 'Vừa', sizePrice: 25000 }], toppings: [] }] }];
        throw new Error('Unexpected GET ' + route);
    } };
    for (const method of ['post', 'patch', 'delete']) api[method] = async (...args) => { writes.push({ method, args }); throw new Error('Write must not run'); };
    const window = {
        restaurantApi: api, restaurantAuth: auth,
        location: { search, href: 'order.html' },
        localStorage: storage(savedTable ? { 'restaurant.selectedTable': JSON.stringify(savedTable) } : {}),
        sessionStorage: storage(pending ? { 'restaurant.posPending': JSON.stringify(pending) } : {}),
        addEventListener() {}, setInterval() {}, clearInterval() {}, crypto: { randomUUID: () => 'test-key' }
    };
    const document = { querySelector: node, querySelectorAll: () => [], hidden: false, activeElement: null };
    const context = vm.createContext({ window, document, URLSearchParams });
    for (const file of ['../js/common/order-session.js', '../js/pages/order.js']) {
        vm.runInContext(fs.readFileSync(path.join(__dirname, file), 'utf8'), context);
    }
    await settle();
    return { node, requests, writes, window,
        fail(value) { failure = value; },
        hold() { let release; hold = new Promise(resolve => { release = resolve; }); return () => { release(); hold = null; }; }
    };
}
function expectEntry(p, visible) {
    assert.equal(p.node('#orderEntryCard').hidden, !visible);
    for (const selector of ['#orderContextBar', '#orderLayout', '#cartDock']) assert.equal(p.node(selector).hidden, visible, selector);
}

test('Chưa chọn bàn: chỉ hiện lời mời chọn bàn, không tải thực đơn/đơn và không báo lỗi', async () => {
    for (const search of ['', '?tableId=abc', '?tableId=0', '?tableId=2147483648']) {
        const p = await page({ search });
        expectEntry(p, true);
        assert.equal(p.node('#orderEntryTitle').textContent, 'Chọn bàn để bắt đầu');
        assert.equal(p.node('.pos-message').hidden, true);
        assert.equal(p.node('#orderEntryRetry').hidden, true);
        assert.deepEqual(p.requests, []);
        await p.node('#sendKitchenButton').emit('click');
        assert.equal(p.writes.length, 0);
    }
});

test('Chọn bàn rõ trên URL: tải đúng bàn, giỏ trống vẫn khóa gửi dù bộ nhớ có bàn khác', async () => {
    for (const options of [{ search: '?tableId=3' }, { search: '?tableId=3', savedTable: { id: 9 } }]) {
        const p = await page(options);
        expectEntry(p, false);
        assert.match(p.node('#foodGrid').innerHTML, /Cơm trắng/);
        assert.equal(p.node('#sendKitchenButton').disabled, true);
        assert.equal(p.node('#activeOrderPanel').hidden, true);
        assert.equal(p.requests.filter(route => route === '/menu').length, 1);
        assert.equal(p.writes.length, 0);
    }
});

test('Vào lại mục Đặt món không tự mở bàn cũ; Chọn bàn khác trở về sơ đồ, không ghi/xóa đơn', async () => {
    const p = await page({ savedTable: { id: 3 } });
    expectEntry(p, true);
    assert.deepEqual(p.requests, []);
    const selected = await page({ search: '?tableId=3' });
    await selected.node('#chooseTableButton').emit('click');
    assert.equal(selected.window.location.href, 'tables.html');
    assert.equal(selected.writes.length, 0);
});

test('Bàn không tồn tại, ngừng hoạt động hoặc bảo trì: giữ ẩn nơi đặt món, có lối chọn lại', async () => {
    for (const options of [{ search: '?tableId=9' }, { search: '?tableId=3', active: false }, { search: '?tableId=3', tableStatus: 'Maintenance' }]) {
        const p = await page(options);
        expectEntry(p, true);
        assert.equal(p.node('#orderEntryChooseTable').textContent, 'Chọn bàn khác');
        assert.equal(p.requests.includes('/menu'), false);
        assert.equal(p.writes.length, 0);
    }
});

test('Tải bàn lỗi: hiện nguyên nhân và thử lại; thử lại thành công mở thực đơn', async () => {
    const p = await page({ search: '?tableId=3', fail: true });
    expectEntry(p, true);
    assert.equal(p.node('#orderEntryRetry').hidden, false);
    assert.match(p.node('#orderEntryDescription').textContent, /Không kết nối/);
    p.fail(false);
    await p.node('#orderEntryRetry').emit('click');
    expectEntry(p, false);
    assert.match(p.node('#foodGrid').innerHTML, /Cơm trắng/);
    assert.equal(p.writes.length, 0);
});

test('Làm mới không làm nhấp nháy thực đơn; mất kết nối sau đó thì ẩn bản cũ và đóng giỏ mobile', async () => {
    const p = await page({ search: '?tableId=3' });
    await p.node('#cartDock').emit('click');
    assert.equal(p.node('.cart-panel').classList.contains('is-open'), true);
    const release = p.hold();
    const loading = p.node('#refreshOrderButton').emit('click');
    expectEntry(p, false);
    assert.equal(p.node('#sendKitchenButton').disabled, true);
    p.fail(true); release(); await loading;
    expectEntry(p, true);
    assert.equal(p.node('.cart-panel').classList.contains('is-open'), false);
    assert.equal(p.node('#orderEntryRetry').hidden, false);
});

test('Chưa chọn bàn vẫn giữ đường quay lại yêu cầu chưa xác nhận của bàn khác, không tự gửi lại', async () => {
    const pending = { userId: 8, tableId: 9, kind: 'create', body: { items: [] }, key: 'original-request' };
    const p = await page({ pending });
    expectEntry(p, true);
    assert.equal(p.node('.pos-message').hidden, false);
    assert.equal(p.node('#pendingTableLink').hidden, false);
    assert.equal(p.node('#pendingTableLink').href, 'order.html?tableId=9');
    assert.deepEqual(JSON.parse(p.window.sessionStorage.getItem('restaurant.posPending')), pending);
    assert.deepEqual(p.requests, []);
    assert.equal(p.writes.length, 0);
});

const searchMenu = [
    { categoryId: 1, categoryName: 'Món mặn', foods: [[11,'Bò lúc lắc'],[12,'Bò xào rau củ'],[14,'Tôm rang me']] },
    { categoryId: 2, categoryName: 'Cơm', foods: [[13,'Cơm chiên hải sản']] },
    { categoryId: 3, categoryName: 'Lẩu', foods: [[15,'Lẩu Thái hải sản']] },
    { categoryId: 4, categoryName: 'Rau', foods: [[16,'Đậu hũ chiên sả']] },
    { categoryId: 5, categoryName: 'Đồ uống', foods: [[17,'Trà tắc']] }
].map(category => ({ ...category, foods: category.foods.map(([foodId,foodName]) => ({ foodId,foodName,sizes:[{sizeId:foodId,sizeName:'1 phần',sizePrice:65000}],toppings:[] })) }));
function searchFoods(p, value) {
    p.node('#foodSearchInput').emit('input', { target: { value } });
    return [...p.node('#foodGrid').innerHTML.matchAll(/data-food-id="(\d+)"/g)].map(match => Number(match[1]));
}

test('Tìm món có/không dấu, chữ hoa và Unicode tổ hợp trả đúng món ngay khi nhập', async () => {
    const p = await page({ search:'?tableId=3', menu:searchMenu });
    for (const value of ['bò', 'bo', '  BÒ  ', 'Bò'.normalize('NFD')]) assert.deepEqual(searchFoods(p,value), [11,12]);
    for (const value of ['ĐẬU', 'dau', 'Đậu'.normalize('NFD')]) assert.deepEqual(searchFoods(p,value), [16]);
    assert.deepEqual(searchFoods(p,'  tra    tac  '), [17]);
});

test('Viết tắt theo chữ đầu tìm đúng món và giữ nguyên tên hiển thị', async () => {
    const p = await page({ search:'?tableId=3', menu:searchMenu });
    for (const [value,expected] of [['bll',[11]],['CCHS',[13]],['lths',[15]],['dhcs',[16]],['cc',[13]]]) assert.deepEqual(searchFoods(p,value),expected);
    searchFoods(p,'bll');
    assert.match(p.node('#foodGrid').innerHTML, /Bò lúc lắc/);
    assert.equal(p.node('#foodCount').textContent,'1 món');
});

test('Từ khóa rời không cần đúng thứ tự, không gửi thêm API hoặc lệnh ghi', async () => {
    const p = await page({ search:'?tableId=3', menu:searchMenu });
    const requests = [...p.requests];
    for (const value of ['tom me','me tom']) assert.deepEqual(searchFoods(p,value),[14]);
    assert.deepEqual(searchFoods(p,'com hai'),[13]);
    assert.deepEqual(searchFoods(p,'bo me'),[]);
    assert.deepEqual(p.requests,requests);
    assert.equal(p.writes.length,0);
});

test('Tìm kiếm giữ phạm vi danh mục; xóa từ khóa trả lại danh sách và trường hợp rỗng có hướng dẫn', async () => {
    const p = await page({ search:'?tableId=3', menu:searchMenu });
    p.node('#categoryList').emit('click',{target:{closest:()=>({dataset:{categoryId:'1'}})}});
    assert.deepEqual(searchFoods(p,'cchs'),[]);
    assert.match(p.node('#foodGrid').innerHTML,/Tất cả danh mục/);
    assert.deepEqual(searchFoods(p,''),[11,12,14]);
    p.node('#categoryList').emit('click',{target:{closest:()=>({dataset:{categoryId:'all'}})}});
    assert.deepEqual(searchFoods(p,'   '),[11,12,14,13,15,16,17]);
    assert.deepEqual(searchFoods(p,'khongco'),[]);
    assert.equal(p.node('#foodCount').textContent,'0 món');
});
