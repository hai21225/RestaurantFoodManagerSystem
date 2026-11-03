const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const context = vm.createContext({ window: {} });
vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common/order-session.js'), 'utf8'), context);
const { OrderSession, groups } = context.window.restaurantPos;
const copy = (value) => value == null ? value : JSON.parse(JSON.stringify(value));
const line = (id, status = 'Pending', quantity = 2, orderItemGroupId = null) => ({ id, status, quantity, orderItemGroupId, sizeId: 5, foodName: 'Món thử', sizeName: 'M', unitPrice: 9100 });
const order = (items = [line(1)], status = 'SentToKitchen') => ({ orderId: 42, tableId: 3, status, items, subtotal: 12345, discountAmount: 600, taxAmount: 217, serviceChargeAmount: 350, totalAmount: 12312 });
function storage() {
    const data = new Map();
    return { getItem: (key) => data.get(key) || null, setItem: (key, value) => data.set(key, value), removeItem: (key) => data.delete(key) };
}
function setup({ initial = order(), roles = ['Waiter'], saved = storage() } = {}) {
    const requests = [];
    const server = { order: copy(initial), failRead: false, get: null, write: null };
    const auth = { getUser: () => ({ userId: 8 }), hasRole: (...allowed) => roles.some((role) => allowed.includes(role)) };
    const api = {
        get: async (url) => {
            requests.push({ method: 'get', url });
            if (server.get) return copy(await server.get(url));
            if (url === '/restaurant-tables') return [{ tableId: 3, tableNumber: 'TEST-3', isActive: true, status: server.order ? 'Occupied' : 'Available' }];
            if (url.startsWith('/orders?')) return { items: server.order ? [server.order] : [], total: server.order ? 1 : 0 };
            if (server.failRead) throw Object.assign(new Error('network'), { status: 0 });
            return copy(server.order);
        }
    };
    for (const method of ['post', 'patch']) api[method] = async (url, body, options) => {
        requests.push({ method, url, body: copy(body), options: copy(options) });
        return server.write ? copy(await server.write(url, body)) : { orderId: 42 };
    };
    let sequence = 0;
    const create = () => new OrderSession({ tableId: 3, api, auth, storage: saved, makeKey: () => `operation-${++sequence}` });
    return { session: create(), create, requests, server, roles, saved };
}

test('tìm đúng đơn đang mở sau hơn 100 đơn cũ, không gọi GET bàn/{id}', async () => {
    const b = setup();
    b.server.get = (url) => url === '/restaurant-tables' ? [{ tableId: 3, isActive: true, status: 'Occupied' }]
        : url.includes('page=1&') ? { items: Array.from({ length: 100 }, () => ({ status: 'Paid' })), total: 101 }
        : url.includes('page=2&') ? { items: [{ orderId: 42, status: 'Completed' }], total: 101 } : order([], 'Completed');
    await b.session.refresh();
    assert.equal(b.session.order.orderId, 42);
    assert.equal(b.requests.filter((r) => r.url.startsWith('/orders?')).length, 2);
    assert.equal(b.requests.some((r) => r.url === '/restaurant-tables/3'), false);
});

test('không đọc được đơn hoặc có nhiều đơn mở thì chặn tạo đơn mới', async () => {
    const b = setup();
    b.server.get = (url) => url === '/restaurant-tables' ? [{ tableId: 3, isActive: true }]
        : { items: [order(), { ...order(), orderId: 43 }], total: 2 };
    await assert.rejects(b.session.refresh(), /nhiều đơn/);
    await assert.rejects(b.session.send([{ sizeId: 5, quantity: 1, toppings: [] }]), /tải xong/);
    assert.equal(b.requests.some((r) => r.method === 'post'), false);
});

test('bàn trống tạo đơn, gửi ghi chú/topping/actor và giữ số tiền server trả', async () => {
    const b = setup({ initial: null });
    await b.session.refresh();
    b.server.write = () => { b.server.order = order(); return { orderId: 42 }; };
    const result = await b.session.send([{ sizeId: 5, quantity: 2, note: 'Ít cay', toppings: [{ toppingId: 9, quantity: 1 }] }], 'Ghi chú hóa đơn');
    const command = b.requests.find((r) => r.method === 'post');
    assert.equal(command.url, '/orders');
    assert.equal(command.body.tableId, 3);
    assert.equal(command.body.createdBy, 8);
    assert.equal(command.body.note, 'Ghi chú hóa đơn');
    assert.equal(command.body.items[0].toppings[0].toppingId, 9);
    assert.ok(command.options.headers['Idempotency-Key']);
    assert.equal(result.order.totalAmount, 12312);
});

