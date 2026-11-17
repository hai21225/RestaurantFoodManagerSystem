const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Chạy script trang và OrderSession thật; DOM/API giả, không ghi database.
class Element {
    constructor() {
        this.handlers = new Map(); this.htmlAssignments = 0; this.hidden = false; this.textContent = ''; this.innerHTML = '';
        this.value = ''; this.open = false;
        const classes = new Set();
        this.classList = {
            add: key => classes.add(key), remove: key => classes.delete(key),
            contains: key => classes.has(key),
            toggle(key, enabled) { if (enabled) classes.add(key); else classes.delete(key); }
        };
    }
    set innerHTML(value) {
        this.html = value; this.htmlAssignments++;
        for (const [property, attribute, key] of [['preparationNodes', 'preparation-item', 'preparationItem'], ['orderedNodes', 'ordered-item', 'orderedItem'], ['waitingNodes', 'waiting-item', 'waitingItem']]) {
            this[property] = [...value.matchAll(new RegExp(`data-${attribute}="([^"]+)"`, 'g'))].map(([, id]) => {
                const node = new Element(); node.dataset = { [key]: id }; return node;
            });
        }
    }
    get innerHTML() { return this.html; }
    addEventListener(name, handler) { this.handlers.set(name, handler); }
    emit(name, event = {}) { return this.handlers.get(name)?.({ preventDefault() {}, ...event }); }
    querySelectorAll() { return []; }
    reset() {}
    focus() {}
    showModal() { this.open = true; }
    close() { this.open = false; }
}
const settle = () => new Promise(resolve => setImmediate(resolve));
const preparingOrder = () => ({ orderId: 42, tableId: 3, status: 'Preparing', totalAmount: 120000,
    items: [{ id: 11, foodName: 'Cơm gà', sizeId: 1, sizeName: 'Vừa', status: 'Preparing', quantity: 2, unitPrice: 60000, toppings: [] }] });
const receiveHistory = { orderItemId: 11, oldStatus: 'Pending', newStatus: 'Preparing', createdAt: '2026-11-12T05:00:00' };
function storage(initial = {}) {
    const data = new Map(Object.entries(initial));
    return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value), removeItem: key => data.delete(key) };
}
async function page({ search = '', savedTable, tableStatus = 'Available', active = true, pending, fail = false, menu, order = null, history = [], historyFail = false, holdHistory = false } = {}) {
    const nodes = new Map();
    const node = selector => {
        if (!nodes.has(selector)) nodes.set(selector, new Element());
        return nodes.get(selector);
    };
    const requests = [], writes = [];
    let failure = fail, hold, releaseHistory, time = Date.parse('2026-11-12T05:12:03Z');
    const intervals = new Map();
    let historyGate = holdHistory ? new Promise(resolve => { releaseHistory = resolve; }) : null;
    const auth = { getUser: () => ({ userId: 8, roles: ['Waiter'] }), hasRole: (...roles) => roles.includes('Waiter'), ready: Promise.resolve() };
    const api = { baseUrl: 'fixture', async get(route) {
        requests.push(route);
        if (route === '/restaurant-tables') {
            if (hold) await hold;
            if (failure) throw new Error('Không kết nối được máy chủ.');
            return [{ tableId: 3, tableNumber: '3', isActive: active, status: tableStatus }];
        }
        if (route.startsWith('/orders?')) return { items: order ? [{ orderId: order.orderId }] : [], total: order ? 1 : 0 };
        if (order && route === `/orders/${order.orderId}`) return JSON.parse(JSON.stringify(order));
        if (order && route === `/orders/${order.orderId}/item-history`) {
            const result = history;
            if (historyGate) await historyGate;
            if (historyFail) throw new Error('Không đọc được lịch sử món.');
            return result;
        }
        if (route === '/menu') return menu ?? [{ categoryId: 1, categoryName: 'Cơm', foods: [{ foodId: 1, foodName: 'Cơm trắng', sizes: [{ sizeId: 1, sizeName: 'Vừa', sizePrice: 25000 }], toppings: [] }] }];
        throw new Error('Unexpected GET ' + route);
    } };
    for (const method of ['post', 'patch', 'delete']) api[method] = async (...args) => { writes.push({ method, args }); throw new Error('Write must not run'); };
    const window = {
        restaurantApi: api, restaurantAuth: auth,
        location: { search, href: 'http://localhost:5500/pages/order.html' + search },
        localStorage: storage(savedTable ? { 'restaurant.selectedTable': JSON.stringify(savedTable) } : {}),
        sessionStorage: storage(pending ? { 'restaurant.posPending': JSON.stringify(pending) } : {}),
        addEventListener() {}, setInterval(fn, delay) { intervals.set(delay, fn); }, clearInterval() {}, crypto: { randomUUID: () => 'test-key' }
    };
    const timingSelectors = { '[data-preparation-item]': 'preparationNodes', '[data-ordered-item]': 'orderedNodes', '[data-waiting-item]': 'waitingNodes' };
    const document = { querySelector: node, querySelectorAll: selector => timingSelectors[selector] ? node('#sentOrderItems')[timingSelectors[selector]] : [], hidden: false, activeElement: null };
    class ClockDate extends Date { static now() { return time; } }
    const context = vm.createContext({ window, document, URL, URLSearchParams, Date: ClockDate });
    for (const file of ['../js/common/order-session.js', '../js/common/preparation-timing.js', '../js/pages/order.js']) {
        vm.runInContext(fs.readFileSync(path.join(__dirname, file), 'utf8'), context);
    }
    await settle();
    return { node, requests, writes, window,
        timers: () => node('#sentOrderItems').preparationNodes,
        orderedTimes: () => node('#sentOrderItems').orderedNodes,
        waitingTimers: () => node('#sentOrderItems').waitingNodes,
        htmlAssignments: () => [...nodes.values()].reduce((total, element) => total + element.htmlAssignments, 0),
        async poll() { intervals.get(5000)(); await settle(); },
        tick(seconds = 1) { time += seconds * 1000; intervals.get(1000)(); },
        data(nextOrder, nextHistory = history) { order = nextOrder; history = nextHistory; },
        failHistory(value) { historyFail = value; },
        unblockNewHistoryReads() { historyGate = null; },
        releaseHistory() { const release = releaseHistory; historyGate = null; release?.(); },
        fail(value) { failure = value; },
        hold() { let release; hold = new Promise(resolve => { release = resolve; }); return () => { release(); hold = null; }; }
    };
}
function expectEntry(p, visible) {
    assert.equal(p.node('#orderEntryCard').hidden, !visible);
    for (const selector of ['#orderContextBar', '#orderLayout', '#cartDock']) assert.equal(p.node(selector).hidden, visible, selector);
}

