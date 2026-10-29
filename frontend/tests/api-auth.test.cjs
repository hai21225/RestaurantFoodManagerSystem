const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

const profile = { userId: 7, userName: 'integration', fullName: 'Kiểm thử', roles: ['Admin'] };
const json = (body, status = 200, headers = {}) => new Response(JSON.stringify(body), {
    status, headers: { 'content-type': 'application/json', ...headers }
});
function storage(initial = {}) {
    const items = new Map(Object.entries(initial));
    return { getItem: (k) => items.get(k) ?? null, setItem: (k, v) => items.set(k, String(v)), removeItem: (k) => items.delete(k) };
}

async function browser({ url = 'http://localhost:5500/pages/tables.html', token = 'test.token.signature' } = {}) {
    const redirects = [], requests = [], downloads = [], revoked = [];
    const initial = token ? { 'restaurant.token': token, 'restaurant.currentUser': JSON.stringify(profile) } : {};
    const localStorage = storage(initial), sessionStorage = storage();
    const events = new EventTarget();
    let handler = async () => json(profile);
    class BrowserUrl extends URL {
        static createObjectURL() { return 'blob:test-download'; }
        static revokeObjectURL(url) { revoked.push(url); }
    }
    const window = {
        localStorage, sessionStorage, location: Object.assign(new URL(url), { replace: (value) => redirects.push(value) }),
        addEventListener: events.addEventListener.bind(events), dispatchEvent: events.dispatchEvent.bind(events),
        atob, setTimeout: (fn) => fn()
    };
    const document = {
        currentScript: { src: 'http://localhost:5500/js/common/auth.js' }, body: { appendChild() {} },
        createElement: () => ({ click() { downloads.push({ name: this.download, url: this.href }); }, remove() {} })
    };
    const context = vm.createContext({ window, document, URL: BrowserUrl, Headers, Response, CustomEvent,
        fetch: async (url, options) => { requests.push({ url, ...options }); return handler(url, options); }
    });
    for (const file of ['auth.js', 'api.js']) {
        vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common', file), 'utf8'), context, { filename: file });
    }
    await window.restaurantAuth.ready;
    requests.length = 0;
    return { window, localStorage, sessionStorage, redirects, requests, downloads, revoked,
        api: window.restaurantApi, auth: window.restaurantAuth, respond: (fn) => { handler = fn; } };
}

test('POST gửi JWT, JSON và Idempotency-Key do caller cung cấp', async () => {
    const b = await browser();
    b.respond(async () => json({ orderId: 11 }, 201));
    assert.equal((await b.api.post('/orders', { tableId: 2 }, { headers: { 'Idempotency-Key': 'same-operation' } })).orderId, 11);
    assert.equal(b.requests[0].headers.get('Authorization'), 'Bearer test.token.signature');
    assert.equal(b.requests[0].headers.get('Idempotency-Key'), 'same-operation');
    assert.equal(b.requests[0].headers.get('Content-Type'), 'application/json');
    assert.equal(b.requests[0].body, '{"tableId":2}');
});

test('PATCH không body và DELETE nhận 204 mà không parse JSON', async () => {
    const b = await browser();
    b.respond(async () => new Response(null, { status: 204 }));
    assert.equal(await b.api.patch('/orders/1/promotion/2'), null);
    assert.equal(b.requests[0].body, undefined);
    assert.equal(await b.api.delete('/foods/1'), null);
    assert.equal(b.requests[1].method, 'DELETE');
});

