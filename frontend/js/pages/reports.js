(function () {
    const auth = window.restaurantAuth;
    const { ReportingSession, periods } = window.restaurantReporting;
    const session = new ReportingSession({ api: window.restaurantApi, auth });
    const $ = (id) => document.getElementById(id);
    const esc = (value) => String(value ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]);
    const money = (value) => `${value.toLocaleString("vi-VN", { maximumFractionDigits: 2 })}đ`;
    const utc = (value) => new Date(/[zZ]$|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
    const date = (value) => utc(value).toLocaleDateString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" });
    const input = () => ({ period: $("reportPeriod").value, date: $("reportDate").value });
    function message(text = "", error = false) { $("reportNotice").textContent = text; $("reportNotice").classList.toggle("is-error", error); }
    function render() {
        const allowed = session.hasAccess(), report = session.report;
        $("reportsContent").hidden = !allowed; $("reportsAccess").hidden = allowed;
        if (!allowed) return;
        $("loadRevenue").disabled = session.revenueLoading || session.exporting;
        for (const id of ["reportPeriod", "reportDate"]) $(id).disabled = session.revenueLoading || session.exporting;
        $("loadRevenue").textContent = session.revenueLoading ? "Đang tải…" : "Xem báo cáo";
        $("exportRevenue").disabled = !session.matches(input()) || session.exporting;
        $("exportRevenue").textContent = session.exporting ? "Đang xuất…" : "Tải CSV";
        $("reportFilterHint").textContent = report && !session.matches(input()) ? "Bộ lọc vừa thay đổi. Nhấn Xem báo cáo để áp dụng trước khi xuất CSV." : "";
        if (!report) {
            $("revenueSummary").innerHTML = ""; $("revenueRows").innerHTML = ""; $("revenueFoot").innerHTML = "";
            $("reportRange").textContent = ""; $("reportUpdated").textContent = "";
            $("reportEmpty").textContent = session.revenueLoading ? "Đang tải số liệu doanh thu…" : "Chưa tải được báo cáo. Chọn kỳ/ngày rồi bấm Xem báo cáo."; return;
        }
        const end = new Date(utc(report.to).getTime() - 1).toISOString();
        $("reportRange").textContent = `${periods[report.period]} · ${date(report.from)} – ${date(end)}`;
        $("revenueSummary").innerHTML = [["Doanh thu đã thu", money(report.totalRevenue)], ["Đơn đã thu", report.orderCount],
            ["Trung bình mỗi đơn", money(report.averagePerOrder)], ["Kỳ báo cáo", periods[report.period]]]
            .map(([label, value]) => `<article class="summary-tile"><span>${label}</span><strong>${esc(value)}</strong></article>`).join("");
        $("reportEmpty").textContent = report.orderCount === 0 ? "Kỳ này chưa có giao dịch đã thu. Các nhóm thời gian vẫn hiển thị số liệu từ báo cáo." : "";
        $("groupHeading").textContent = report.period === "day" ? "Khung giờ" : "Ngày";
        const maximum = Math.max(1, ...report.groups.map((g) => g.revenue));
        $("revenueRows").innerHTML = report.groups.map((group) => `<tr><th scope="row">${esc(group.label)}</th><td>${money(group.revenue)}</td>
            <td><meter min="0" max="${maximum}" value="${group.revenue}" aria-label="Doanh thu ${esc(group.label)}: ${money(group.revenue)}">${money(group.revenue)}</meter></td><td>${group.orderCount}</td></tr>`).join("");
        $("revenueFoot").innerHTML = `<tr><th scope="row">Tổng kỳ</th><td>${money(report.totalRevenue)}</td><td></td><td>${report.orderCount}</td></tr>`;
        $("reportUpdated").textContent = `Đã tải lúc ${utc(session.loadedAt).toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" })} · Giờ Việt Nam`;
    }
    async function load() {
        message();
        try {
            const promise = session.loadRevenue(input()); render(); const report = await promise;
            if (report) {
                $("reportDate").value = session.applied.date;
                const url = new URL(window.location.href); url.searchParams.set("period", session.applied.period); url.searchParams.set("date", session.applied.date); window.history.replaceState(null, "", url);
            }
        } catch (error) { message(error.message, true); }
        finally { render(); }
    }
    $("reportFilters").addEventListener("submit", (event) => { event.preventDefault(); if (!session.revenueLoading && !session.exporting) load(); });
    for (const id of ["reportPeriod", "reportDate"]) $(id).addEventListener("input", render);
    $("exportRevenue").addEventListener("click", async () => {
        if (!session.matches(input()) || session.exporting) return;
        message();
        try { const promise = session.exportCsv(); render(); const name = await promise; message(`Đã gửi file ${name} cho trình duyệt tải xuống.`); }
        catch (error) { message(error.message, true); }
        finally { render(); }
    });
    auth.ready.then(() => {
        render(); if (!session.hasAccess()) return;
        const params = new URLSearchParams(window.location.search), period = params.get("period") || "day";
        if (Object.hasOwn(periods, period)) $("reportPeriod").value = period;
        $("reportDate").value = params.get("date") || ""; load();
    });
})();