test('POS đếm từ lịch sử nhận món, tick không gọi API/rerender và giữ khóa giảm/hủy Phục vụ', async () => {
    const p = await page({ search: '?tableId=3', order: preparingOrder(), history: [receiveHistory,
        { ...receiveHistory, oldStatus: 'Preparing', createdAt: '2026-11-12T05:11:00Z' }] });
    assert.equal(p.timers()[0].textContent, 'Đang làm · 12:03');
    assert.match(p.node('#sentOrderItems').innerHTML, /Gọi tăng/);
    assert.doesNotMatch(p.node('#sentOrderItems').innerHTML, /data-cancel-item=/);
    const html = p.node('#sentOrderItems').innerHTML, requests = p.requests.length, timer = p.timers()[0];
    p.tick();
    assert.equal(timer.textContent, 'Đang làm · 12:04');
    assert.equal(p.timers()[0], timer);
    assert.equal(p.node('#sentOrderItems').innerHTML, html);
    assert.equal(p.requests.length, requests);
    assert.equal(p.writes.length, 0);
});

test('POS lịch sử thời gian lỗi hoặc thiếu mốc không giả 00:00 và không khóa đặt thêm', async () => {
    for (const historyFail of [true, false]) {
        const p = await page({ search: '?tableId=3', order: preparingOrder(), historyFail });
        assert.equal(p.timers()[0].textContent, historyFail ? 'Chưa rõ thời gian' : 'Chưa có mốc bắt đầu');
        assert.equal(p.node('#foodGrid').innerHTML.includes('disabled'), false);
        assert.equal(p.node('#refreshOrderButton').disabled, false);
        assert.equal(p.writes.length, 0);
        if (historyFail) {
            assert.match(p.node('#preparationTimeNotice').textContent, /Không đọc được lịch sử món/);
            assert.equal(p.node('#preparationTimeNotice').hidden, false);
            p.failHistory(false); p.data(preparingOrder(), [receiveHistory]); await p.poll();
            assert.equal(p.node('#preparationTimeNotice').hidden, true);
            assert.equal(p.timers()[0].textContent, 'Đang làm · 12:03');
        }
    }
});

test('POS mở lại món lấy mốc mới; refresh lỗi không tiếp tục hiện đồng hồ như dữ liệu đang cập nhật', async () => {
    const p = await page({ search: '?tableId=3', order: preparingOrder(), history: [receiveHistory] });
    p.data(preparingOrder(), [receiveHistory, { ...receiveHistory, oldStatus: 'Completed', createdAt: '2026-11-12T05:10:00Z' }]);
    await p.poll();
    assert.equal(p.timers()[0].textContent, 'Đang làm · 02:03');
    p.fail(true); await p.poll();
    assert.equal(p.timers()[0].textContent, 'Thời gian chưa cập nhật');
    assert.equal(p.writes.length, 0);
});