test('đơn Completed vẫn gọi thêm vào đơn cũ, không POST /orders mới', async () => {
    const b = setup({ initial: order([line(1, 'Completed')], 'Completed') });
    await b.session.refresh();
    b.server.write = () => { b.server.order = order([line(1, 'Completed'), line(2)]); return b.server.order; };
    await b.session.send([{ sizeId: 5, quantity: 1, toppings: [] }]);
    const writes = b.requests.filter((r) => r.method === 'post');
    assert.equal(writes.length, 1);
    assert.equal(writes[0].url, '/orders/42/items');
    assert.equal(writes[0].body.changedBy, 8);
    assert.equal(b.session.order.status, 'SentToKitchen');
});

test('đổi tổng nhóm dòng sau khi tách món, không nhầm số lượng một dòng', async () => {
    const b = setup({ initial: order([line(1, 'Pending', 2, 'group-A'), line(2, 'Pending', 3, 'group-A'), line(3, 'Cancelled', 9, 'group-A')]) });
    await b.session.refresh();
    assert.equal(groups(b.session.order)[0].quantity, 5);
    await b.session.changeQuantity('group-A', 4, 'Khách giảm một phần');
    const command = b.requests.find((r) => r.method === 'patch');
    assert.equal(command.url, '/orders/42/items/1/quantity');
    assert.equal(command.body.quantity, 4);
    assert.equal(command.body.delta, undefined);
    assert.equal(command.body.reasonCode, 'CustomerChange');
});

test('Waiter không giảm/hủy Preparing/Ready, vẫn tăng được bằng lệnh quantity', async () => {
    for (const status of ['Preparing', 'Ready', 'Completed']) {
        const b = setup({ initial: order([line(1, status)], status === 'Completed' ? 'Completed' : 'Preparing') });
        await b.session.refresh();
        assert.equal(b.session.canCancel(b.session.order.items[0]), false);
        await assert.rejects(b.session.changeQuantity('item-1', 1, 'Giảm'), /Bếp đã nhận/);
        await assert.rejects(b.session.cancelItem(1, 'Hủy'), /không còn được phép/);
        await b.session.changeQuantity('item-1', 3, 'Khách gọi tăng');
        assert.equal(b.requests.filter((r) => r.method === 'patch').length, 1);
    }
});

test('Waiter hủy dòng Pending, gửi lý do thật và không chọn mã hao hụt của Admin', async () => {
    const b = setup();
    await b.session.refresh();
    await assert.rejects(b.session.cancelItem(1, '   '), /lý do/);
    await b.session.cancelItem(1, 'Khách đổi món', 'PhysicalShortage');
    const command = b.requests.find((r) => r.method === 'post');
    assert.equal(command.body.reason, 'Khách đổi món');
    assert.equal(command.body.reasonCode, 'CustomerChange');
    assert.equal(command.body.changedBy, 8);
});

test('Admin giảm dưới phần đã xong phải xác nhận hao hụt, gửi allowWastage', async () => {
    const b = setup({ roles: ['Admin'], initial: order([line(1, 'Completed', 3), line(2, 'Pending')], 'Preparing') });
    await b.session.refresh();
    await assert.rejects(b.session.changeQuantity('item-1', 1, 'Hỏng', false), /hao hụt/);
    await b.session.changeQuantity('item-1', 1, 'Hỏng', true);
    assert.equal(b.requests.find((r) => r.method === 'patch').body.allowWastage, true);
});

test('số lượng thập phân, âm và giảm về 0 thiếu lý do không được gửi API', async () => {
    const b = setup();
    await b.session.refresh();
    for (const quantity of [0.5, -1, 2147483648]) await assert.rejects(b.session.changeQuantity('item-1', quantity, 'test'), /số nguyên/);
    await assert.rejects(b.session.changeQuantity('item-1', 0, ''), /lý do/);
    await assert.rejects(b.session.send([{ sizeId: 5, quantity: 1.5 }]), /số nguyên/);
    assert.equal(b.requests.some((r) => r.method !== 'get'), false);
});

test('HTTP 403/409 giữ dữ liệu đã đọc, không giả đổi trạng thái hoặc tự tạo đơn', async () => {
    for (const status of [403, 409]) {
        const b = setup();
        await b.session.refresh();
        b.server.write = () => { throw Object.assign(new Error('Bếp vừa nhận món'), { status }); };
        await assert.rejects(b.session.changeQuantity('item-1', 1, 'Giảm'), /Bếp vừa nhận/);
        assert.equal(b.session.order.items[0].quantity, 2);
        assert.equal(b.session.order.status, 'SentToKitchen');
        assert.equal(b.session.pending, null);
        assert.equal(b.requests.filter((r) => r.method === 'post').length, 0);
    }
});

