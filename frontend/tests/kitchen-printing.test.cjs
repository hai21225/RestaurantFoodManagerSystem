const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const context = { window: {}, Date };
for (const file of ["preparation-timing.js", "kitchen-printing.js"]) vm.runInNewContext(fs.readFileSync(path.join(__dirname, "../js/common", file), "utf8"), context);
const { renderSlip } = context.window.restaurantKitchenPrinting;
const generatedAt = Date.parse("2026-11-12T05:12:03Z");
const item = (id, status, foodName, quantity = 1) => ({ id, status, foodName, quantity, sizeName: "Vừa", toppings: [] });
const slips = (html) => Array.from(html.matchAll(/<article\b[^>]*class="kitchen-slip(?:\s[^"]*)?"[^>]*>[\s\S]*?<\/article>/g), (match) => match[0]);
const ticket = () => ({ kitchenOrderId: 10, orderId: 42, tableNumber: "B3", sentAt: "2026-11-12T05:00:00", waiterName: "Phục vụ A",
    order: { orderId: 42, note: "Ưu tiên món cho trẻ em", items: [item(99, "Pending", "MÓN THUỘC PHIẾU KHÁC")] },
    items: [item(1, "Pending", "Cơm gà", 2), item(2, "Preparing", "Canh chua", 3), item(3, "Ready", "Món đã sẵn sàng"), item(4, "Completed", "Món đã hoàn tất"), { ...item(5, "Cancelled", "Món đã hủy"), cancelReason: "Khách đổi món" }] });

