const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), vm = require('node:vm'), path = require('node:path');
const ctx = vm.createContext({ window: {}, URLSearchParams });
vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common/warehouse-session.js'), 'utf8'), ctx);
const { WarehouseSession, stockSnapshot, supplierSnapshot, purchaseSnapshot, dateBound } = ctx.window.restaurantWarehouse;
const copy = (x) => JSON.parse(JSON.stringify(x));
function storage() { const map = new Map(); return { getItem: (k) => map.get(k) || null, setItem: (k, v) => map.set(k, v), removeItem: (k) => map.delete(k) }; }
function setup({ role = 'WarehouseManager', mode = 'purchases', saved = storage() } = {}) {
    const calls = [], server = {
        inventory: [{ inventoryId: 1, ingredientId: 1, quanlity: 10, updatedAt: '2026-10-31T01:00:00Z' }],
        ingredients: [{ ingredientId: 1, name: 'Gạo', unit: 'kg', minimumStock: 1, isActive: true, stockStatus: 'Available' }, { ingredientId: 2, name: 'Hỏng', unit: 'kg', minimumStock: 1, isActive: false, stockStatus: 'Spoiled' }],
        low: [{ ingredientId: 2, quanlity: 20, minimumStock: 1, shortage: -19, stockStatus: 'Spoiled', isOutOfStock: false }],
        suppliers: [{ supplierId: 1, name: 'NCC Test', phone: '000', email: 'test@example.invalid', address: 'Địa chỉ thử', isActive: true }, { supplierId: 2, name: 'Tắt', isActive: false }],
        purchases: [{ id: 3, supplierId: 1, supplierName: 'NCC Test', status: 'Draft', totalAmount: 87913, items: [{ ingredientId: 1, quantity: 2, unitPrice: 40000, totalPrice: 87913 }] }], get: null, write: null
    };
    const auth = { getUser: () => ({ userId: 8 }), hasRole: (...roles) => roles.includes(role) };
    const api = { get: async (url) => {
        calls.push({ method: 'get', url }); if (server.get) return copy(await server.get(url));
        if (url === '/management/inventory') return copy(server.inventory);
        if (url === '/management/ingredients') return copy(server.ingredients);
        if (url === '/management/inventory/low-stock') return copy(server.low);
        if (url.startsWith('/management/inventory-transactions?')) return { page: 1, pageSize: 20, total: 1, items: [{ id: 1, quanlity: 1.25, quanlityBefore: 10, quanlityAfter: 11.25, unit: 'kg', note: 'Thử' }] };
        if (url === '/management/suppliers') return copy(server.suppliers);
        if (url.startsWith('/management/suppliers/')) return copy(server.suppliers.find((s) => s.supplierId === Number(url.split('/').at(-1))));
        if (url === '/management/purchase-orders') return copy(server.purchases);
        if (url.startsWith('/management/purchase-orders/')) return copy(server.purchases.find((p) => p.id === Number(url.split('/').at(-1))));
        throw new Error('Unknown ' + url);
    } };
    for (const method of ['post', 'put', 'patch', 'delete']) api[method] = async (url, body) => {
        calls.push({ method, url, body: body === undefined ? undefined : copy(body) });
        if (server.write) return server.write(url, body, method);
        if (url.includes('/adjust')) { server.inventory[0].quanlity += body.quantity; return null; }
        if (url === '/management/purchase-orders') { const p = { id: 4, supplierId: body.supplierId, status: 'Draft', totalAmount: 12345, items: body.items.map((i) => ({ ...i, totalPrice: 12345 })) }; server.purchases.push(p); return copy(p); }
        if (url.endsWith('/receive')) { server.purchases[0].status = 'Received'; return null; }
        if (url.endsWith('/cancel')) { server.purchases[0].status = 'Cancelled'; return null; }
        if (url === '/management/suppliers') { const s = { ...body, supplierId: 4, isActive: true }; server.suppliers.push(s); return copy(s); }
        const supplier = server.suppliers[0];
        if (method === 'put') Object.assign(supplier, body);
        else supplier.isActive = method === 'delete' ? false : body.isActive;
        return null;
    };
    const create = (newMode = mode) => new WarehouseSession({ api, auth, storage: saved, mode: newMode });
    return { session: create(), create, server, calls, saved };
}
const writes = (b) => b.calls.filter((c) => c.method !== 'get');
const supplierData = { name: ' NCC mới ', phone: ' 000000 ', email: ' new@example.invalid ', address: ' Địa chỉ thử ' };
const purchaseData = { supplierId: 1, items: [{ ingredientId: 1, quantity: '1.25', unitPrice: '1234.56' }] };

