const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), vm = require('node:vm'), path = require('node:path');
const context = vm.createContext({ window: {}, URLSearchParams });
vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common/reporting-session.js'), 'utf8'), context);
const { ReportingSession, vnDate } = context.window.restaurantReporting;
const copy = (x) => JSON.parse(JSON.stringify(x));
function setup(role = 'Admin') {
    const calls = [], auth = { role, hasRole: (...roles) => roles.includes(auth.role) };
    const server = {
        summary: { revenueToday: 87913, paidOrderCountToday: 3, openOrderCount: 18, lowStockCount: 0,
            tables: { total: 4, available: 1, occupied: 2, maintenance: 1 }, kitchen: { open: 3, pending: 1, preparing: 1, ready: 1 }, recentOrders: [{ orderId: 42, tableId: 3, status: 'Ready', totalAmount: 57891 }] },
        tables: [{ tableId: 3, tableNumber: 'B03', capacity: 4, isActive: true, status: 'Occupied' }],
        report: { period: 'day', timeZone: 'Asia/Ho_Chi_Minh', from: '2026-10-30T17:00:00Z', to: '2026-10-31T17:00:00Z', totalRevenue: 87913, orderCount: 3, averagePerOrder: 12345.67,
            groups: [{ label: '23:00', from: '2026-10-31T16:00:00Z', to: '2026-10-31T17:00:00Z', revenue: 5000, orderCount: 1 }] }, get: null, download: null
    };
    const api = { get: async (url) => {
        calls.push({ method: 'get', url }); if (server.get) return server.get(url);
        if (url === '/reports/dashboard') return copy(server.summary);
        if (url === '/restaurant-tables') return copy(server.tables);
        const query = new URL('http://test' + url).searchParams; return { ...copy(server.report), period: query.get('period') };
    }, download: async (url, filename) => { calls.push({ method: 'download', url, filename }); if (server.download) return server.download(url, filename); return filename; } };
    return { session: new ReportingSession({ api, auth }), auth, api, server, calls };
}
const downloads = (b) => b.calls.filter((c) => c.method === 'download');