test("each active dish gets an independent slip with its actual quantity instead of a combined kitchen list", () => {
    const source = ticket(), before = JSON.stringify(source);
    const html = renderSlip(source, { generatedAt });
    assert.match(html, /PHIẾU BẾP/);
    assert.match(html, /Bàn B3/);
    assert.doesNotMatch(html, /MÓN CẦN LÀM|CHỜ NHẬN|ĐANG CHẾ BIẾN/);
    assert.match(html, /2 phiếu món riêng/);
    assert.match(html, /5 phần trong bản in/);
    assert.match(html, /Cơm gà/); assert.match(html, /Canh chua/);
    assert.doesNotMatch(html, /Món đã sẵn sàng|Món đã hoàn tất|Món đã hủy|MÓN THUỘC PHIẾU KHÁC/);
    assert.equal((html.match(/class="kitchen-slip-item /g) || []).length, 2);
    const printed = slips(html);
    assert.equal(printed.length, 2, "One article per dish row, not one per portion");
    assert.match(printed[0], /Cơm gà/); assert.doesNotMatch(printed[0], /Canh chua/);
    assert.match(printed[1], /Canh chua/); assert.doesNotMatch(printed[1], /Cơm gà/);
    assert.match(printed[0], /kitchen-slip-quantity">2 <small>phần/);
    assert.match(printed[1], /kitchen-slip-quantity">3 <small>phần/);
    assert.match(printed[0], /Dòng món<\/dt><dd>#1/);
    assert.match(printed[1], /Dòng món<\/dt><dd>#2/);
    for (const printedSlip of printed) {
        assert.match(printedSlip, /Bàn B3/);
        assert.match(printedSlip, /Phiếu<\/dt><dd>#10/);
        assert.match(printedSlip, /Đơn<\/dt><dd>#42/);
        assert.equal((printedSlip.match(/class="kitchen-slip-item /g) || []).length, 1);
    }
    assert.equal(JSON.stringify(source), before, "Rendering does not mutate session data");
});

test("closed orders are clearly marked for reference and cannot become a new active cooking slip", () => {
    for (const status of ["Paid", "Cancelled"]) {
        const source = ticket(); source.order.status = status;
        const active = renderSlip(source, { generatedAt });
        assert.match(active, /ĐƠN ĐÃ KẾT THÚC — CHỈ ĐỐI CHIẾU/);
        assert.doesNotMatch(active, /class="kitchen-slip-item /);
        const all = renderSlip(source, { scope: "all", generatedAt });
        assert.match(all, /ĐƠN ĐÃ KẾT THÚC — CHỈ ĐỐI CHIẾU/);
        assert.match(all, /ĐÃ HỦY — KHÔNG LÀM/);
    }
});

test("all scope makes completed and cancelled lines unmistakable and reprints are prominently labelled", () => {
    const html = renderSlip(ticket(), { scope: "all", isReprint: true, generatedAt });
    assert.match(html, /class="kitchen-slip-reprint">BẢN IN LẠI/);
    assert.match(html, /BẢN ĐỐI CHIẾU/);
    assert.doesNotMatch(html, /CHỜ NHẬN|ĐANG CHẾ BIẾN/);
    assert.match(html, /5 phiếu món riêng/);
    assert.match(html, /8 phần trong bản in/);
    assert.equal((html.match(/ĐÃ XONG/g) || []).length, 2);
    assert.match(html, /is-cancelled[\s\S]*ĐÃ HỦY — KHÔNG LÀM/);
    assert.match(html, /Lý do hủy:<\/strong> Khách đổi món/);
    assert.equal(slips(html).length, 5);
    assert.equal((html.match(/class="kitchen-slip-reprint">BẢN IN LẠI/g) || []).length, 5,
        "A separated or cut reprint must keep its own warning");
    assert.doesNotMatch(renderSlip(ticket(), { scope: "all", isReprint: false, generatedAt }), /BẢN IN LẠI/);
});

test("sizes, toppings per portion and both kinds of notes are preserved without printing prices", () => {
    const source = ticket();
    source.totalAmount = 987654321; source.order.totalAmount = 876543219; source.order.taxAmount = 765432198;
    source.items = [{ ...item(1, "Preparing", "Cơm gà", 2), sizeName: "Phần lớn", note: "Không cay\nÍt hành", unitPrice: 654321987,
        toppings: [{ toppingName: "Trứng", quantity: 1, unitPrice: 543219876 }, { toppingName: "Rau", quanlity: 2 }] }];
    const html = renderSlip(source, { generatedAt });
    assert.match(html, /kitchen-slip-quantity">2 <small>phần/);
    assert.match(html, /Cỡ: Phần lớn/);
    assert.match(html, /Topping mỗi phần/);
    assert.match(html, /Trứng × 1/); assert.match(html, /Rau × 2/);
    assert.match(html, /Ghi chú món:<\/strong> Không cay<br>Ít hành/);
    assert.match(html, /Ghi chú đơn[\s\S]*Ưu tiên món cho trẻ em/);
    assert.doesNotMatch(html, /987654321|876543219|765432198|654321987|543219876|Đơn giá|Tổng tiền|Thành tiền|Thuế/);
});

test("all API-supplied text and identifiers are escaped instead of becoming markup or attributes", () => {
    const payload = '<img src=x onerror="alert(1)"><script>bad()</script>&\'';
    const source = { kitchenOrderId: payload, orderId: payload, tableNumber: payload, waiterName: payload, order: { note: payload },
        items: [{ ...item(1, "Cancelled", payload), sizeName: payload, note: payload, cancelReason: payload, toppings: [{ toppingName: payload, quantity: 1 }] }] };
    const html = renderSlip(source, { scope: "all", generatedAt });
    assert.doesNotMatch(html, /<(?:img|script)\b|<[^>]*\bonerror=/i);
    assert.match(html, /&lt;img src=x onerror=&quot;alert\(1\)&quot;&gt;/);
    assert.match(html, /&lt;script&gt;bad\(\)&lt;\/script&gt;&amp;&#39;/);
    assert.equal((html.match(/class="kitchen-slip-item is-cancelled"/g) || []).length, 1);
    assert.equal((html.match(/<article/g) || []).length, 1);
});

test("server sent time and client generation time are separate, both in Vietnam time, and do not claim a print receipt", () => {
    const html = renderSlip(ticket(), { generatedAt });
    assert.match(html, /Gửi phiếu lúc[\s\S]*12:00:00/);
    assert.doesNotMatch(html, /Giờ đặt món|Giờ gửi phiếu/);
    assert.match(html, /Lập bản in lúc: [^<]*12:12:03/);
    assert.match(html, /giờ Việt Nam trên thiết bị/);
    assert.doesNotMatch(html, /Đã in thành công|Máy in đã nhận|Mã giao dịch/);
    assert.match(html, /Đối chiếu màn hình trước khi làm; phiếu giấy không tự cập nhật/);
    assert.doesNotMatch(html, /Giờ nhận món|Đã in thành công|Đã ghi nhận bản in/);
    const future = renderSlip(ticket(), { generatedAt: Date.parse("2030-01-01T00:00:00Z") });
    assert.match(future, /Lập bản in lúc: [^<]*07:00:00/);
    assert.match(future, /Đối chiếu màn hình trước khi làm/);
});

test("separate slips retain their own dish details and repeat the shared ticket sent time once", () => {
    const source = ticket();
    source.items = [
        { ...item(1, "Pending", "Lẩu Thái hải sản"), sizeName: "Lớn", orderedAt: "2026-11-12T04:40:00", note: "Không cay" },
        { ...item(2, "Preparing", "Tôm rang me", 2), sizeName: "Đĩa", orderedAt: "2026-11-12T04:58:30Z", note: "Ít đường" }
    ];
    const printed = slips(renderSlip(source, { generatedAt }));
    assert.equal(printed.length, 2);
    assert.match(printed[0], /Lẩu Thái hải sản/); assert.match(printed[0], /Cỡ: Lớn/);
    assert.match(printed[0], /Gửi phiếu lúc[\s\S]*12:00:00/);
    assert.match(printed[0], /Ghi chú món:<\/strong> Không cay/);
    assert.doesNotMatch(printed[0], /Tôm rang me|Cỡ: Đĩa|11:58:30|Ít đường/);
    assert.match(printed[1], /Tôm rang me/); assert.match(printed[1], /Cỡ: Đĩa/);
    assert.match(printed[1], /Gửi phiếu lúc[\s\S]*12:00:00/);
    assert.match(printed[1], /Ghi chú món:<\/strong> Ít đường/);
    assert.doesNotMatch(printed[1], /Lẩu Thái hải sản|Cỡ: Lớn|11:40:00|Không cay/);
    for (const printedSlip of printed) {
        assert.equal((printedSlip.match(/Gửi phiếu lúc:/g) || []).length, 1);
        assert.doesNotMatch(printedSlip, /Giờ đặt món|Giờ gửi phiếu|11:40:00|11:58:30/);
        assert.match(printedSlip, /Cỡ:[\s\S]*kitchen-slip-sent-at[\s\S]*Gửi phiếu lúc:/);
    }
});

test("a missing ticket sent time is explicit and is not inferred from dish or order timestamps", () => {
    const source = ticket();
    source.sentAt = null;
    source.items = [
        { ...item(1, "Pending", "Có giờ đặt"), orderedAt: "2026-11-12T04:10:00Z" },
        { ...item(2, "Pending", "Chưa rõ giờ đặt"), orderedAt: "invalid", createdAt: "2026-11-12T04:20:00Z", updatedAt: "2026-11-12T04:30:00Z" }
    ];
    source.order.createdAt = "2026-11-12T04:00:00Z";
    const printed = slips(renderSlip(source, { generatedAt }));
    for (const printedSlip of printed) {
        assert.match(printedSlip, /Gửi phiếu lúc:<\/strong> Chưa có dữ liệu/);
        assert.equal((printedSlip.match(/Gửi phiếu lúc:/g) || []).length, 1);
        assert.doesNotMatch(printedSlip, /Giờ đặt món|Giờ gửi phiếu|11:10:00|11:20:00|11:30:00|11:00:00/);
    }
});

test("the same food in different sizes remains two independent slips and neither is merged by name", () => {
    const source = ticket();
    source.items = [
        { ...item(7, "Pending", "Lẩu Thái hải sản", 1), sizeName: "Nhỏ", orderedAt: "2026-11-12T04:00:00Z" },
        { ...item(8, "Pending", "Lẩu Thái hải sản", 3), sizeName: "Lớn", placedAt: "2026-11-12T04:15:00Z" }
    ];
    const printed = slips(renderSlip(source, { generatedAt }));
    assert.equal(printed.length, 2);
    assert.match(printed[0], /Cỡ: Nhỏ/); assert.doesNotMatch(printed[0], /Cỡ: Lớn/);
    assert.match(printed[0], /kitchen-slip-quantity">1 <small>phần/);
    assert.match(printed[1], /Cỡ: Lớn/); assert.doesNotMatch(printed[1], /Cỡ: Nhỏ/);
    assert.match(printed[1], /kitchen-slip-quantity">3 <small>phần/);
    assert.match(printed[1], /Gửi phiếu lúc[\s\S]*12:00:00/);
    assert.doesNotMatch(printed[1], /Giờ đặt món|11:15:00/);
});

test("unknown dates, quantities and empty scopes stay explicit instead of inventing printable dishes", () => {
    const source = ticket(); source.sentAt = "2026-02-30T10:00:00Z";
    source.items = [{ ...item(1, "Pending", "Chưa rõ số lượng"), quantity: "<script>" }];
    const html = renderSlip(source, { generatedAt: NaN });
    assert.match(html, /Gửi phiếu lúc:<\/strong> Chưa có dữ liệu/);
    assert.match(html, /Lập bản in lúc: —/);
    assert.match(html, /kitchen-slip-quantity">—/);
    assert.match(html, /— phần trong bản in/);
    assert.doesNotMatch(html, /NaN|Invalid Date|<script>/);
    const empty = renderSlip({ items: [item(1, "Ready", "Đã làm"), item(2, "Cancelled", "Đã hủy")] }, { generatedAt });
    assert.match(empty, /0 phiếu món riêng/);
    assert.match(empty, /Không có món cần làm trong phiếu này/);
    assert.equal((empty.match(/class="kitchen-slip-item /g) || []).length, 0);
    assert.match(renderSlip(null, { scope: "all", generatedAt }), /Phiếu chưa có dòng món/);
});
