const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const context = vm.createContext({ window: {}, URLSearchParams });
vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common/payment-session.js'), 'utf8'), context);
const { PaymentSession, payBlock, snapshot, dateBound } = context.window.restaurantPayments;
const copy = (x) => JSON.parse(JSON.stringify(x));
function storage() { const map = new Map(); return { getItem: (k) => map.get(k) || null, setItem: (k, v) => map.set(k, v), removeItem: (k) => map.delete(k) }; }
function setup({ role = 'Cashier', saved = storage() } = {}) {
    const calls = [];
    const server = { order: { orderId: 42, tableId: 3, status: 'Ready', totalAmount: 87913, subtotal: 90000, discountAmount: 5000, taxAmount: 913, serviceChargeAmount: 2000, updatedAt: '2026-10-31T01:00:00Z',
        items: [{ id: 1, status: 'Completed', quantity: 2, unitPrice: 40000 }, { id: 2, status: 'Cancelled', quantity: 1, unitPrice: 90000 }] }, payments: [], get: null, write: null };
    const auth = { getUser: () => ({ userId: 8 }), hasRole: (...roles) => roles.includes(role) };
    const api = { get: async (url) => {
        calls.push({ method: 'get', url }); if (server.get) return copy(await server.get(url));
        if (url === '/restaurant-tables') return [{ tableId: 3, tableNumber: 'TEST', status: server.order.status === 'Paid' ? 'Available' : 'Occupied' }];
        if (url.startsWith('/orders?')) { const q = new URL('http://test' + url).searchParams; return { page: Number(q.get('page')), pageSize: 100, total: q.get('status') === server.order.status ? 1 : 0, items: q.get('status') === server.order.status ? [copy(server.order)] : [] }; }
        if (url.startsWith('/payments/order/')) return copy(server.payments);
        if (url.startsWith('/payments?')) return { items: copy(server.payments), total: server.payments.length, page: 1, pageSize: 20 };
        return copy(server.order);
    }, post: async (url, body, options) => {
        calls.push({ method: 'post', url, body: body === undefined ? undefined : copy(body), options: copy(options) });
        if (server.write) return server.write(url, body);
        if (url === '/payments') {
            const p = { paymentId: 10, orderId: 42, amount: server.order.totalAmount, status: 'Paid', paymentMethod: body.paymentMethod, transactionCode: body.transactionCode, provider: 'Direct' };
            server.payments.push(p); server.order.status = 'Paid'; return copy(p);
        }
        if (url.endsWith('/refund')) { server.payments[0].status = 'Refunded'; server.order.status = 'Ready'; return copy(server.payments[0]); }
        if (url === '/payments/momo') {
            let p = server.payments.find((x) => x.status === 'Pending');
            if (!p) { p = { paymentId: 20, orderId: 42, amount: server.order.totalAmount, status: 'Pending', paymentMethod: 'MoMo', provider: 'MoMo' }; server.payments.push(p); }
            return { ...copy(p), isMock: true, redirectUrl: '/api/payments/momo/mock/20', qrCodeUrl: '' };
        }
        if (url.includes('/momo/mock/')) { const success = url.endsWith('true'); server.payments[0].status = success ? 'Paid' : 'Failed'; if (success) server.order.status = 'Paid'; return { accepted: true }; }
    } };
    const create = () => new PaymentSession({ api, auth, storage: saved, makeKey: () => 'unique-request-key' });
    return { session: create(), create, calls, server, saved };
}
const writes = (b) => b.calls.filter((c) => c.method === 'post');