test('quyền kho: WarehouseManager/Admin; Waiter/Chef/Cashier không đọc hoặc ghi', async () => {
    for (const role of ['Waiter', 'Chef', 'Cashier']) { const b = setup({ role }); await assert.rejects(b.session.refresh(), (e) => e.status === 403); await assert.rejects(b.session.command('adjust', 1, { quantity: 1, note: 'Test' })); assert.equal(b.calls.length, 0); }
    assert.equal(setup({ role: 'Admin' }).session.hasAccess(), true);
});
test('inventory đọc low-stock từ server, giữ Spoiled dù tồn nhiều và field quanlity', async () => {
    const b = setup({ mode: 'inventory' }); await b.session.refresh();
    assert.equal(b.session.inventory[0].quanlity, 10); assert.equal(b.session.lowStock[0].stockStatus, 'Spoiled'); assert.equal(b.session.lowStock[0].quanlity, 20);
    assert.ok(b.calls.some((c) => c.url === '/management/inventory/low-stock'));
});
test('điều chỉnh số lẻ gửi delta quantity + lý do, GET lại tồn, không sửa trạng thái nguyên liệu', async () => {
    const b = setup({ mode: 'inventory' }); await b.session.refresh();
    await b.session.command('adjust', 1, { quantity: '-1.25', note: ' Kiểm kê hỏng ' }, stockSnapshot(b.session.inventory[0], b.session.ingredients[0]));
    assert.deepEqual(writes(b)[0], { method: 'post', url: '/management/inventory/1/adjust', body: { quantity: -1.25, note: 'Kiểm kê hỏng', createdBy: 8 } });
    assert.equal(b.session.inventory[0].quanlity, 8.75); assert.equal(b.session.ingredients[0].stockStatus, 'Available');
});
test('điều chỉnh 0, sai độ chính xác, quá khoảng, thiếu lý do bị chặn', async () => {
    const b = setup({ mode: 'inventory' });
    for (const quantity of [0, '', 0.0001, -1000001, Infinity]) await assert.rejects(b.session.command('adjust', 1, { quantity, note: 'Test' }), (e) => e.status === 400);
    await assert.rejects(b.session.command('adjust', 1, { quantity: 1, note: ' ' })); assert.equal(writes(b).length, 0);
});
test('chặn giảm quá tồn và dữ liệu tồn/đơn vị đổi trong khi mở form', async () => {
    const b = setup({ mode: 'inventory' }); await b.session.refresh(); const expected = stockSnapshot(b.session.inventory[0], b.session.ingredients[0]);
    await assert.rejects(b.session.command('adjust', 1, { quantity: -11, note: 'Test' }), /vượt tồn/);
    b.server.inventory[0].quanlity = 9;
    await assert.rejects(b.session.command('adjust', 1, { quantity: 1, note: 'Test' }, expected), /vừa đổi/); assert.equal(writes(b).length, 0);
});
test('mất ACK điều chỉnh không suy kết quả từ tồn mới, không tự ghi lần nữa', async () => {
    const b = setup({ mode: 'inventory' }); await b.session.refresh();
    b.server.write = (_, body) => { b.server.inventory[0].quanlity += body.quantity; throw Object.assign(new Error('offline'), { status: 0 }); };
    await assert.rejects(b.session.command('adjust', 1, { quantity: 1.25, note: 'Thử mất ACK' }));
    const restored = b.create(); assert.throws(() => restored.acknowledgeReview());
    assert.equal(await restored.reviewPending(), false); assert.equal(restored.inventory[0].quanlity, 11.25);
    restored.acknowledgeReview(); assert.equal(writes(b).length, 1); assert.equal(restored.pending, null);
});
test('đổi sang màn Nhập hàng vẫn khóa điều chỉnh chưa rõ, buộc quay lại Kho để đối chiếu', async () => {
    const b = setup({ mode: 'inventory' }); b.server.write = () => { throw new Error('offline'); };
    await assert.rejects(b.session.command('adjust', 1, { quantity: 1, note: 'Test' }));
    const other = b.create('purchases'); await assert.rejects(other.reviewPending(), /đúng màn/); await assert.rejects(other.command('createSupplier', null, supplierData)); assert.equal(writes(b).length, 1);
});
test('ACK đã nhận nhưng GET lỗi: review chỉ đọc, không cộng kho lại', async () => {
    const b = setup({ mode: 'inventory' }); b.server.write = () => { b.server.get = () => { throw new Error('GET lỗi'); }; return null; };
    await assert.rejects(b.session.command('adjust', 1, { quantity: 1, note: 'Test' })); assert.equal(b.session.pending.accepted, true);
    b.server.get = null; assert.equal(await b.session.reviewPending(), true); assert.equal(writes(b).length, 1);
});
test('tạo phiếu gửi đơn giá/số lượng/actor, chỉ hiện tổng và thành tiền server', async () => {
    const b = setup(); const p = await b.session.command('createPurchase', null, purchaseData);
    assert.equal(p.totalAmount, 12345); assert.equal(b.session.purchase.totalAmount, 12345); assert.equal(b.session.purchase.items[0].totalPrice, 12345);
    assert.deepEqual(writes(b)[0].body, { supplierId: 1, createdBy: 8, items: [{ ingredientId: 1, quantity: 1.25, unitPrice: 1234.56 }] });
});
test('phiếu nhập chặn nguyên liệu trùng, ngừng dùng, supplier tắt và dòng rỗng', async () => {
    const b = setup();
    for (const data of [{ supplierId: 1, items: [] }, { ...purchaseData, supplierId: 2 }, { supplierId: 1, items: [purchaseData.items[0], purchaseData.items[0]] }, { supplierId: 1, items: [{ ingredientId: 2, quantity: 1, unitPrice: 1 }] }]) await assert.rejects(b.session.command('createPurchase', null, data), (e) => e.status === 400);
    assert.equal(writes(b).length, 0);
});
test('số lượng 3 số lẻ, đơn giá 2 số lẻ; blank không được hiểu là giá 0', async () => {
    const b = setup();
    for (const line of [{ quantity: '1.0001', unitPrice: 1 }, { quantity: 1, unitPrice: -1 }, { quantity: 1, unitPrice: '' }, { quantity: 1, unitPrice: 1.001 }]) await assert.rejects(b.session.command('createPurchase', null, { supplierId: 1, items: [{ ingredientId: 1, ...line }] }));
    assert.equal(writes(b).length, 0);
});
test('lựa chọn vừa ngừng hoạt động được GET lại kiểm tra trước khi tạo phiếu', async () => {
    const b = setup(); await b.session.refresh(); b.server.suppliers[0].isActive = false;
    await assert.rejects(b.session.command('createPurchase', null, purchaseData), /đang hoạt động/); assert.equal(writes(b).length, 0);
});
test('tạo phiếu mất ACK không ghép đoán phiếu giống nhau hay tự POST lại', async () => {
    const b = setup(); b.server.write = () => { throw Object.assign(new Error('Mất ACK'), { status: 0 }); };
    await assert.rejects(b.session.command('createPurchase', null, purchaseData)); const restored = b.create();
    assert.equal(await restored.reviewPending(), false); await assert.rejects(restored.command('createPurchase', null, purchaseData)); assert.equal(writes(b).length, 1);
});
test('nhận phiếu gửi receivedBy, tải trạng thái Received từ server, không duyệt giả', async () => {
    const b = setup(); await b.session.selectPurchase(3);
    await b.session.command('receive', 3, {}, purchaseSnapshot(b.session.purchase));
    assert.deepEqual(writes(b)[0], { method: 'post', url: '/management/purchase-orders/3/receive', body: { receivedBy: 8 } }); assert.equal(b.session.purchase.status, 'Received');
    await assert.rejects(b.session.command('receive', 3)); assert.equal(writes(b).length, 1);
});
test('phiếu đã nhận/đã hủy không nhận hoặc hủy lại qua UI', async () => {
    for (const status of ['Received', 'Cancelled', 'Approved']) {
        const b = setup(); b.server.purchases[0].status = status;
        await assert.rejects(b.session.command('receive', 3)); await assert.rejects(b.session.command('cancelPurchase', 3)); assert.equal(writes(b).length, 0);
    }
});
test('hủy phiếu là command riêng, không gửi item hoặc giả điều chỉnh kho', async () => {
    const b = setup(); await b.session.command('cancelPurchase', 3);
    assert.equal(writes(b)[0].url, '/management/purchase-orders/3/cancel'); assert.deepEqual(writes(b)[0].body, {}); assert.equal(b.session.purchase.status, 'Cancelled');
});
test('phiếu vừa đổi tiền/dòng hàng thì chặn xác nhận dựa trên snapshot cũ', async () => {
    const b = setup(); await b.session.selectPurchase(3); const expected = purchaseSnapshot(b.session.purchase); b.server.purchases[0].totalAmount++;
    await assert.rejects(b.session.command('receive', 3, {}, expected), /vừa thay đổi/); assert.equal(writes(b).length, 0);
});
test('mất ACK nhận hàng được đối chiếu bằng trạng thái Received, không nhập hai lần', async () => {
    const b = setup(); b.server.write = () => { b.server.purchases[0].status = 'Received'; throw new Error('Mất ACK'); };
    await assert.rejects(b.session.command('receive', 3)); const restored = b.create(); assert.equal(await restored.reviewPending(), true); assert.equal(writes(b).length, 1);
});
test('hai lần bấm cùng lúc chỉ gửi một lệnh nhập', async () => {
    const b = setup(); const first = b.session.command('receive', 3); await assert.rejects(b.session.command('receive', 3)); await first; assert.equal(writes(b).length, 1);
});
test('tạo NCC trim và kiểm tra đủ trường, email, độ dài', async () => {
    const b = setup(); const created = await b.session.command('createSupplier', null, supplierData);
    assert.equal(created.name, 'NCC mới'); assert.equal(writes(b)[0].body.address, 'Địa chỉ thử');
    await assert.rejects(b.session.command('createSupplier', null, { ...supplierData, email: 'bad' }));
    await assert.rejects(b.session.command('createSupplier', null, { ...supplierData, name: ' ' })); assert.equal(writes(b).length, 1);
});
test('sửa NCC dùng PUT với snapshot tươi; không ghi đè dữ liệu người khác vừa đổi', async () => {
    const b = setup(); await b.session.refresh(); const expected = supplierSnapshot(b.session.suppliers[0]); b.server.suppliers[0].phone = '111';
    await assert.rejects(b.session.command('updateSupplier', 1, supplierData, expected), /vừa thay đổi/); assert.equal(writes(b).length, 0);
    await b.session.command('updateSupplier', 1, supplierData); assert.equal(writes(b)[0].method, 'put');
});
test('ngừng NCC là soft delete và khôi phục qua PATCH active, phiếu cũ vẫn còn', async () => {
    const b = setup(); await b.session.command('deleteSupplier', 1); assert.equal(b.session.suppliers[0].isActive, false); assert.equal(b.session.purchases.length, 1);
    await b.session.command('supplierActive', 1, { isActive: true }); assert.equal(b.session.suppliers[0].isActive, true);
    assert.equal(writes(b)[0].method, 'delete'); assert.equal(writes(b)[1].url, '/management/suppliers/1/active');
});
test('400/409 trả nguyên thông báo nghiệp vụ và không giả trạng thái Received', async () => {
    const b = setup(); b.server.write = () => { throw Object.assign(new Error('Cancelled purchase order cannot be received'), { status: 409 }); };
    await assert.rejects(b.session.command('receive', 3), /Cancelled purchase order/); assert.equal(b.session.pending, null); assert.equal(b.session.purchase.status, 'Draft');
});
test('lọc lịch sử bắt buộc referenceType cho referenceId, ManualAdjustment không nhận mã chứng từ', async () => {
    const b = setup({ mode: 'inventory' });
    await assert.rejects(b.session.loadLedger({ referenceId: 7 }), /Chọn nguồn/);
    await assert.rejects(b.session.loadLedger({ referenceId: 7, referenceType: 'ManualAdjustment' })); assert.equal(b.calls.length, 0);
    await b.session.loadLedger({ referenceId: 7, referenceType: 'PurchaseOrder', ingredientId: 1, type: 'Purchase' }, 2);
    assert.ok(b.calls[0].url.includes('referenceType=PurchaseOrder')); assert.ok(b.calls[0].url.includes('page=2')); assert.equal(b.session.ledger.items[0].quanlity, 1.25);
});
test('ngày lịch sử đủ ngày Việt Nam chuyển sang UTC, ngày sai bị chặn', () => {
    assert.equal(dateBound('2026-10-31'), '2026-10-30T17:00:00.000Z'); assert.equal(dateBound('2026-10-31', true), '2026-10-31T16:59:59.999Z'); assert.throws(() => dateBound('2026-02-30'));
});
test('yêu cầu user khác không khôi phục, user cũ reload phải review lại trước khi ACK thủ công', async () => {
    const saved = storage(); saved.setItem('restaurant.warehousePending', JSON.stringify({ userId: 999, kind: 'adjust' })); assert.equal(setup({ saved }).session.pending, null);
    saved.setItem('restaurant.warehousePending', JSON.stringify({ userId: 8, kind: 'adjust', id: 1, reviewed: true, accepted: false, body: { quantity: 1, note: 'Test' } }));
    const b = setup({ saved, mode: 'inventory' }); assert.equal(b.session.pending.reviewed, false); assert.throws(() => b.session.acknowledgeReview());
});