test('401 xoá phiên, dữ liệu đơn cũ và quay về login kèm trang cần mở', async () => {
    const b = await browser();
    b.localStorage.setItem('restaurant.selectedTable', '2');
    b.localStorage.setItem('restaurant.lastOrder', 'old-order');
    b.sessionStorage.setItem('restaurant.posPending', 'previous-session-request');
    b.respond(async () => new Response('invalid-json', { status: 401, headers: { 'content-type': 'application/json' } }));
    await assert.rejects(b.api.get('/menu'), (e) => e.status === 401);
    assert.equal(b.auth.getToken(), null);
    assert.equal(b.sessionStorage.getItem('restaurant.currentUser'), null);
    assert.equal(b.localStorage.getItem('restaurant.selectedTable'), null);
    assert.equal(b.localStorage.getItem('restaurant.lastOrder'), null);
    assert.equal(b.sessionStorage.getItem('restaurant.posPending'), null);
    assert.equal(new URL(b.redirects[0]).searchParams.get('returnUrl'), b.window.location.href);
});

test('403 báo thiếu quyền và giữ phiên để người dùng tiếp tục việc được phép', async () => {
    const b = await browser();
    b.respond(async () => new Response(null, { status: 403 }));
    await assert.rejects(b.api.get('/users'), (e) => e.status === 403 && /quyền/.test(e.message));
    assert.equal(b.auth.getToken(), 'test.token.signature');
    assert.equal(b.redirects.length, 0);
});

test('sai mật khẩu trả 401 tại login: hiện message, không vòng lặp chuyển trang', async () => {
    const b = await browser({ url: 'http://localhost:5500/pages/login.html', token: null });
    b.respond(async () => json({ message: 'Sai tài khoản hoặc mật khẩu' }, 401));
    await assert.rejects(b.api.post('/auth/login', { password: 'wrong' }), /Sai tài khoản/);
    assert.equal(b.requests[0].headers.has('Authorization'), false);
    assert.equal(b.redirects.length, 0);
});

test('setup/status dùng được trước đăng nhập', async () => {
    const b = await browser({ url: 'http://localhost:5500/pages/setup.html', token: null });
    b.respond(async () => json({ requiresSetup: true }));
    assert.equal((await b.api.get('/setup/status')).requiresSetup, true);
    assert.equal(b.requests[0].headers.has('Authorization'), false);
});

test('lưu userId tương thích trang cũ, role thật, đăng xuất xoá mọi phiên', async () => {
    const b = await browser();
    b.auth.setSession({ ...profile, token: 'new-token', roles: ['Chef'] });
    assert.equal(JSON.parse(b.sessionStorage.getItem('restaurant.currentUser')).userId, 7);
    assert.equal(JSON.parse(b.localStorage.getItem('restaurant.currentUser')).token, undefined);
    b.localStorage.setItem('restaurant.currentRole', 'admin');
    assert.equal(b.auth.hasRole('Admin'), false);
    assert.equal(b.auth.hasRole('Chef'), true);
    b.auth.logout();
    assert.equal(b.auth.getToken(), null);
    assert.equal(b.localStorage.getItem('restaurant.currentRole'), null);
    assert.equal(b.redirects.length, 1);
});

test('login không có token không được coi là thành công', async () => {
    const b = await browser({ url: 'http://localhost:5500/pages/login.html', token: null });
    assert.throws(() => b.auth.setSession(profile), /token/);
    assert.equal(b.auth.getToken(), null);
});

test('download dùng Bearer và Blob, tên Unicode và thu hồi object URL', async () => {
    const b = await browser();
    b.respond(async () => new Response('\ufeffNgày,Doanh thu\n29/10,0', {
        headers: { 'content-type': 'text/csv', 'content-disposition': "attachment; filename*=UTF-8''doanh-thu-%C4%91.csv" }
    }));
    assert.equal(await b.api.download('/reports/revenue/export.csv'), 'doanh-thu-đ.csv');
    assert.equal(b.requests[0].headers.has('Authorization'), true);
    assert.equal(b.downloads[0].name, 'doanh-thu-đ.csv');
    assert.equal(b.revoked[0], 'blob:test-download');
});

test('download 403 không tạo file lỗi giả CSV', async () => {
    const b = await browser();
    b.respond(async () => json({ message: 'Không được xuất báo cáo' }, 403));
    await assert.rejects(b.api.download('/reports/revenue/export.csv'), /Không được xuất/);
    assert.equal(b.downloads.length, 0);
    assert.ok(b.auth.getToken());
});

