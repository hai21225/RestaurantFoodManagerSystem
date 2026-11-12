const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

// Execute the page with the real KitchenSession; all requests stay in this fixture.
class Element {
    constructor() {
        this.handlers = new Map(); this.attributes = new Map(); this.textContent = ''; this.html = '';
        this.hidden = false; this.disabled = false; this.open = false; this.dataset = {}; this.htmlAssignments = 0;
        const classes = new Set();
        this.classList = {
            add: value => classes.add(value), remove: value => classes.delete(value),
            contains: value => classes.has(value),
            toggle(value, enabled) { if (enabled) classes.add(value); else classes.delete(value); }
        };
    }
    set innerHTML(value) {
        this.html = value; this.htmlAssignments++;
        this.actions = [...value.matchAll(/<button\b([^>]*data-kitchen-action="[^"]+"[^>]*)>/g)].map(match => {
            const button = new Element(); button.disabled = /\sdisabled(?:\s|$)/.test(match[1]);
            button.dataset = Object.fromEntries([...match[1].matchAll(/data-(kitchen-action|ticket-id|item-id)="([^"]+)"/g)]
                .map(([, key, value]) => [key.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase()), value]));
            button.closest = selector => selector === '[data-kitchen-action]' || selector === 'button' ? button : null;
            return button;
        });
        this.prints = [...value.matchAll(/<button\b([^>]*data-kitchen-print="([^"]+)"[^>]*)>/g)].map(match => {
            const button = new Element(); button.dataset = { kitchenPrint: match[2] };
            button.disabled = /\sdisabled(?:\s|$)/.test(match[1]);
            button.closest = selector => selector === '[data-kitchen-print]' ? button : null;
            return button;
        });
        this.clocks = [...value.matchAll(/<span\b([^>]*data-preparation-start="[^"]*"[^>]*)>([^<]*)<\/span>/g)].map(match => {
            const clock = new Element();
            clock.dataset = Object.fromEntries([...match[1].matchAll(/data-(preparation-start|preparation-scope)="([^"]*)"/g)]
                .map(([, key, value]) => [key.replace(/-([a-z])/g, (_, letter) => letter.toUpperCase()), value]));
            clock.textContent = match[2];
            return clock;
        });
    }
    get innerHTML() { return this.html; }
    setAttribute(key, value) { this.attributes.set(key, value); }
    addEventListener(name, handler) { this.handlers.set(name, handler); }
    emit(name, event = {}) { return this.handlers.get(name)?.({ preventDefault() {}, ...event }); }
    querySelectorAll() { return []; }
    scrollIntoView() { this.scrolled = true; }
    reset() {}
    focus() {}
    close() { this.open = false; }
    showModal() { this.open = true; }
}
const settle = () => new Promise(resolve => setImmediate(resolve));
const copy = value => JSON.parse(JSON.stringify(value));
function storage() {
    const values = new Map();
    return { getItem: key => values.get(key) ?? null, setItem: (key, value) => values.set(key, value), removeItem: key => values.delete(key) };
}
async function page({ fail = false, withTicket = false, loading = false, ticketStatus = 'Pending', itemStatus = ticketStatus,
    startedAt = null, preparationStartedAt, currentTime = Date.parse('2026-11-12T10:05:00Z'),
    customTickets = null, customItems = null, customOrders = null, allowReceive = false } = {}) {
    const nodes = new Map();
    const node = selector => {
        if (!nodes.has(selector)) nodes.set(selector, new Element());
        return nodes.get(selector);
    };
    const requests = [], writes = [];
    const tickets = copy(customTickets ?? (withTicket
        ? [{ kitchenOrderId: 10, orderId: 42, status: ticketStatus, startedAt, sentAt: '2026-11-12T09:00:00Z', tableNumber: 'TEST', priority: 0 }] : []));
    const itemsByTicket = copy(customItems ?? { 10: [{ id: 1, orderItemId: 11, status: itemStatus, preparationStartedAt, quantity: 2, foodName: 'Món đang chờ', sizeName: 'Vừa' }] });
    const orders = copy(customOrders ?? { 42: { status: 'SentToKitchen', items: [{ id: 11 }] } });
    let failure = fail, hold, release, poll, clock, now = currentTime;
    const intervalDelays = [];
    class FixtureDate extends Date {
        constructor(...args) { super(...(args.length ? args : [now])); }
        static now() { return now; }
    }
    function holdNext() { hold = new Promise(resolve => { release = resolve; }); }
    if (loading) holdNext();
    const api = { async get(route) {
        requests.push(route);
        if (hold) await hold;
        if (failure) throw new Error('Máy chủ gặp lỗi.');
        if (['/management/kitchen/orders?status=open', '/management/kitchen/orders'].includes(route)) return copy(tickets);
        const itemsMatch = route.match(/^\/management\/kitchen\/orders\/(\d+)\/items$/);
        if (itemsMatch && itemsByTicket[itemsMatch[1]]) return copy(itemsByTicket[itemsMatch[1]]);
        const orderMatch = route.match(/^\/orders\/(\d+)$/);
        if (orderMatch && orders[orderMatch[1]]) return copy(orders[orderMatch[1]]);
        throw new Error('Unexpected GET ' + route);
    } };
    for (const method of ['post', 'patch', 'delete']) api[method] = async (...args) => {
        writes.push({ method, args: copy(args) });
        const receive = args[0].match(/^\/management\/kitchen\/items\/(\d+)\/status$/);
        if (allowReceive && method === 'patch' && receive && args[1].status === 'Preparing') {
            const ticket = tickets.find(ticket => itemsByTicket[ticket.kitchenOrderId].some(item => item.id === Number(receive[1])));
            const item = itemsByTicket[ticket.kitchenOrderId].find(item => item.id === Number(receive[1]));
            item.status = 'Preparing'; ticket.status = 'Preparing'; ticket.startedAt ||= '2026-11-12T10:05:00Z';
            orders[ticket.orderId].status = 'Preparing';
            return null; // Chỉ mutate fixture trong bộ nhớ; không có server/database.
        }
        throw new Error('No writes are allowed in this view test');
    };
    const auth = { getUser: () => ({ userId: 8, roles: ['Chef'] }), hasRole: (...roles) => roles.includes('Chef'), ready: Promise.resolve() };
    const document = { body: new Element(), getElementById: id => node('#' + id),
        querySelector: node, hidden: false, activeElement: null,
        querySelectorAll(selector) {
            if (selector === '[data-kitchen-action]') return node('#kitchenBoard').actions || [];
            if (selector === '[data-kitchen-print]') return [node('#kitchenBoard'), node('#kitchenReceiptContent')].flatMap(element => element.prints || []);
            if (selector === '[data-preparation-start]') return [node('#kitchenBoard'), node('#kitchenReceiptContent')].flatMap(element => element.clocks || []);
            return [];
        }
    };
    const window = { restaurantAuth: auth, restaurantApi: api, sessionStorage: storage(), addEventListener() {},
        setInterval(callback, delay) { intervalDelays.push(delay); if (delay === 5000) poll = callback; if (delay === 1000) clock = callback; return delay; }, clearInterval() {} };
    const context = vm.createContext({ window, document, Date: FixtureDate });
    for (const file of ['../js/common/kitchen-session.js', '../js/common/preparation-timing.js', '../js/common/kitchen-printing.js', '../js/common/kitchen-print-controller.js', '../js/pages/kitchen.js']) {
        vm.runInContext(fs.readFileSync(path.join(__dirname, file), 'utf8'), context);
    }
    await settle();
    return { node, requests, writes, intervalDelays,
        clocks: () => document.querySelectorAll('[data-preparation-start]'),
        htmlAssignments: () => [...nodes.values()].reduce((total, element) => total + element.htmlAssignments, 0),
        focusFilter() { document.activeElement = { closest: () => node('#kitchenSearch') }; },
        fail(value) { failure = value; }, holdNext,
        release() { const done = release; hold = null; done(); },
        async poll() { poll(); await settle(); },
        async tick(milliseconds = 1000) { now += milliseconds; clock(); await settle(); }
    };
}
function summaryValues(p) {
    return [...p.node('#kitchenSummary').innerHTML.matchAll(/<strong>(.*?)<\/strong>/g)].map(match => match[1]);
}

