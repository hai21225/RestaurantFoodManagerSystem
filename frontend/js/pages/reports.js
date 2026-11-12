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
    let selectedBucket = null;
    function message(text = "", error = false) { $("reportNotice").textContent = text; $("reportNotice").classList.toggle("is-error", error); }
    function selectBucket(index) {
        const group = session.report?.groups[index];
        if (!group) return;
        selectedBucket = index;
        $("reportChart").querySelectorAll("[data-bucket]").forEach((button) => {
            const selected = Number(button.dataset.bucket) === index;
            button.classList.toggle("is-selected", selected); button.setAttribute("aria-pressed", String(selected));
        });
        $("chartSelection").textContent = `${group.label} · ${money(group.revenue)} · ${group.orderCount} đơn đã thu`;
    }
    function renderChart(report) {
        // Chỉ tính tỷ lệ chiều cao cột; tiền và số đơn luôn hiển thị nguyên từ API.
        const maximum = Math.max(1, ...report.groups.map((group) => group.revenue));
        $("chartHint").textContent = report.groups.length > 14 ? "Cuộn ngang để xem các cột. Chọn cột để xem số liệu." : "Chọn một cột để xem doanh thu và số đơn.";
        $("reportChart").innerHTML = `<div class="revenue-chart${report.groups.length > 14 ? " is-dense" : ""}" style="--buckets:${report.groups.length}">${report.groups.map((group, index) =>
            `<button class="chart-column${group.revenue > 0 ? " has-revenue" : ""}" type="button" data-bucket="${index}" aria-pressed="false" aria-label="${esc(group.label)}: ${money(group.revenue)}, ${group.orderCount} đơn" title="${esc(group.label)}: ${money(group.revenue)}"><span class="chart-track" aria-hidden="true"><span class="chart-bar" style="height:${group.revenue / maximum * 100}%"></span></span><span class="chart-label" aria-hidden="true">${esc(group.label)}</span></button>`).join("")}</div>`;
        const first = report.groups.findIndex((group) => group.orderCount > 0 || group.revenue !== 0);
        if (report.groups.length) {
            selectBucket(selectedBucket ?? (first < 0 ? 0 : first));
            // Chỉ cuộn bên trong biểu đồ nhiều cột, tránh nhảy cả trang xuống dưới.
            const wrap = $("reportChart"), selected = wrap.querySelector(".is-selected");
            if (selected && wrap.scrollWidth > wrap.clientWidth) wrap.scrollLeft += selected.getBoundingClientRect().left - wrap.getBoundingClientRect().left - (wrap.clientWidth - selected.clientWidth) / 2;
        }
    }
    function renderTable() {
        const report = session.report;
        if (!report) return;
        const groups = $("onlyWithActivity").checked ? report.groups.filter((group) => group.orderCount > 0 || group.revenue !== 0) : report.groups;
        $("groupHeading").textContent = report.period === "day" ? "Khung giờ" : "Ngày";
        $("reportRowsHint").textContent = `${groups.length}/${report.groups.length} nhóm đang hiển thị · CSV luôn gồm toàn bộ kỳ.`;
        $("revenueRows").innerHTML = groups.length ? groups.map((group) => `<tr><th scope="row">${esc(group.label)}</th><td>${money(group.revenue)}</td><td>${group.orderCount}</td></tr>`).join("")
            : '<tr><td colspan="3" class="table-empty">Không có giao dịch đã thu trong kỳ này.</td></tr>';
        $("revenueFoot").innerHTML = `<tr><th scope="row">Tổng kỳ</th><td>${money(report.totalRevenue)}</td><td>${report.orderCount}</td></tr>`;
    }
    function render() {
        const allowed = session.hasAccess(), report = session.report;
        $("reportsContent").hidden = !allowed; $("reportsAccess").hidden = allowed;
        if (!allowed) return;
        $("loadRevenue").disabled = session.revenueLoading || session.exporting;
        for (const id of ["reportPeriod", "reportDate"]) $(id).disabled = session.revenueLoading || session.exporting;
        document.querySelectorAll("[data-report-period]").forEach((button) => {
            button.disabled = session.revenueLoading || session.exporting;
            button.setAttribute("aria-pressed", String(button.dataset.reportPeriod === $("reportPeriod").value));
        });
        $("loadRevenue").textContent = session.revenueLoading ? "Đang tải…" : "Xem báo cáo";
        $("exportRevenue").disabled = !session.matches(input()) || session.exporting;
        $("exportRevenue").textContent = session.exporting ? "Đang xuất…" : "Tải báo cáo CSV ↓";
        $("reportFilterHint").textContent = report && !session.matches(input()) ? "Bộ lọc chưa áp dụng. Nhấn Xem báo cáo trước khi tải CSV." : "";
        $("onlyWithActivity").disabled = !report;
        if (!report) {
            for (const id of ["revenueSummary", "revenueRows", "revenueFoot", "reportChart"]) $(id).innerHTML = "";
            for (const id of ["reportRange", "reportUpdated", "chartSelection", "reportRowsHint", "chartHint"]) $(id).textContent = "";
            $("reportEmpty").textContent = session.revenueLoading ? "Đang tải số liệu doanh thu…" : "Chưa tải được báo cáo. Chọn kỳ/ngày rồi bấm Xem báo cáo."; return;
        }
        const end = new Date(utc(report.to).getTime() - 1).toISOString();
        $("reportRange").textContent = `${periods[report.period]} · ${date(report.from)} – ${date(end)}`;
        $("revenueSummary").innerHTML = [["Doanh thu đã thu", money(report.totalRevenue), "Trong kỳ đang xem"], ["Đơn đã thu", report.orderCount, "Theo thời điểm thanh toán"],
            ["Trung bình mỗi đơn", money(report.averagePerOrder), "Số liệu từ báo cáo"]]
            .map(([label, value, note]) => `<article class="summary-tile"><span>${label}</span><strong>${esc(value)}</strong><small>${note}</small></article>`).join("");
        $("reportEmpty").textContent = report.orderCount === 0 ? "Kỳ này chưa có giao dịch đã thu. Chọn ngày hoặc kỳ khác để xem doanh thu trước đó." : "";
        renderChart(report); renderTable();
        $("reportUpdated").textContent = `Cập nhật ${utc(session.loadedAt).toLocaleString("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" })} · Giờ Việt Nam`;
    }
    async function load() {
        message(); selectedBucket = null;
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
    document.querySelectorAll("[data-report-period]").forEach((button) => button.addEventListener("click", () => {
        if (session.revenueLoading || session.exporting || !session.hasAccess()) return;
        $("reportPeriod").value = button.dataset.reportPeriod;
        load();
    }));
    for (const id of ["reportPeriod", "reportDate"]) $(id).addEventListener("input", render);
    $("onlyWithActivity").addEventListener("change", renderTable);
    $("reportChart").addEventListener("click", (event) => {
        const button = event.target.closest("[data-bucket]"); if (button) selectBucket(Number(button.dataset.bucket));
    });
    $("exportRevenue").addEventListener("click", async () => {
        if (!session.matches(input()) || session.exporting) return;
        message();
        try { const promise = session.exportCsv(); render(); const name = await promise; message(`Đã gửi file ${name} cho trình duyệt tải xuống. Bạn mở file trong mục Tải xuống / Downloads.`); }
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