test('queue đọc cả Ready và Completed, không dùng GET bàn có tác dụng ghi', async () => {
    const b = setup(); await b.session.loadQueue();
    assert.equal(b.session.orders[0].totalAmount, 87913);
    assert.ok(b.calls.some((c) => c.url.includes('status=Completed')));
    assert.equal(b.calls.some((c) => c.url.startsWith('/restaurant-tables/')), false);
});
test('phân trang queue đọc hết, không chỉ trang đầu', async () => {
    const b = setup();
    b.server.get = (url) => url === '/restaurant-tables' ? [] : { total: 101, items: Array.from({ length: url.includes('page=2') ? 1 : 100 }, (_, i) => ({ orderId: (url.includes('page=2') ? 100 : 0) + i, status: 'Ready' })) };
    await b.session.loadQueue(); assert.equal(b.session.orders.length, 101);
    assert.ok(b.calls.some((c) => c.url.includes('page=2')));
});
test('Cashier và Admin được truy cập; Waiter, Chef bị chặn trước mọi request', async () => {
    for (const role of ['Waiter', 'Chef']) { const b = setup({ role }); await assert.rejects(b.session.loadQueue(), (e) => e.status === 403); assert.equal(b.calls.length, 0); }
    assert.equal(setup({ role: 'Admin' }).session.hasAccess(), true);
});
test('chỉ đơn có món hoàn tất và tổng dương mới được thu', async () => {
    const b = setup(), order = b.server.order;
    assert.equal(payBlock(order), '');
    for (const status of ['Pending', 'SentToKitchen', 'Preparing', 'Paid', 'Cancelled']) assert.ok(payBlock({ ...order, status }));
    assert.ok(payBlock({ ...order, totalAmount: 0 })); assert.ok(payBlock({ ...order, items: [] }));
    assert.ok(payBlock({ ...order, items: [{ status: 'Cancelled' }] }));
    assert.ok(payBlock({ ...order, items: [{ status: 'Preparing' }] }));
});
test('thu tiền dùng amount server, không gửi amount và có idempotency + mã đối chiếu ổn định', async () => {
    const b = setup(); await b.session.select(42); const expected = snapshot(b.session.order);
    const result = await b.session.command('direct', { method: 'Cash' }, expected);
    assert.equal(result.amount, 87913); assert.equal(b.session.order.totalAmount, 87913); assert.equal(b.session.order.status, 'Paid');
    assert.deepEqual(writes(b)[0].body, { orderId: 42, paymentMethod: 'Cash', transactionCode: 'FE-unique-request-key', createdBy: 8 });
    assert.equal(writes(b)[0].options.headers['Idempotency-Key'], 'unique-request-key'); assert.equal(b.session.pending, null);
});
test('phương thức phải thuộc allowlist; MoMo đi intent, không giả lập qua payments trực tiếp', async () => {
    const b = setup(); await b.session.select(42);
    for (const method of ['cash', 'Unknown', 'MoMo']) await assert.rejects(b.session.command('direct', { method }), (e) => e.status === 400);
    assert.equal(writes(b).length, 0);
});
test('mã giao dịch người dùng được trim, không thay mã khi đã nhập', async () => {
    const b = setup(); await b.session.select(42); await b.session.command('direct', { method: 'Transfer', transactionCode: ' BANK-123 ' });
    assert.equal(writes(b)[0].body.transactionCode, 'BANK-123');
});
test('đơn đổi tiền hoặc trạng thái trong lúc hộp xác nhận mở thì không ghi', async () => {
    const b = setup(); await b.session.select(42); const expected = snapshot(b.session.order); b.server.order.totalAmount = 99000;
    await assert.rejects(b.session.command('direct', { method: 'Cash' }, expected), /vừa thay đổi/); assert.equal(writes(b).length, 0);
});
test('gọi thêm món sau lần xem trước bị chặn bởi dữ liệu fresh dù không truyền snapshot', async () => {
    const b = setup(); await b.session.select(42); b.server.order.status = 'SentToKitchen';
    await assert.rejects(b.session.command('direct', { method: 'Cash' }), /chờ bếp/); assert.equal(writes(b).length, 0);
});
test('khóa bấm hai lần trong khi đang đọc/ghi', async () => {
    const b = setup(); await b.session.select(42);
    const first = b.session.command('direct', { method: 'Cash' });
    await assert.rejects(b.session.command('direct', { method: 'Cash' }), /Chờ/); await first; assert.equal(writes(b).length, 1);
});
test('mất ACK sau khi đã thu: reload giữ yêu cầu, review chỉ GET và tìm bằng transactionCode', async () => {
    const b = setup(); await b.session.select(42);
    b.server.write = (_, body) => { b.server.payments.push({ paymentId: 9, status: 'Paid', transactionCode: body.transactionCode }); b.server.order.status = 'Paid'; throw Object.assign(new Error('Mất kết nối'), { status: 0 }); };
    await assert.rejects(b.session.command('direct', { method: 'Cash' })); assert.ok(b.session.pending);
    const reloaded = b.create(); assert.ok(reloaded.pending);
    const result = await reloaded.reviewPending(); assert.equal(result.resolved, true); assert.equal(reloaded.pending, null); assert.equal(writes(b).length, 1);
});
test('ACK thành công nhưng GET lỗi không gửi lại khoản thu', async () => {
    const b = setup(); await b.session.select(42);
    b.server.write = (_, body) => { b.server.get = () => { throw new Error('GET lỗi'); }; return { paymentId: 9, transactionCode: body.transactionCode }; };
    await assert.rejects(b.session.command('direct', { method: 'Cash' })); assert.equal(b.session.pending.accepted, true);
    b.server.get = null; await b.session.reviewPending(); assert.equal(b.session.pending, null); assert.equal(writes(b).length, 1);
});
test('mất kết nối chưa rõ kết quả phải tải rồi xác nhận đối chiếu, không tự retry', async () => {
    const b = setup(); await b.session.select(42); b.server.write = () => { throw Object.assign(new Error('offline'), { status: 0 }); };
    await assert.rejects(b.session.command('direct', { method: 'Cash' }));
    assert.throws(() => b.session.acknowledgeReview()); await assert.rejects(b.session.command('direct', { method: 'Cash' }));
    const result = await b.session.reviewPending(); assert.equal(result.resolved, false); b.session.acknowledgeReview(); assert.equal(b.session.pending, null); assert.equal(writes(b).length, 1);
});
test('phiếu đã thu rồi hoàn vẫn đủ để đối chiếu cash mất ACK, không tự thu lại', async () => {
    const b = setup(); await b.session.select(42);
    b.server.write = (_, body) => { b.server.payments.push({ paymentId: 9, status: 'Refunded', transactionCode: body.transactionCode }); throw new Error('ACK mất'); };
    await assert.rejects(b.session.command('direct', { method: 'Cash' })); assert.equal((await b.session.reviewPending()).resolved, true); assert.equal(writes(b).length, 1);
});
test('409 giữ nguyên message, không giả trạng thái Paid', async () => {
    const b = setup(); await b.session.select(42); b.server.write = () => { throw Object.assign(new Error("Transaction code 'BANK-1' already exists"), { status: 409 }); };
    await assert.rejects(b.session.command('direct', { method: 'Bank', transactionCode: 'BANK-1' }), /already exists/);
    assert.equal(b.session.order.status, 'Ready'); assert.equal(b.session.pending, null);
});
test('hoàn tiền yêu cầu lý do và phiếu Paid; nhận trạng thái Ready từ backend', async () => {
    const b = setup(); await b.session.select(42); await b.session.command('direct', { method: 'Cash' });
    await assert.rejects(b.session.command('refund', { paymentId: 10, reason: ' ' }), (e) => e.status === 400);
    await b.session.command('refund', { paymentId: 10, reason: ' Khách đổi phương thức ' });
    assert.equal(b.session.order.status, 'Ready'); assert.equal(b.session.payments[0].status, 'Refunded');
    assert.deepEqual(writes(b)[1].body, { reason: 'Khách đổi phương thức', createdBy: 8 });
    await assert.rejects(b.session.command('refund', { paymentId: 10, reason: 'Hoàn lại' }));
});
test('hoàn tiền mất ACK được đối chiếu bằng paymentId và trạng thái Refunded', async () => {
    const b = setup(); await b.session.select(42); await b.session.command('direct', { method: 'Cash' });
    b.server.write = () => { b.server.payments[0].status = 'Refunded'; b.server.order.status = 'Ready'; throw new Error('Mất ACK'); };
    await assert.rejects(b.session.command('refund', { paymentId: 10, reason: 'Thử hoàn' }));
    assert.equal((await b.session.reviewPending()).resolved, true); assert.equal(writes(b).length, 2);
});
test('bàn đã có đơn mới thì chặn hoàn đơn cũ trước khi gửi POST refund', async () => {
    const b = setup(); await b.session.select(42); await b.session.command('direct', { method: 'Cash' });
    b.server.get = (url) => url.startsWith('/orders?') ? { total: 1, items: [{ orderId: 43, tableId: 3, status: 'Preparing' }] }
        : url.startsWith('/payments/order/') ? b.server.payments : b.server.order;
    await assert.rejects(b.session.command('refund', { paymentId: 10, reason: 'Hoàn đơn cũ' }), /đơn khác đang mở/);
    assert.equal(writes(b).length, 1); assert.equal(b.server.order.status, 'Paid');
    assert.ok(b.calls.some((c) => c.url.includes('tableId=3')));
});
test('intent MoMo giữ Pending, chặn thu trực tiếp và chỉ dùng nút Mock khi isMock=true', async () => {
    const b = setup(); await b.session.select(42); await b.session.command('momo');
    assert.equal(b.session.order.status, 'Ready'); assert.equal(b.session.payments[0].status, 'Pending'); assert.equal(b.session.intent().isMock, true);
    await assert.rejects(b.session.command('direct', { method: 'Cash' }), /đang chờ/);
    await b.session.command('mock', { success: true }); assert.equal(b.session.order.status, 'Paid');
});
test('khôi phục intent đang Pending dùng cùng paymentId, không thu tiền', async () => {
    const b = setup(); await b.session.select(42); await b.session.command('momo'); await b.session.command('momo');
    assert.equal(b.server.payments.length, 1); assert.equal(b.session.intent().paymentId, 20); assert.equal(b.session.order.status, 'Ready');
});
test('Mock thất bại nhận Failed, cho phép đổi sang tiền mặt sau khi GET xác nhận', async () => {
    const b = setup(); await b.session.select(42); await b.session.command('momo'); await b.session.command('mock', { success: false });
    assert.equal(b.session.payments[0].status, 'Failed'); assert.equal(b.session.block(), ''); assert.equal(b.session.order.status, 'Ready');
});
test('MoMo thật không được gọi route giả lập, URL provider không quyết định trạng thái đơn', async () => {
    const b = setup(); await b.session.select(42); await b.session.command('momo'); b.session.momo.isMock = false;
    await assert.rejects(b.session.command('mock', { success: true }), (e) => e.status === 400); assert.equal(b.session.order.status, 'Ready');
});
test('tổng đơn đổi sau intent chặn Mock thành công; Mock thất bại vẫn dùng để đối chiếu môi trường thử', async () => {
    const b = setup(); await b.session.select(42); await b.session.command('momo'); b.server.order.totalAmount += 1;
    await assert.rejects(b.session.command('mock', { success: true }), /Số tiền|số tiền/); assert.equal(writes(b).length, 1);
    await b.session.command('mock', { success: false }); assert.equal(b.session.payments[0].status, 'Failed');
});
test('ngày lọc đổi trọn ngày VN sang UTC; ngày không tồn tại bị chặn', () => {
    assert.equal(dateBound('2026-10-31'), '2026-10-30T17:00:00.000Z');
    assert.equal(dateBound('2026-10-31', true), '2026-10-31T16:59:59.999Z');
    assert.throws(() => dateBound('2026-02-30')); assert.throws(() => dateBound('31/10/2026'));
});
test('lọc phiếu thu gửi method/status/order và phân trang; không cộng doanh thu ở client', async () => {
    const b = setup(); await b.session.loadLedger({ method: 'Cash', status: 'Refunded', orderId: '42', from: '2026-10-31', to: '2026-10-31' }, 2);
    const q = new URL('http://test' + b.calls[0].url).searchParams;
    assert.equal(q.get('page'), '2'); assert.equal(q.get('method'), 'Cash'); assert.equal(q.get('to'), '2026-10-31T16:59:59.999Z');
    await assert.rejects(b.session.loadLedger({ from: '2026-11-01', to: '2026-10-31' }));
});
test('thông tin yêu cầu của người khác không được khôi phục sang phiên mới', () => {
    const saved = storage(); saved.setItem('restaurant.paymentPending', JSON.stringify({ userId: 999, orderId: 42, kind: 'direct' }));
    assert.equal(setup({ saved }).session.pending, null);
});