test('GET đầu tiên lỗi: số liệu chưa biết, có lối tải lại và không báo danh sách rỗng', async () => {
    const p = await page({ fail: true });
    assert.deepEqual(summaryValues(p), ['—', '—', '—', '—']);
    assert.match(p.node('#kitchenBoard').innerHTML, /Chưa tải được phiếu\. Bấm Làm mới/);
    assert.doesNotMatch(p.node('#kitchenBoard').innerHTML, /Chưa có phiếu nào/);
    assert.doesNotMatch(p.node('#kitchenStatusTabs').innerHTML, /<span>0<\/span>/);
    assert.match(p.node('#kitchenAnnouncement').textContent, /Máy chủ gặp lỗi/);
    assert.equal(p.node('#refreshKitchenButton').disabled, false);
    assert.equal(p.node('#kitchenActionConfirm').disabled, true);
    assert.equal(p.writes.length, 0);
});

test('Đang tải không giả 0; GET thành công rỗng mới hiện 0 và trạng thái rỗng hợp lệ', async () => {
    const p = await page({ loading: true });
    assert.deepEqual(summaryValues(p), ['—', '—', '—', '—']);
    assert.match(p.node('#kitchenBoard').innerHTML, /Đang tải phiếu bếp/);
    assert.equal(p.node('#kitchenBoard').attributes.get('aria-busy'), 'true');
    assert.equal(p.node('#refreshKitchenButton').textContent, 'Đang tải…');
    p.release(); await settle();
    assert.deepEqual(summaryValues(p), ['0', '0', '0', '0']);
    assert.match(p.node('#kitchenBoard').innerHTML, /Chưa có phiếu nào/);
    assert.doesNotMatch(p.node('#kitchenBoard').innerHTML, /Đang tải phiếu bếp|Chưa tải được phiếu/);
    assert.equal(p.node('#kitchenBoard').attributes.get('aria-busy'), 'false');
    assert.equal(p.writes.length, 0);
});