test('409 giữ message nghiệp vụ từ backend', async () => {
    const b = await browser();
    b.respond(async () => json({ message: 'Bàn đã có đơn đang mở' }, 409));
    await assert.rejects(b.api.post('/orders', {}), (e) => e.status === 409 && e.message === 'Bàn đã có đơn đang mở');
});

test('validation problem+json được hiển thị, 500 không lộ stack trace', async () => {
    const b = await browser();
    b.respond(async () => new Response('{"errors":{"Name":["Tên bắt buộc"]}}', { status: 400, headers: { 'content-type': 'application/problem+json' } }));
    await assert.rejects(b.api.post('/foods', {}), /Tên bắt buộc/);
    b.respond(async () => json({ message: 'SqlException at internal.cs line 1' }, 500));
    await assert.rejects(b.api.get('/menu'), (e) => e.status === 500 && !e.message.includes('SqlException'));
});

test('lỗi mạng giữ phiên; abort không bị đổi thành lỗi CORS', async () => {
    const b = await browser();
    b.respond(async () => { throw new TypeError('network failed'); });
    await assert.rejects(b.api.get('/menu'), (e) => e.status === 0);
    assert.ok(b.auth.getToken());
    b.respond(async () => { throw new DOMException('cancelled', 'AbortError'); });
    await assert.rejects(b.api.get('/menu'), (e) => e.name === 'AbortError');
    assert.equal(b.redirects.length, 0);
});

test('401 của request cũ không xoá phiên mới vừa đăng nhập', async () => {
    const b = await browser();
    let finish;
    b.respond(() => new Promise((resolve) => { finish = resolve; }));
    const pending = b.api.get('/menu');
    b.auth.setSession({ ...profile, token: 'new-session' });
    finish(new Response(null, { status: 401 }));
    await assert.rejects(pending, (e) => e.status === 401);
    assert.equal(b.auth.getToken(), 'new-session');
    assert.equal(b.redirects.length, 0);
});

test('URL quay lại chỉ thuộc ứng dụng và không quay vào login/setup', async () => {
    for (const target of ['https://outside.example/x.html', 'javascript:alert(1)', '/pages/login.html', '/pages/setup.html']) {
        const b = await browser({ url: 'http://localhost:5500/pages/login.html?returnUrl=' + encodeURIComponent(target), token: null });
        assert.equal(b.auth.getReturnUrl(), 'http://localhost:5500/index.html');
    }
    const b = await browser({ url: 'http://localhost:5500/pages/login.html?returnUrl=%2Fpages%2Fpurchases.html', token: null });
    assert.equal(b.auth.getReturnUrl(), 'http://localhost:5500/pages/purchases.html');
});

test('hết hạn token hoặc mở trang khi chưa đăng nhập đều về login', async () => {
    const expired = 'header.' + Buffer.from(JSON.stringify({ exp: 1 })).toString('base64url') + '.sig';
    for (const token of [null, expired]) {
        const b = await browser({ token });
        assert.equal(b.auth.getToken(), null);
        assert.equal(b.redirects.length, 1);
        await assert.rejects(b.api.post('/orders', {}), (e) => e.status === 401);
        assert.equal(b.requests.length, 0);
    }
});

test('đăng xuất ở tab khác dọn sessionStorage của tab hiện tại', async () => {
    const b = await browser();
    b.localStorage.removeItem('restaurant.token');
    const event = new Event('storage');
    event.key = 'restaurant.token';
    b.window.dispatchEvent(event);
    assert.equal(b.sessionStorage.getItem('restaurant.currentUser'), null);
    assert.equal(b.redirects.length, 1);
});

test('không gửi token tới URL ngoài API', async () => {
    const b = await browser();
    await assert.rejects(b.api.get('//outside.example'), /Đường dẫn/);
    await assert.rejects(b.api.get('https://outside.example'), /Đường dẫn/);
    assert.equal(b.requests.length, 0);
});