test('mất ACK sau gọi thêm: lưu body/key, chặn lệnh mới, retry cùng key sau reload', async () => {
    const b = setup();
    await b.session.refresh();
    let calls = 0;
    b.server.write = () => { if (!calls++) throw Object.assign(new Error('mất kết nối'), { status: 0 }); return order(); };
    await assert.rejects(b.session.send([{ sizeId: 5, quantity: 1, toppings: [] }]), /mất kết nối/);
    await assert.rejects(b.session.send([{ sizeId: 5, quantity: 2, toppings: [] }]), /chưa được xác nhận/);
    const resumed = b.create();
    await resumed.resume();
    const writes = b.requests.filter((r) => r.method === 'post');
    assert.equal(writes.length, 2);
    assert.equal(writes[0].url, '/orders/42/items');
    assert.deepEqual(writes[1].body, writes[0].body);
    assert.equal(writes[1].options.headers['Idempotency-Key'], writes[0].options.headers['Idempotency-Key']);
    assert.equal(resumed.pending, null);
});

test('ACK thành công nhưng GET lỗi: retry chỉ đọc lại, không gửi món lần nữa', async () => {
    const b = setup();
    await b.session.refresh();
    b.server.failRead = true;
    await assert.rejects(b.session.send([{ sizeId: 5, quantity: 1, toppings: [] }]), /network/);
    assert.equal(b.session.pending.accepted, true);
    const resumed = b.create();
    b.server.failRead = false;
    await resumed.resume();
    assert.equal(b.requests.filter((r) => r.method === 'post').length, 1);
});

test('hai lần bấm liên tiếp không gửi thêm hai lệnh', async () => {
    const b = setup();
    await b.session.refresh();
    let finish;
    b.server.write = () => new Promise((resolve) => { finish = resolve; });
    const first = b.session.send([{ sizeId: 5, quantity: 1, toppings: [] }]);
    await assert.rejects(b.session.send([{ sizeId: 5, quantity: 1, toppings: [] }]), /tải xong/);
    finish({ orderId: 42 });
    await first;
    assert.equal(b.requests.filter((r) => r.method === 'post').length, 1);
});

test('hủy mất ACK rồi reload: đã Cancelled thì chỉ đọc lại, không hủy hai lần', async () => {
    const b = setup();
    await b.session.refresh();
    b.server.write = () => { b.server.order = order([line(1, 'Cancelled')], 'Cancelled'); throw Object.assign(new Error('network'), { status: 0 }); };
    await assert.rejects(b.session.cancelItem(1, 'Khách đổi món'), /network/);
    const resumed = b.create();
    await resumed.resume();
    assert.equal(b.requests.filter((r) => r.method === 'post').length, 1);
    assert.equal(resumed.order.status, 'Cancelled');
});

test('đơn Paid/Cancelled không tự mở lượt mới khi người khác vừa kết thúc đơn', async () => {
    for (const status of ['Paid', 'Cancelled']) {
        const b = setup();
        await b.session.refresh();
        b.server.order.status = status;
        await b.session.refresh();
        assert.equal(b.session.order.status, status);
        await assert.rejects(b.session.send([{ sizeId: 5, quantity: 1, toppings: [] }]), /kết thúc/);
    }
});

test('Cashier chỉ xem; Admin có thể hủy cả đơn và gửi mã hao hụt', async () => {
    const read = setup({ roles: ['Cashier'] });
    await read.session.refresh();
    await assert.rejects(read.session.send([{ sizeId: 5, quantity: 1, toppings: [] }]), /Chỉ phục vụ/);
    const admin = setup({ roles: ['Admin'] });
    await admin.session.refresh();
    await admin.session.cancelOrder('Nguyên liệu hỏng', 'PhysicalShortage');
    const command = admin.requests.find((r) => r.method === 'patch');
    assert.equal(command.body.status, 'Cancelled');
    assert.equal(command.body.reasonCode, 'PhysicalShortage');
});

test('người khác vừa tạo đơn: 409 giữ giỏ, chỉ gọi thêm khi người dùng gửi lần tiếp', async () => {
    const b = setup({ initial: null });
    await b.session.refresh();
    b.server.write = () => { b.server.order = order(); throw Object.assign(new Error('Table already has an open order'), { status: 409 }); };
    const items = [{ sizeId: 5, quantity: 1, toppings: [] }];
    await assert.rejects(b.session.send(items), (e) => e.status === 409);
    assert.equal(b.requests.filter((r) => r.method === 'post').length, 1);
    await b.session.refresh();
    b.server.write = () => order();
    await b.session.send(items);
    assert.equal(b.requests.filter((r) => r.method === 'post')[1].url, '/orders/42/items');
});

test('đã nhận ACK nhưng đơn chuyển bàn: xác nhận kết quả, không giữ lệnh để gửi trùng', async () => {
    const b = setup();
    await b.session.refresh();
    b.server.write = () => { b.server.order.tableId = 9; return { orderId: 42 }; };
    const result = await b.session.send([{ sizeId: 5, quantity: 1, toppings: [] }]);
    assert.equal(result.moved, true);
    assert.equal(result.order.tableId, 9);
    assert.equal(b.session.order, null);
    assert.equal(b.session.loaded, false);
    assert.equal(b.session.pending, null);
});