test('Poll thành công sau lỗi thay trạng thái chưa tải bằng rỗng thật và xóa cảnh báo lỗi cũ', async () => {
    const p = await page({ fail: true });
    p.fail(false); await p.poll();
    assert.deepEqual(summaryValues(p), ['0', '0', '0', '0']);
    assert.match(p.node('#kitchenBoard').innerHTML, /Chưa có phiếu nào/);
    assert.doesNotMatch(p.node('#kitchenBoard').innerHTML, /Chưa tải được phiếu/);
    assert.equal(p.node('#kitchenAnnouncement').classList.contains('is-error'), false);
    assert.equal(p.writes.length, 0);
});

test('Làm mới lỗi giữ phiếu cũ để xem, khóa xử lý và không ghi khi sự kiện nhận món bị gọi', async () => {
    const p = await page({ withTicket: true });
    assert.deepEqual(summaryValues(p), ['1', '1', '0', '0']);
    assert.ok(p.node('#kitchenBoard').actions.some(button => !button.disabled));
    p.holdNext();
    const refresh = p.node('#refreshKitchenButton').emit('click');
    assert.match(p.node('#kitchenBoard').innerHTML, /Món đang chờ/);
    assert.ok(p.node('#kitchenBoard').actions.every(button => button.disabled));
    p.fail(true); p.release(); await refresh;
    assert.match(p.node('#kitchenBoard').innerHTML, /Món đang chờ/);
    assert.deepEqual(summaryValues(p), ['—', '—', '—', '—']);
    assert.match(p.node('#kitchenAnnouncement').textContent, /dữ liệu từ lần tải trước, chỉ được xem/);
    assert.ok(p.node('#kitchenBoard').actions.every(button => button.disabled));
    const requestCount = p.requests.length;
    p.node('#kitchenBoard').emit('click', { target: p.node('#kitchenBoard').actions[0] });
    await settle();
    assert.equal(p.requests.length, requestCount);
    assert.equal(p.writes.length, 0);
});