test('POS không chờ lịch sử để mở món và bỏ kết quả lịch sử đến trễ sau khi món đã xong', async () => {
    const p = await page({ search: '?tableId=3', order: preparingOrder(), history: [receiveHistory], holdHistory: true });
    assert.match(p.node('#foodGrid').innerHTML, /Cơm trắng/);
    assert.equal(p.node('#foodGrid').innerHTML.includes('disabled'), false);
    assert.equal(p.timers()[0].textContent, 'Đang lấy thời gian…');
    const ready = preparingOrder(); ready.status = 'Ready'; ready.items[0].status = 'Completed';
    p.data(ready); await p.poll(); p.releaseHistory(); await settle();
    assert.equal(p.timers().length, 0);
    assert.doesNotMatch(p.node('#sentOrderItems').innerHTML, /data-preparation-item/);
    assert.equal(p.writes.length, 0);
});

test('POS hiện giờ gọi từng dòng theo giờ Việt Nam, chờ bếp nhận đếm riêng và tick không gọi API/render lại', async () => {
    const order = preparingOrder(); order.status = 'SentToKitchen';
    order.items[0].status = 'Pending'; order.items[0].orderItemGroupId = 'same-group';
    order.items.push({ ...order.items[0], id: 12, foodName: 'Món gọi sau' });
    const history = [{ orderItemId: 11, oldStatus: '', newStatus: 'Pending', createdAt: '2026-11-12T05:00:00' },
        { orderItemId: 12, oldStatus: '', newStatus: 'Pending', createdAt: '2026-11-12T12:10:00+07:00' }];
    const p = await page({ search: '?tableId=3', order, history });
    assert.deepEqual(p.orderedTimes().map(node => node.textContent), ['Gọi lúc 12:00:00', 'Gọi lúc 12:10:00']);
    assert.deepEqual(p.waitingTimers().map(node => node.textContent), ['Chờ bếp nhận · 12:03', 'Chờ bếp nhận · 02:03']);
    assert.equal(p.timers().length, 0);
    assert.equal(p.requests.filter(route => route === '/orders/42/item-history').length, 1);
    const requests = p.requests.length, assignments = p.htmlAssignments(), html = p.node('#sentOrderItems').innerHTML;
    const orderedNodes = p.orderedTimes(), waitingNodes = p.waitingTimers();
    p.tick();
    assert.deepEqual(waitingNodes.map(node => node.textContent), ['Chờ bếp nhận · 12:04', 'Chờ bếp nhận · 02:04']);
    assert.equal(p.orderedTimes()[0], orderedNodes[0]);
    assert.equal(p.waitingTimers()[0], waitingNodes[0]);
    assert.equal(p.node('#sentOrderItems').innerHTML, html);
    assert.equal(p.htmlAssignments(), assignments);
    assert.equal(p.requests.length, requests);
    assert.equal(p.writes.length, 0);
});

test('POS nhận món bỏ đồng hồ chờ, dùng mốc bắt đầu riêng; Ready vẫn giữ giờ gọi nhưng không có đồng hồ đang chạy', async () => {
    const pending = preparingOrder(); pending.status = 'SentToKitchen'; pending.items[0].status = 'Pending';
    const creation = { orderItemId: 11, oldStatus: '', newStatus: 'Pending', createdAt: '2026-11-12T05:00:00Z' };
    const p = await page({ search: '?tableId=3', order: pending, history: [creation] });
    assert.equal(p.waitingTimers()[0].textContent, 'Chờ bếp nhận · 12:03');
    const received = { ...receiveHistory, createdAt: '2026-11-12T05:11:00Z' };
    p.data(preparingOrder(), [creation, received]); await p.poll();
    assert.equal(p.waitingTimers().length, 0);
    assert.equal(p.timers()[0].textContent, 'Đang làm · 01:03');
    assert.equal(p.orderedTimes()[0].textContent, 'Gọi lúc 12:00:00');
    const ready = preparingOrder(); ready.status = 'Ready'; ready.items[0].status = 'Ready';
    p.data(ready, [creation, received, { ...received, oldStatus: 'Preparing', newStatus: 'Ready', createdAt: '2026-11-12T05:12:00Z' }]); await p.poll();
    assert.equal(p.waitingTimers().length, 0);
    assert.equal(p.timers().length, 0);
    assert.equal(p.orderedTimes()[0].textContent, 'Gọi lúc 12:00:00');
    assert.equal(p.requests.filter(route => route === '/orders/42/item-history').length, 3);
    assert.equal(p.writes.length, 0);
});

