const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const context = vm.createContext({ window: {} });
vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common/kitchen-session.js'), 'utf8'), context);
const { KitchenSession, snapshot } = context.window.restaurantKitchen;
const copy = (x) => JSON.parse(JSON.stringify(x));
function storage() { const m = new Map(); return { getItem: (k) => m.get(k) || null, setItem: (k,v) => m.set(k,v), removeItem: (k) => m.delete(k) }; }
function setup({ role = 'Chef', saved = storage() } = {}) {
    const requests = [];
    const server = {
        tickets: [{ kitchenOrderId: 10, orderId: 42, status: 'Pending', tableNumber: 'TEST', waiterName: 'Phục vụ', priority: 0 }],
        items: [{ id: 1, orderItemId: 11, kitchenOrderId: 10, status: 'Pending', quantity: 3, foodName: 'Món thử', sizeName: 'M' }, { id: 2, orderItemId: 12, kitchenOrderId: 10, status: 'Pending', quantity: 2, foodName: 'Món khác', sizeName: 'M' }],
        order: { orderId: 42, status: 'SentToKitchen', totalAmount: 87913, items: [{ id: 11, status: 'Pending', cancelReason: null }, { id: 12, status: 'Pending', cancelReason: null }] },
        get: null, write: null
    };
    const auth = { getUser: () => ({ userId: 8 }), hasRole: (...roles) => roles.includes(role) };
    const api = {
        get: async (url) => {
            requests.push({ method: 'get', url });
            if (server.get) return copy(await server.get(url));
            if (url === '/management/kitchen/orders?status=open') return copy(server.tickets.filter((t) => !['Cancelled','Completed'].includes(t.status)));
            if (url === '/management/kitchen/orders') return copy(server.tickets);
            if (url.includes('/items')) return copy(server.items);
            return copy(server.order);
        }
    };
    for (const method of ['post','patch']) api[method] = async (url,body) => {
        requests.push({ method,url,body:copy(body) });
        if (server.write) return server.write(url,body);
        return null;
    };
    const create = () => new KitchenSession({ api,auth,storage:saved });
    return { session:create(), create, server, requests, saved };
}
const writes = (b) => b.requests.filter((r) => r.method !== 'get');