test('Đồng hồ Chef ghi rõ cấp phiếu, tick mỗi giây chỉ đổi text và không gọi API/render lại bảng', async () => {
    const p = await page({ withTicket: true, ticketStatus: 'Preparing', startedAt: '2026-11-12T09:55:00' });
    assert.deepEqual(p.intervalDelays.slice().sort((a, b) => a - b), [1000, 5000]);
    const clocks = p.clocks().filter(node => node.dataset.preparationScope === 'Phiếu đã bắt đầu');
    assert.equal(clocks.length, 1);
    assert.equal(clocks[0].dataset.preparationScope, 'Phiếu đã bắt đầu');
    assert.equal(clocks[0].textContent, 'Phiếu đã bắt đầu · 10:00');
    assert.match(p.node('#kitchenBoard').innerHTML, /mỗi món có thể được nhận ở thời điểm khác nhau/);
    const requests = p.requests.length, htmlAssignments = p.htmlAssignments();
    const board = p.node('#kitchenBoard').innerHTML;
    await p.tick();
    assert.equal(p.clocks().find(node => node.dataset.preparationScope === 'Phiếu đã bắt đầu'), clocks[0], 'Keep the existing clock DOM node');
    assert.equal(clocks[0].textContent, 'Phiếu đã bắt đầu · 10:01');
    await p.tick(64000);
    assert.equal(clocks[0].textContent, 'Phiếu đã bắt đầu · 11:05');
    assert.equal(p.requests.length, requests);
    assert.equal(p.htmlAssignments(), htmlAssignments);
    assert.equal(p.node('#kitchenBoard').innerHTML, board);
    assert.equal(p.requests.some(route => route.includes('item-history')), false);
    assert.equal(p.writes.length, 0);
});

test('Pending/Ready không có đồng hồ đang làm; Preparing thiếu hoặc sai mốc không dùng giờ gửi phiếu thay thế', async () => {
    for (const status of ['Pending', 'Ready']) {
        const p = await page({ withTicket: true, ticketStatus: status, startedAt: '2026-11-12T09:55:00Z', preparationStartedAt: '2026-11-12T10:00:00Z' });
        assert.equal(p.clocks().filter(node => ['Phiếu đã bắt đầu', 'Đang làm'].includes(node.dataset.preparationScope)).length, 0, status);
        await p.tick();
        assert.equal(p.clocks().filter(node => ['Phiếu đã bắt đầu', 'Đang làm'].includes(node.dataset.preparationScope)).length, 0, status);
        assert.equal(p.writes.length, 0);
    }
    for (const startedAt of [null, 'invalid', '2026-11-12T11:00:00Z']) {
        const p = await page({ withTicket: true, ticketStatus: 'Preparing', startedAt });
        const clock = p.clocks().find(node => node.dataset.preparationScope === 'Phiếu đã bắt đầu');
        assert.ok(clock);
        assert.equal(clock.textContent, 'Phiếu đã bắt đầu · Chưa có mốc giờ');
        assert.equal(clock.classList.contains('is-unavailable'), true);
        await p.tick();
        assert.equal(clock.textContent, 'Phiếu đã bắt đầu · Chưa có mốc giờ');
        assert.equal(p.requests.some(route => route.includes('item-history')), false);
        assert.equal(p.writes.length, 0);
    }
});

test('GET làm mới thất bại không tiếp tục báo đồng hồ từ phiếu cũ là dữ liệu hiện tại', async () => {
    const p = await page({ withTicket: true, ticketStatus: 'Preparing', startedAt: '2026-11-12T09:55:00Z' });
    assert.equal(p.clocks()[0].textContent, 'Phiếu đã bắt đầu · 10:00');
    p.fail(true); await p.poll();
    assert.equal(p.clocks()[0].textContent, 'Thời gian chưa cập nhật');
    assert.equal(p.clocks()[0].classList.contains('is-unavailable'), true);
    const requests = p.requests.length, htmlAssignments = p.htmlAssignments();
    await p.tick();
    assert.equal(p.clocks()[0].textContent, 'Thời gian chưa cập nhật');
    assert.equal(p.requests.length, requests);
    assert.equal(p.htmlAssignments(), htmlAssignments);
    assert.equal(p.requests.some(route => route.includes('item-history')), false);
    assert.equal(p.writes.length, 0);
});