test('Admin gọi đúng dashboard + danh sách bàn thuần đọc, giữ nguyên tổng từ server', async () => {
    const b = setup(); await b.session.loadDashboard();
    assert.equal(b.session.summary.revenueToday, 87913); assert.equal(b.session.summary.openOrderCount, 18);
    assert.equal(b.session.summary.lowStockCount, 0); assert.equal(b.session.tables[0].tableNumber, 'B03');
    assert.deepEqual(b.calls.map((c) => c.url), ['/reports/dashboard', '/restaurant-tables']);
});
test('Waiter/Chef/Cashier/WarehouseManager không đọc tổng quan, báo cáo hoặc CSV', async () => {
    for (const role of ['Waiter', 'Chef', 'Cashier', 'WarehouseManager']) {
        const b = setup(role); await assert.rejects(b.session.loadDashboard(), (e) => e.status === 403);
        await assert.rejects(b.session.loadRevenue(), (e) => e.status === 403); await assert.rejects(b.session.exportCsv(), (e) => e.status === 403); assert.equal(b.calls.length, 0);
    }
});
test('lỗi sơ đồ bàn không biến doanh thu/tổng quan thành số 0 hoặc mất số liệu hợp lệ', async () => {
    const b = setup(); b.server.get = (url) => { if (url === '/restaurant-tables') throw new Error('Không tải được bàn'); return copy(b.server.summary); };
    await b.session.loadDashboard(); assert.equal(b.session.summary.revenueToday, 87913); assert.equal(b.session.tableError, 'Không tải được bàn'); assert.equal(b.session.tables.length, 0);
});
test('lỗi dashboard làm trống snapshot cũ, không hiển thị doanh thu cũ như mới', async () => {
    const b = setup(); await b.session.loadDashboard(); b.server.get = () => { throw Object.assign(new Error('Máy chủ lỗi'), { status: 500 }); };
    await assert.rejects(b.session.loadDashboard(), /Máy chủ lỗi/); assert.equal(b.session.summary, null); assert.equal(b.session.dashboardLoading, false);
});
test('payload tổng quan thiếu số liệu bị chặn, không fallback sang số 0 giả', async () => {
    const b = setup(); delete b.server.summary.revenueToday;
    await assert.rejects(b.session.loadDashboard(), (e) => e.status === 502); assert.equal(b.session.summary, null);
});
test('GET dashboard cũ hoàn tất muộn không ghi đè lần làm mới mới hơn', async () => {
    const b = setup(); let resolveFirst, count = 0;
    b.server.get = (url) => url === '/restaurant-tables' ? [] : ++count === 1 ? new Promise((resolve) => { resolveFirst = resolve; }) : { ...copy(b.server.summary), revenueToday: 99000 };
    const first = b.session.loadDashboard(); await b.session.loadDashboard(); resolveFirst(copy(b.server.summary)); await first;
    assert.equal(b.session.summary.revenueToday, 99000); assert.equal(b.session.dashboardLoading, false);
});
test('ngày/tuần/tháng gửi nguyên ngày Việt Nam, không dùng from/to hoặc tự đổi kỳ ở client', async () => {
    const b = setup();
    for (const period of ['day', 'week', 'month']) { await b.session.loadRevenue({ period, date: '2026-10-31' }); assert.ok(b.calls.at(-1).url.endsWith(`period=${period}&date=2026-10-31`)); }
});
test('giữ totalRevenue, averagePerOrder và nhóm từ server; không cộng nhóm hay chia lại trung bình', async () => {
    const b = setup(); await b.session.loadRevenue({ period: 'day', date: '2026-10-31' });
    assert.equal(b.session.report.totalRevenue, 87913); assert.equal(b.session.report.averagePerOrder, 12345.67); assert.equal(b.session.report.groups[0].revenue, 5000);
});
test('để trống ngày dùng server mặc định rồi cố định ngày đã tải cho CSV qua nửa đêm', async () => {
    const b = setup(); await b.session.loadRevenue(); assert.equal(b.calls[0].url, '/reports/revenue?period=day'); assert.equal(b.session.applied.date, '2026-10-31');
    b.server.report.from = '2026-10-31T17:00:00Z'; await b.session.exportCsv();
    assert.equal(downloads(b)[0].url, '/reports/revenue/export.csv?period=day&date=2026-10-31');
});
test('tuần mặc định giữ mốc server, tên file dùng ngày bắt đầu kỳ theo VN', async () => {
    const b = setup(); b.server.report.from = '2026-10-25T17:00:00Z'; b.server.report.to = '2026-11-01T17:00:00Z';
    await b.session.loadRevenue({ period: 'week' }); assert.equal(b.session.applied.date, '2026-10-26');
    assert.equal(await b.session.exportCsv(), 'doanh-thu-week-2026-10-26.csv');
});
test('ngày sai/không tồn tại và period ngoài allowlist không gửi API', async () => {
    const b = setup();
    for (const input of [{ period: 'year' }, { date: '2026-02-30' }, { date: '31/10/2026' }, { date: '0000-01-01' }]) await assert.rejects(b.session.loadRevenue(input), (e) => e.status === 400);
    assert.equal(b.calls.length, 0);
});
test('VN đổi ngày đúng ở biên 17:00 UTC, kể cả timestamp backend không có Z', () => {
    assert.equal(vnDate('2026-10-31T16:59:59Z'), '2026-10-31'); assert.equal(vnDate('2026-10-31T17:00:00Z'), '2026-11-01');
    assert.equal(vnDate('2026-10-31T17:00:00'), '2026-11-01'); assert.throws(() => vnDate('bad'));
});
test('bộ lọc vừa nhập khác kỳ đã tải thì matches=false, chưa được xuất qua nút UI', async () => {
    const b = setup(); await b.session.loadRevenue({ period: 'day', date: '2026-10-31' });
    assert.equal(b.session.matches({ period: 'day', date: '2026-10-31' }), true);
    assert.equal(b.session.matches({ period: 'month', date: '2026-10-31' }), false); assert.equal(b.session.matches({ period: 'day', date: '' }), false);
});
test('trước khi tải hoặc sau khi tải lỗi không xuất CSV/không lưu JSON lỗi thành CSV', async () => {
    const b = setup(); await assert.rejects(b.session.exportCsv(), (e) => e.status === 409);
    await b.session.loadRevenue(); b.server.get = () => { throw Object.assign(new Error('Không đủ quyền'), { status: 403 }); };
    await assert.rejects(b.session.loadRevenue()); await assert.rejects(b.session.exportCsv()); assert.equal(downloads(b).length, 0); assert.equal(b.session.report, null);
});
test('CSV đi qua api.download với đúng kỳ/ngày đã áp dụng và tên file rõ ràng', async () => {
    const b = setup(); b.server.report.from = '2026-09-30T17:00:00Z';
    await b.session.loadRevenue({ period: 'month', date: '2026-10-31' }); await b.session.exportCsv();
    assert.deepEqual(downloads(b)[0], { method: 'download', url: '/reports/revenue/export.csv?period=month&date=2026-10-31', filename: 'doanh-thu-month-2026-10-01.csv' });
});
test('bấm tải CSV hai lần trong khi đang tải chỉ gửi một yêu cầu', async () => {
    const b = setup(); await b.session.loadRevenue(); let finish; b.server.download = () => new Promise((resolve) => { finish = resolve; });
    const first = b.session.exportCsv(); await assert.rejects(b.session.exportCsv(), (e) => e.status === 409); finish('file.csv'); await first; assert.equal(downloads(b).length, 1);
});
test('đang xuất CSV thì không đổi kỳ trong model; giữ report đang xem', async () => {
    const b = setup(); await b.session.loadRevenue(); let finish; b.server.download = () => new Promise((resolve) => { finish = resolve; });
    const pending = b.session.exportCsv(); await assert.rejects(b.session.loadRevenue({ period: 'week' }), (e) => e.status === 409); assert.equal(b.session.report.totalRevenue, 87913); finish('ok.csv'); await pending;
});
test('lỗi xuất trả nguyên message và mở khóa thử lại, không báo tải thành công giả', async () => {
    const b = setup(); await b.session.loadRevenue(); b.server.download = () => { throw Object.assign(new Error('Không đủ quyền tải CSV'), { status: 403 }); };
    await assert.rejects(b.session.exportCsv(), /Không đủ quyền tải CSV/); assert.equal(b.session.exporting, false);
});
test('response báo cáo cũ về muộn không ghi đè bộ lọc/số liệu hiện tại', async () => {
    const b = setup(); let finish;
    b.server.get = (url) => url.includes('date=2026-10-30') ? new Promise((resolve) => { finish = resolve; }) : { ...copy(b.server.report), totalRevenue: 99000 };
    const first = b.session.loadRevenue({ date: '2026-10-30' }); await b.session.loadRevenue({ date: '2026-10-31' }); finish(copy(b.server.report));
    assert.equal(await first, null); assert.equal(b.session.report.totalRevenue, 99000); assert.equal(b.session.applied.date, '2026-10-31');
});
test('lỗi của GET cũ đến sau lần GET mới thành công không xóa báo cáo mới', async () => {
    const b = setup(); let reject;
    b.server.get = (url) => url.includes('date=2026-10-30') ? new Promise((_, fail) => { reject = fail; }) : copy(b.server.report);
    const first = b.session.loadRevenue({ date: '2026-10-30' }); await b.session.loadRevenue({ date: '2026-10-31' }); reject(new Error('Lỗi request cũ'));
    assert.equal(await first, null); assert.equal(b.session.report.totalRevenue, 87913); assert.equal(b.session.revenueLoading, false);
});
test('payload thiếu tổng tiền, sai period hoặc nhóm lỗi không được coi như báo cáo 0đ', async () => {
    for (const bad of [{ totalRevenue: null }, { period: 'week' }, { groups: [{ revenue: null, orderCount: 1 }] }]) {
        const b = setup(); b.server.get = () => ({ ...copy(b.server.report), ...bad });
        await assert.rejects(b.session.loadRevenue(), (e) => e.status === 502);
        assert.equal(b.session.report, null); assert.equal(b.session.applied, null);
    }
});
test('mất vai trò Admin thì xuất bị chặn và xóa số liệu khỏi model', async () => {
    const b = setup(); await b.session.loadRevenue(); await b.session.loadDashboard(); b.auth.role = 'Cashier';
    await assert.rejects(b.session.exportCsv(), (e) => e.status === 403); assert.equal(b.session.report, null); assert.equal(b.session.summary, null); assert.equal(downloads(b).length, 0);
});