test('poll snapshot phiếu mở, dùng order details lấy lý do, không gọi lịch sử Chef bị cấm', async () => {
    const b=setup(); b.server.order.items[0].cancelReason='Thiếu thực tế';
    await b.session.refresh();
    assert.equal(b.session.tickets[0].items[0].cancelReason,'Thiếu thực tế');
    assert.equal(b.requests[0].url,'/management/kitchen/orders?status=open');
    assert.equal(b.requests.some((r) => r.url.includes('history') || r.url.includes('restaurant-tables/') || r.url.includes('since=')),false);
});
test('nhiều vòng cùng đơn chỉ đọc chi tiết đơn một lần trong snapshot', async () => {
    const b=setup(); b.server.tickets.push({...b.server.tickets[0],kitchenOrderId:20});
    await b.session.refresh();
    assert.equal(b.requests.filter((r) => r.url==='/orders/42').length,1);
});
test('nhận đúng một món, không nhận cả phiếu hay đổi món còn lại cục bộ', async () => {
    const b=setup(); await b.session.refresh();
    b.server.write=() => { b.server.items[0].status='Preparing'; b.server.tickets[0].status='Preparing'; };
    await b.session.perform('receive',10,1,{},snapshot(b.session.tickets[0].items[0]));
    assert.equal(writes(b)[0].url,'/management/kitchen/items/1/status');
    assert.deepEqual(writes(b)[0].body,{status:'Preparing',changedBy:8});
    assert.equal(b.session.tickets[0].items[1].status,'Pending');
});
test('báo đã làm dùng prepared-quantity, nhận dòng tách và số tiền nguyên từ server', async () => {
    const b=setup(); b.server.items[0].status='Preparing'; await b.session.refresh();
    b.server.write=() => { b.server.items[0].quantity=2; b.server.items.push({...b.server.items[0],id:3,orderItemId:13,quantity:1,status:'Completed'}); };
    await b.session.perform('prepared',10,1,{preparedQuantity:1,reason:'Mới làm được một phần'});
    assert.equal(writes(b)[0].url,'/management/kitchen/items/1/prepared-quantity');
    assert.equal(writes(b)[0].body.preparedQuantity,1);
    assert.equal(b.session.tickets[0].items[0].quantity,2);
    assert.equal(b.session.tickets[0].items[2].status,'Completed');
    assert.equal(b.session.tickets[0].order.totalAmount,87913);
});
test('chưa nhận món thì chưa dùng nút báo đã làm, trạng thái server là nguồn chuẩn', async () => {
    const b=setup(); await assert.rejects(b.session.perform('prepared',10,1,{preparedQuantity:1}), (e)=>e.status===409);
    assert.equal(writes(b).length,0);
});
test('thiếu/hỏng cho phép 0 phần, gửi mã hao hụt và cờ ngừng bán kèm lý do', async () => {
    const b=setup(); await b.session.perform('shortage',10,1,{preparedQuantity:0,reasonCode:'PhysicalShortage',reason:'Lô nguyên liệu hỏng',deactivateDish:true});
    const request=writes(b)[0];
    assert.equal(request.url,'/management/kitchen/items/1/shortage');
    assert.equal(request.method,'post');
    assert.equal(request.body.preparedQuantity,0);
    assert.equal(request.body.reasonCode,'PhysicalShortage');
    assert.equal(request.body.deactivateDish,true);
    assert.equal(request.body.inactiveReason,'Lô nguyên liệu hỏng');
    assert.equal(request.body.ingredientId,undefined);
});
test('giới hạn số nguyên và reason code của shortage được kiểm tra trước khi ghi', async () => {
    for (const data of [{preparedQuantity:3,reasonCode:'PhysicalShortage'}, {preparedQuantity:-1,reasonCode:'OutOfStock'}, {preparedQuantity:0.5,reasonCode:'OutOfStock'}, {preparedQuantity:1,reasonCode:'CustomerChange'}]) {
        const b=setup(); await assert.rejects(b.session.perform('shortage',10,1,{...data,reason:'Kiểm thử'}),(e)=>e.status===400);
        assert.equal(writes(b).length,0);
    }
});
test('hủy/mở lại/thiếu yêu cầu lý do, lý do dài quá 500 bị chặn', async () => {
    for (const action of ['cancel','shortage','reopen']) {
        const b=setup(); if(action==='reopen') b.server.items[0].status='Completed';
        await assert.rejects(b.session.perform(action,10,1,{preparedQuantity:0,reasonCode:'KitchenError',reason:'   '}),(e)=>e.status===400);
        assert.equal(writes(b).length,0);
    }
    const b=setup(); await assert.rejects(b.session.perform('cancel',10,1,{reason:'x'.repeat(501),reasonCode:'KitchenError'}),(e)=>e.status===400);
});
test('mở lại dùng POST reopen cho cả Ready và Completed, không PATCH lùi trạng thái', async () => {
    for(const status of ['Ready','Completed']) {
        const b=setup(); b.server.items[0].status=status; b.server.tickets[0].status='Ready';
        b.server.write=()=>{b.server.items[0].status='Preparing';b.server.tickets[0].status='Preparing';};
        await b.session.perform('reopen',10,1,{reason:'Bấm nhầm hoàn thành'});
        assert.equal(writes(b)[0].url,'/management/kitchen/items/1/reopen');
        assert.equal(writes(b)[0].method,'post');
        assert.equal(b.session.tickets[0].items[0].status,'Preparing');
    }
});
test('phiếu Completed/Cancelled hoặc đơn Paid/Cancelled không được mở lại', async () => {
    for(const type of ['ticket','order']) for(const status of type==='ticket'?['Completed','Cancelled']:['Paid','Cancelled']) {
        const b=setup();b.server.items[0].status='Completed';
        if(type==='ticket')b.server.tickets[0].status=status;else b.server.order.status=status;
        await assert.rejects(b.session.perform('reopen',10,1,{reason:'Mở lại'}),(e)=>e.status===409);
        assert.equal(writes(b).length,0);
    }
});
test('Chef không hủy Ready, Admin được hủy Ready; Completed chỉ có luồng mở lại', async () => {
    const chef=setup();chef.server.items[0].status='Ready';
    await assert.rejects(chef.session.perform('cancel',10,1,{reason:'Hủy',reasonCode:'KitchenError'}),(e)=>e.status===409);
    const admin=setup({role:'Admin'});admin.server.items[0].status='Ready';
    await admin.session.perform('cancel',10,1,{reason:'Xử lý ngoại lệ',reasonCode:'KitchenError'});
    assert.equal(writes(admin)[0].body.status,'Cancelled');
});
test('Waiter không gọi/ghi API Bếp', async () => {
    const b=setup({role:'Waiter'});
    await assert.rejects(b.session.refresh(),(e)=>e.status===403);
    await assert.rejects(b.session.perform('receive',10,1),(e)=>e.status===403);
    assert.equal(b.requests.length,0);
});
test('số phần đổi trong lúc mở hộp thoại thì chặn lệnh dựa trên snapshot cũ', async () => {
    const b=setup();b.server.items[0].status='Preparing';await b.session.refresh();
    const expected=snapshot(b.session.tickets[0].items[0]);b.server.items[0].quantity=2;
    await assert.rejects(b.session.perform('prepared',10,1,{preparedQuantity:1},expected),/vừa thay đổi/);
    assert.equal(writes(b).length,0);
});
test('403/409 không tự đổi trạng thái hay giữ lệnh để ghi lại', async () => {
    for(const status of [403,409]) {
        const b=setup();b.server.write=()=>{throw Object.assign(new Error('Dữ liệu đã đổi'),{status});};
        await assert.rejects(b.session.perform('receive',10,1),/Dữ liệu đã đổi/);
        assert.equal(b.session.tickets[0].items[0].status,'Pending');
        assert.equal(b.session.pending,null);
    }
});
test('mất phản hồi prepared: reload chỉ đối chiếu, tuyệt đối không gửi phần đã làm lần nữa', async () => {
    const b=setup();b.server.items[0].status='Preparing';
    b.server.write=()=>{b.server.items[0].quantity=2;throw Object.assign(new Error('network'),{status:0});};
    await assert.rejects(b.session.perform('prepared',10,1,{preparedQuantity:1}),/network/);
    const resumed=b.create();
    await assert.rejects(resumed.perform('receive',10,2),/đối chiếu/);
    assert.throws(()=>resumed.acknowledgeReview(),/Tải dữ liệu/);
    await resumed.reviewPending();
    assert.equal(writes(b).length,1);
    assert.equal(resumed.pending.reviewed,true);
    assert.equal(resumed.tickets[0].items[0].quantity,2);
    resumed.acknowledgeReview();assert.equal(resumed.pending,null);
});
test('ACK nhận rồi GET lỗi: tải lại kết quả không gọi POST/PATCH thêm lần nào', async () => {
    const b=setup();let fail=false;
    b.server.write=()=>{b.server.items[0].status='Preparing';fail=true;};
    b.server.get=(url)=>{
        if(fail)throw Object.assign(new Error('network'),{status:0});
        return url.includes('/items')?b.server.items:url.startsWith('/orders/')?b.server.order:b.server.tickets;
    };
    await assert.rejects(b.session.perform('receive',10,1),/network/);
    assert.equal(b.session.pending.accepted,true);
    fail=false;const resumed=b.create();await resumed.reviewPending();
    assert.equal(writes(b).length,1);assert.equal(resumed.pending,null);
});
test('bấm hai món liên tiếp khi lệnh trước còn chạy không tạo hai yêu cầu ghi', async () => {
    const b=setup();let finish,started;
    const ready=new Promise((resolve)=>{started=resolve;});
    b.server.write=()=>new Promise((resolve)=>{finish=resolve;started();});
    const first=b.session.perform('receive',10,1);await ready;
    await assert.rejects(b.session.perform('receive',10,2),/chờ hoặc đối chiếu/);
    finish();await first;assert.equal(writes(b).length,1);
});
test('phiếu vừa hủy rời danh sách mở vẫn đọc được lý do trong kết quả đối chiếu', async () => {
    const b=setup();
    b.server.write=()=>{b.server.tickets[0].status='Cancelled';b.server.order.status='Cancelled';b.server.items.forEach((i)=>{i.status='Cancelled';});b.server.order.items[0].cancelReason='Nguyên liệu hỏng';};
    await b.session.perform('shortage',10,1,{preparedQuantity:0,reasonCode:'PhysicalShortage',reason:'Nguyên liệu hỏng'});
    assert.equal(b.session.tickets.length,0);
    assert.equal(b.session.receipt.status,'Cancelled');
    assert.equal(b.session.receipt.items[0].cancelReason,'Nguyên liệu hỏng');
});