test('Nếu DTO sau này có mốc riêng từng món, Chef hiển thị đúng mà không cần quyền đọc item-history', async () => {
    const p = await page({ withTicket: true, ticketStatus: 'Preparing', startedAt: '2026-11-12T09:55:00Z', preparationStartedAt: '2026-11-12T17:03:00+07:00' });
    const byScope = new Map(p.clocks().map(node => [node.dataset.preparationScope, node]));
    assert.equal(byScope.size, 3);
    assert.equal(byScope.get('Phiếu đã bắt đầu').textContent, 'Phiếu đã bắt đầu · 10:00');
    assert.equal(byScope.get('Đang làm').textContent, 'Đang làm · 02:00');
    const requests = p.requests.length;
    await p.tick();
    assert.equal(byScope.get('Phiếu đã bắt đầu').textContent, 'Phiếu đã bắt đầu · 10:01');
    assert.equal(byScope.get('Đang làm').textContent, 'Đang làm · 02:01');
    assert.equal(p.requests.length, requests);
    assert.equal(p.requests.some(route => route.includes('item-history')), false);
    assert.equal(p.writes.length, 0);
});

function ticket(id, sentAt, priority = 0, status = 'Pending') {
    return { kitchenOrderId: id, orderId: id + 100, tableNumber: `B${id}`, sentAt, priority, status };
}
function item(id, status, quantity = 1, extra = {}) {
    return { id, orderItemId: id + 1000, foodName: `Món ${id}`, sizeName: 'Phần', status, quantity, ...extra };
}
function customPageData(tickets, customItems, closedOrderIds = []) {
    return { customTickets: tickets, customItems, customOrders: Object.fromEntries(tickets.map(ticket => [ticket.orderId,
        { status: closedOrderIds.includes(ticket.orderId) ? 'Paid' : 'Preparing', items: customItems[ticket.kitchenOrderId].map(item => ({ id: item.orderItemId })) }])) };
}
function laneHtml(p, key) {
    const html = p.node('#kitchenBoard').innerHTML;
    const start = html.indexOf(`aria-labelledby="kitchen-column-${key}"`);
    const end = html.indexOf('<section class="kitchen-column', start + 1);
    return html.slice(start, end < 0 ? undefined : end);
}
function lineHtml(p, id) {
    return p.node('#kitchenBoard').innerHTML.match(new RegExp(`<li\\b[^>]*data-kitchen-line="${id}"[^>]*>([\\s\\S]*?)<\\/li>`))?.[1] || '';
}
function ticketOrder(p, key = 'pending') {
    return [...laneHtml(p, key).matchAll(/data-kitchen-ticket="(\d+)"/g)].map(([, id]) => Number(id));
}

test('Phiếu có món ở ba trạng thái chia đúng làn, mỗi dòng một lần và tổng phiếu không cộng trùng', async () => {
    const t = ticket(10, '2026-11-12T09:00:00Z', 0, 'Preparing');
    const p = await page(customPageData([t], { 10: [item(1, 'Pending', 2), item(2, 'Preparing', 3), item(3, 'Completed', 1)] }));
    assert.deepEqual(summaryValues(p), ['1', '1', '1', '1']);
    for (const [key, id, quantity] of [['pending', 1, 2], ['preparing', 2, 3], ['ready', 3, 1]]) {
        const lane = laneHtml(p, key);
        assert.match(lane, new RegExp(`data-kitchen-line="${id}"`));
        assert.match(lane, new RegExp(`1 món · ${quantity} phần`));
        for (const other of [1, 2, 3].filter(other => other !== id)) assert.doesNotMatch(lane, new RegExp(`data-kitchen-line="${other}"`));
        assert.equal([...p.node('#kitchenBoard').innerHTML.matchAll(new RegExp(`data-kitchen-line="${id}"`, 'g'))].length, 1);
    }
    assert.equal(p.node('#kitchenWaitingCount').textContent, '1 món chưa nhận · 2 phần');
    assert.equal(p.writes.length, 0);
});