test('POS thiếu/sai/clone/tương lai không giả giờ gọi; GET phụ lỗi có thông báo nhưng vẫn mở menu để gọi thêm', async () => {
    const order = preparingOrder(); order.status = 'SentToKitchen'; order.items[0].status = 'Pending';
    order.createdAt = '2026-11-12T04:00:00Z'; order.updatedAt = '2026-11-12T05:11:00Z';
    order.items[0].createdAt = '2026-11-12T05:10:00Z';
    for (const [orderedAt, history] of [[undefined, []], ['invalid', [{ orderItemId: 11, oldStatus: '', newStatus: 'Pending', createdAt: 'invalid' }]],
        [undefined, [{ orderItemId: 11, oldStatus: 'Pending', newStatus: 'Pending', createdAt: '2026-11-12T05:10:00Z', reason: 'Created with quantity 2 (new pending line)' }]], ['2026-11-12T06:00:00Z', []]]) {
        const p = await page({ search: '?tableId=3', order: { ...order, items: [{ ...order.items[0], orderedAt }] }, history });
        assert.equal(p.orderedTimes()[0].textContent, 'Chưa có giờ gọi riêng');
        assert.equal(p.waitingTimers()[0].textContent, 'Chưa có giờ gọi riêng');
        assert.doesNotMatch(p.node('#sentOrderItems').innerHTML, /NaN/);
        assert.equal(p.writes.length, 0);
    }
    const p = await page({ search: '?tableId=3', order, historyFail: true });
    assert.equal(p.orderedTimes()[0].textContent, 'Chưa rõ giờ gọi riêng');
    assert.equal(p.waitingTimers()[0].textContent, 'Chưa rõ thời gian chờ');
    assert.equal(p.node('#preparationTimeNotice').hidden, false);
    assert.match(p.node('#preparationTimeNotice').textContent, /Không đọc được lịch sử món/);
    assert.match(p.node('#foodGrid').innerHTML, /Cơm trắng/);
    assert.equal(p.node('#foodGrid').innerHTML.includes('disabled'), false);
    assert.equal(p.node('#refreshOrderButton').disabled, false);
    assert.equal(p.writes.length, 0);
});

test('POS không chờ giờ gọi riêng để dùng menu và bỏ audit trả muộn của lượt tải trước', async () => {
    const first = preparingOrder(); first.status = 'SentToKitchen'; first.items[0].status = 'Pending';
    const p = await page({ search: '?tableId=3', order: first, history: [{ orderItemId: 11, oldStatus: '', newStatus: 'Pending', createdAt: '2026-11-12T05:00:00Z' }], holdHistory: true });
    assert.equal(p.orderedTimes()[0].textContent, 'Đang lấy giờ gọi…');
    assert.equal(p.waitingTimers()[0].textContent, 'Đang lấy thời gian chờ…');
    assert.equal(p.node('#foodGrid').innerHTML.includes('disabled'), false);
    const next = { ...first, items: [{ ...first.items[0], id: 21 }] };
    p.data(next, [{ orderItemId: 21, oldStatus: '', newStatus: 'Pending', createdAt: '2026-11-12T05:10:00Z' }]);
    p.unblockNewHistoryReads(); await p.poll();
    assert.equal(p.orderedTimes()[0].textContent, 'Gọi lúc 12:10:00');
    p.releaseHistory(); await settle();
    assert.equal(p.orderedTimes()[0].textContent, 'Gọi lúc 12:10:00');
    assert.equal(p.waitingTimers()[0].textContent, 'Chờ bếp nhận · 02:03');
    assert.equal(p.requests.filter(route => route === '/orders/42/item-history').length, 2);
    assert.equal(p.writes.length, 0);
});

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
    assert.equal(selected.window.location.href, 'tables.html?ui=20261112-print1');
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
    assert.equal(p.node('#pendingTableLink').href, 'order.html?tableId=9&ui=20261112-print1');
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


test('Thẻ món dùng ảnh HTTP hợp lệ, bỏ URL script và giữ hook chọn món/giá từ menu', async () => {
    const menu = [{ categoryId:1, categoryName:'Món', foods:[
        { foodId:31, foodName:'Món <&>', foodImageUrl:'/images/food.png?a=1&b=2', sizes:[{sizeId:31,sizeName:'Lớn',sizePrice:65000}], toppings:[] },
        { foodId:32, foodName:'Món khác', foodImageUrl:'javascript:alert(1)', sizes:[{sizeId:32,sizeName:'Vừa',sizePrice:25000}], toppings:[] }
    ] }];
    const p = await page({search:'?tableId=3',menu});
    const html = p.node('#foodGrid').innerHTML;
    assert.match(html, /src="http:\/\/localhost:5500\/images\/food.png\?a=1&amp;b=2"/);
    assert.equal((html.match(/data-food-image/g)||[]).length,1);
    assert.doesNotMatch(html,/javascript:|<h4>Món <&>/);
    assert.match(html,/Món &lt;&amp;&gt;/);
    assert.match(html,/data-food-id="31"/);
    assert.match(html,/65[.\u00a0,]000đ/);
    assert.equal(p.writes.length,0);
});
