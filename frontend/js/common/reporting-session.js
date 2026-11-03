(function () {
    const fail = (message, status = 400) => Object.assign(new Error(message), { status });
    const periods = { day: "Ngày", week: "Tuần", month: "Tháng" };
    function queryOf(input = {}) {
        const period = input.period || "day", date = String(input.date || "").trim();
        if (!Object.hasOwn(periods, period)) throw fail("Chọn kỳ ngày, tuần hoặc tháng.");
        if (date) {
            if (!/^\d{4}-\d{2}-\d{2}$/.test(date) || date.startsWith("0000")) throw fail("Ngày báo cáo không hợp lệ.");
            const parsed = new Date(`${date}T00:00:00Z`);
            if (!Number.isFinite(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== date) throw fail("Ngày báo cáo không hợp lệ.");
        }
        return { period, date };
    }
    function vnDate(value) {
        const date = new Date(/[zZ]$|[+-]\d\d:\d\d$/.test(value) ? value : `${value}Z`);
        if (!Number.isFinite(date.getTime())) throw fail("Không đọc được mốc thời gian báo cáo.", 502);
        const parts = new Intl.DateTimeFormat("en-CA", { timeZone: "Asia/Ho_Chi_Minh", year: "numeric", month: "2-digit", day: "2-digit" }).formatToParts(date);
        return ["year", "month", "day"].map((type) => parts.find((p) => p.type === type).value).join("-");
    }
    function queryString(query) {
        const params = new URLSearchParams({ period: query.period });
        if (query.date) params.set("date", query.date);
        return params.toString();
    }
    const finite = (value) => typeof value === "number" && Number.isFinite(value);
    class ReportingSession {
        constructor({ api, auth }) {
            this.api = api; this.auth = auth;
            this.summary = null; this.tables = []; this.tableError = "";
            this.dashboardLoading = false; this.dashboardVersion = 0;
            this.report = null; this.applied = null; this.loadedAt = null;
            this.revenueLoading = false; this.revenueVersion = 0; this.exporting = false;
        }
        hasAccess() { return this.auth.hasRole("Admin"); }
        assertAccess() {
            if (!this.hasAccess()) {
                this.summary = null; this.tables = []; this.report = null; this.applied = null;
                throw fail("Chỉ Quản trị viên được xem tổng quan và báo cáo doanh thu.", 403);
            }
        }
        async loadDashboard() {
            this.assertAccess();
            const version = ++this.dashboardVersion;
            this.dashboardLoading = true; this.summary = null; this.tables = []; this.tableError = "";
            try {
                const [summary, tables] = await Promise.allSettled([this.api.get("/reports/dashboard"), this.api.get("/restaurant-tables")]);
                if (version !== this.dashboardVersion) return null;
                if (summary.status === "rejected") throw summary.reason;
                const data = summary.value;
                if (!data || !Array.isArray(data.recentOrders) || !data.tables || !data.kitchen
                    || ![data.revenueToday, data.paidOrderCountToday, data.openOrderCount, data.lowStockCount,
                        data.tables.total, data.tables.available, data.tables.occupied, data.tables.maintenance,
                        data.kitchen.open, data.kitchen.pending, data.kitchen.preparing, data.kitchen.ready].every(finite)) throw fail("Dữ liệu tổng quan chưa đầy đủ. Hãy làm mới.", 502);
                this.summary = data;
                if (tables.status === "fulfilled" && Array.isArray(tables.value)) this.tables = tables.value;
                else this.tableError = tables.status === "rejected" ? tables.reason.message : "Không đọc được danh sách bàn.";
                return data;
            } finally { if (version === this.dashboardVersion) this.dashboardLoading = false; }
        }
        async loadRevenue(input = {}) {
            this.assertAccess();
            if (this.exporting) throw fail("Chờ xuất CSV xong trước khi đổi kỳ báo cáo.", 409);
            const query = queryOf(input), version = ++this.revenueVersion;
            this.revenueLoading = true; this.report = null; this.applied = null;
            try {
                const data = await this.api.get(`/reports/revenue?${queryString(query)}`);
                if (version !== this.revenueVersion) return null;
                if (!data || data.period !== query.period || !Array.isArray(data.groups)
                    || ![data.totalRevenue, data.orderCount, data.averagePerOrder].every(finite)
                    || !data.groups.every((g) => finite(g.revenue) && finite(g.orderCount))) throw fail("Dữ liệu báo cáo chưa đầy đủ. Hãy tải lại.", 502);
                const startDate = vnDate(data.from); vnDate(data.to);
                this.report = data;
                // Nếu không chọn ngày, cố định ngày server đã dùng để CSV không trôi kỳ qua nửa đêm.
                this.applied = { period: query.period, date: query.date || startDate };
                this.loadedAt = new Date().toISOString();
                return data;
            } catch (error) { if (version !== this.revenueVersion) return null; throw error; }
            finally { if (version === this.revenueVersion) this.revenueLoading = false; }
        }
        matches(input) {
            if (!this.report || !this.applied || this.revenueLoading) return false;
            try { const query = queryOf(input); return query.period === this.applied.period && query.date === this.applied.date; }
            catch (_) { return false; }
        }
        async exportCsv() {
            this.assertAccess();
            if (this.exporting || this.revenueLoading || !this.report || !this.applied) throw fail("Tải báo cáo thành công trước khi xuất CSV.", 409);
            const query = { ...this.applied };
            const filename = `doanh-thu-${query.period}-${vnDate(this.report.from)}.csv`;
            this.exporting = true;
            try { return await this.api.download(`/reports/revenue/export.csv?${queryString(query)}`, filename); }
            finally { this.exporting = false; }
        }
    }
    window.restaurantReporting = { ReportingSession, queryOf, queryString, vnDate, periods };
})();