test('Cảnh báo chưa nhận là toàn cục dù lọc/làn che món; bỏ đơn đã đóng và nút xem đưa về đầy đủ hàng chờ', async () => {
    const tickets = [ticket(10, '2026-11-12T09:00:00Z', 0, 'Preparing'), ticket(20, '2026-11-12T09:10:00Z', 1), ticket(30, '2026-11-12T08:00:00Z')];
    const p = await page(customPageData(tickets, { 10: [item(1, 'Pending', 2)], 20: [item(2, 'Pending', 3)], 30: [item(3, 'Pending', 10)] }, [130]));
    const initial = p.node('#kitchenWaitingCount').textContent;
    assert.equal(initial, '2 món chưa nhận · 5 phần');
    assert.match(p.node('#kitchenWaitingOldest').textContent, /16:00:00/);
    p.node('#kitchenSearch').value = 'không tồn tại';
    p.node('#kitchenSearch').emit('input', { target: p.node('#kitchenSearch') });
    p.node('#priorityFilters').emit('click', { target: { closest: () => ({ dataset: { kitchenPriority: 'high' } }) } });
    p.node('#kitchenStatusTabs').emit('click', { target: { closest: () => ({ dataset: { kitchenStage: 'preparing' } }) } });
    assert.equal(p.node('#kitchenWaitingAlert').hidden, false);
    assert.equal(p.node('#kitchenWaitingCount').textContent, initial);
    assert.equal(ticketOrder(p).length, 0);
    const reads = p.requests.length;
    p.node('#kitchenShowWaiting').emit('click');
    assert.equal(p.node('#kitchenSearch').value, '');
    assert.deepEqual(ticketOrder(p), [10, 20]);
    assert.match(p.node('#kitchenBoard').innerHTML, /class="kitchen-column is-active-status" aria-labelledby="kitchen-column-pending"/);
    assert.equal(p.node('#kitchenBoard').scrolled, true);
    assert.equal(p.requests.length, reads);
    assert.equal(p.writes.length, 0);
});

test('Thứ tự mặc định FIFO độc lập priority, tie theo ID, thiếu mốc cuối; đổi ưu tiên là nhóm rồi FIFO không gọi API', async () => {
    const tickets = [ticket(30, '2026-11-12T09:04:00Z'), ticket(20, '2026-11-12T09:02:00Z', 1), ticket(10, '2026-11-12T09:02:00Z'),
        ticket(40, '2026-11-12T09:03:00Z', 5), ticket(50, 'invalid', 9)];
    const p = await page(customPageData(tickets, Object.fromEntries(tickets.map(t => [t.kitchenOrderId, [item(t.kitchenOrderId, 'Pending')]]))));
    assert.deepEqual(ticketOrder(p), [10, 20, 40, 30, 50]);
    assert.match(p.node('#kitchenQueueHint').textContent, /^Phiếu gửi trước đứng trước/);
    const reads = p.requests.length;
    p.node('#kitchenSort').emit('change', { target: { value: 'priority' } });
    assert.deepEqual(ticketOrder(p), [20, 40, 10, 30, 50]);
    assert.match(p.node('#kitchenQueueHint').textContent, /^Phiếu ưu tiên trước/);
    assert.equal(p.requests.length, reads);
    assert.equal(p.writes.length, 0);
});

test('Giờ riêng món chỉ dùng orderedAt khi có; mốc phiếu vẫn ghi rõ phạm vi và không giả từ createdAt của clone', async () => {
    const t = ticket(10, '2026-11-12T09:00:00Z', 0, 'Preparing');
    const p = await page(customPageData([t], { 10: [item(1, 'Pending', 1, { orderedAt: '2026-11-12T09:58:00Z' }),
        item(2, 'Pending', 1, { createdAt: '2026-11-12T09:59:00Z' })] }));
    assert.match(p.node('#kitchenBoard').innerHTML, /Gửi phiếu lúc 16:00:00/);
    assert.match(lineHtml(p, 1), /Gọi lúc 16:58:00/);
    assert.doesNotMatch(lineHtml(p, 2), /Gọi lúc|Chờ bếp nhận ·/);
    const clock = p.clocks().find(node => node.dataset.preparationScope === 'Chờ bếp nhận');
    assert.equal(clock.textContent, 'Chờ bếp nhận · 07:00');
    assert.ok(p.clocks().some(node => node.textContent === 'Phiếu gửi cách đây · 1:05:00'));
    const reads = p.requests.length; await p.tick();
    assert.equal(clock.textContent, 'Chờ bếp nhận · 07:01');
    assert.equal(p.requests.length, reads);
    assert.equal(p.requests.some(route => route.includes('item-history')), false);
    assert.equal(p.writes.length, 0);
});

test('Nhận một món chỉ mutate fixture trong bộ nhớ, vẫn giữ làn chờ để không khuất món Pending còn lại', async () => {
    const t = ticket(10, '2026-11-12T09:00:00Z');
    const p = await page({ ...customPageData([t], { 10: [item(1, 'Pending'), item(2, 'Pending', 2)] }), allowReceive: true });
    const receive = p.node('#kitchenBoard').actions.find(button => button.dataset.kitchenAction === 'receive' && button.dataset.itemId === '1');
    p.node('#kitchenBoard').emit('click', { target: receive }); await settle();
    assert.equal(p.writes.length, 1);
    assert.equal(p.writes[0].method, 'patch');
    assert.equal(p.writes[0].args[0], '/management/kitchen/items/1/status');
    assert.match(laneHtml(p, 'pending'), /data-kitchen-line="2"/);
    assert.doesNotMatch(laneHtml(p, 'pending'), /data-kitchen-line="1"/);
    assert.match(laneHtml(p, 'preparing'), /data-kitchen-line="1"/);
    assert.match(p.node('#kitchenBoard').innerHTML, /class="kitchen-column is-active-status" aria-labelledby="kitchen-column-pending"/);
    assert.equal(p.node('#kitchenWaitingCount').textContent, '1 món chưa nhận · 2 phần');
});

test('Giữ focus ở tìm kiếm không ngừng poll nhận phiếu mới; hộp thoại xử lý đang mở vẫn chặn poll', async () => {
    const p = await page({ withTicket: true });
    p.focusFilter();
    const before = p.requests.length;
    await p.poll();
    assert.ok(p.requests.length > before, 'A focused toolbar must not permanently pause new kitchen reads');
    assert.equal(p.requests.filter(route => route === '/management/kitchen/orders?status=open').length, 2);
    assert.equal(p.writes.length, 0);
    p.node('#kitchenActionDialog').showModal();
    const whileDialog = p.requests.length;
    await p.poll();
    assert.equal(p.requests.length, whileDialog, 'Keep operation dialogs stable while the employee edits');
    assert.equal(p.writes.length, 0);
});

test('In từ cột đang chế biến xem cả món còn xử lý của phiếu, không tự in/nhận món và tạm ngừng poll', async () => {
    const t = ticket(10, '2026-11-12T09:00:00Z', 0, 'Preparing');
    const p = await page(customPageData([t], { 10: [item(1, 'Pending'), item(2, 'Preparing'), item(3, 'Completed')] }));
    const printButtons = p.node('#kitchenBoard').prints;
    assert.equal(printButtons.length, 3);
    const before = p.requests.length;
    p.node('#kitchenBoard').emit('click', { target: printButtons[1] }); await settle();
    assert.ok(p.requests.length > before);
    assert.equal(p.node('#kitchenPrintDialog').open, true);
    assert.equal((p.node('#kitchenPrintPreview').innerHTML.match(/<article class="kitchen-slip"/g) || []).length, 2, 'Each dish has its own slip');
    assert.equal((p.node('#kitchenPrintPreview').innerHTML.match(/class="kitchen-slip-item /g) || []).length, 2);
    assert.doesNotMatch(p.node('#kitchenPrintPreview').innerHTML, /CHỜ NHẬN|ĐANG CHẾ BIẾN|MÓN CẦN LÀM/);
    assert.doesNotMatch(p.node('#kitchenPrintPreview').innerHTML, /ĐÃ XONG/);
    assert.equal(p.node('#kitchenPrintPaper').innerHTML, '');
    const reading = p.requests.length;
    await p.poll(); assert.equal(p.requests.length, reading);
    p.node('#kitchenPrintCancel').emit('click');
    assert.equal(p.node('#kitchenPrintDialog').open, false);
    assert.equal(p.node('#kitchenPrintPreview').innerHTML, '');
    await p.poll(); assert.ok(p.requests.length > reading);
    assert.equal(p.writes.length, 0);
});
