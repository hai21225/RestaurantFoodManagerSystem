(function () {
    "use strict";

    // Mốc SQL/.NET không có offset trong API này là UTC, không phải giờ của trình duyệt.
    function parseUtc(value) {
        if (typeof value !== "string" || !value.trim()) return null;
        const text = value.trim();
        const parts = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})(?::(\d{2})(?:\.(\d{1,7}))?)?(Z|[+-]\d{2}:\d{2})?$/i.exec(text);
        if (!parts) return null;
        const [, yearText, monthText, dayText, hourText, minuteText, secondText, , offset] = parts;
        const year = Number(yearText), month = Number(monthText), day = Number(dayText);
        const hour = Number(hourText), minute = Number(minuteText), second = Number(secondText || 0);
        const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
        const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        if (month < 1 || month > 12 || day < 1 || day > days[month - 1] || hour > 23 || minute > 59 || second > 59) return null;
        if (offset && offset.toUpperCase() !== "Z") {
            const [offsetHour, offsetMinute] = offset.slice(1).split(":").map(Number);
            if (offsetHour > 23 || offsetMinute > 59) return null;
        }
        const epoch = Date.parse(offset ? text : `${text}Z`);
        return Number.isFinite(epoch) ? epoch : null;
    }

    const status = (value) => typeof value === "string" ? value.trim().toLowerCase() : "";
    const itemId = (value) => {
        if (typeof value !== "number" && typeof value !== "string") return null;
        if (typeof value === "string" && !/^\d+$/.test(value.trim())) return null;
        const id = Number(value);
        return Number.isSafeInteger(id) && id > 0 ? id : null;
    };

    function startedForItem(item, history) {
        if (!item || status(item.status) !== "preparing") return null;
        // Ưu tiên field DTO mới khi Hải bổ sung; không dùng mốc phiếu/gửi món thay cho từng món.
        const explicit = parseUtc(item.preparationStartedAt);
        if (explicit !== null) return new Date(explicit).toISOString();
        const id = itemId(item.orderItemId ?? item.id);
        if (id === null || !Array.isArray(history)) return null;
        let latest = null;
        for (const event of history) {
            if (!event || itemId(event.orderItemId) !== id || status(event.newStatus) !== "preparing" || status(event.oldStatus) === "preparing") continue;
            const epoch = parseUtc(event.createdAt);
            if (epoch === null) continue;
            if (latest === null || epoch > latest) latest = epoch;
        }
        return latest === null ? null : new Date(latest).toISOString();
    }

    function orderedForItem(item, history) {
        if (!item) return null;
        for (const value of [item.orderedAt, item.placedAt]) {
            const explicit = parseUtc(value);
            if (explicit !== null) return new Date(explicit).toISOString();
        }
        const id = itemId(item.orderItemId ?? item.id);
        if (id === null || !Array.isArray(history)) return null;
        let earliest = null;
        for (const event of history) {
            // Audit tạo món thật: oldStatus rỗng -> Pending. Pending -> Pending có thể chỉ đổi số lượng.
            if (!event || itemId(event.orderItemId) !== id || typeof event.oldStatus !== "string"
                || event.oldStatus.trim() !== "" || status(event.newStatus) !== "pending") continue;
            const epoch = parseUtc(event.createdAt);
            if (epoch !== null && (earliest === null || epoch < earliest)) earliest = epoch;
        }
        // Không dùng createdAt của dòng clone: đó có thể là giờ tách phần đã nấu, không phải giờ gọi.
        return earliest === null ? null : new Date(earliest).toISOString();
    }

    function compareTickets(a, b, mode = "oldest") {
        const aTime = parseUtc(a?.sentAt), bTime = parseUtc(b?.sentAt);
        // Phiếu thiếu mốc thật luôn ở cuối; không suy thời gian từ id hoặc lần tải trang.
        if (aTime === null && bTime !== null) return 1;
        if (aTime !== null && bTime === null) return -1;
        if (mode === "priority") {
            const prioritized = (value) => (typeof value === "number" || typeof value === "string")
                && Number.isFinite(Number(value)) && Number(value) > 0;
            const aPriority = prioritized(a?.priority), bPriority = prioritized(b?.priority);
            // Chỉ có nhóm ưu tiên > 0 được xác định; không tự suy số lớn hơn là cấp ưu tiên cao hơn.
            if (aPriority !== bPriority) return aPriority ? -1 : 1;
        }
        if (aTime !== null && bTime !== null && aTime !== bTime) return aTime < bTime ? -1 : 1;
        const aId = itemId(a?.kitchenOrderId), bId = itemId(b?.kitchenOrderId);
        if (aId === null && bId !== null) return 1;
        if (aId !== null && bId === null) return -1;
        return aId === bId ? 0 : aId < bId ? -1 : 1;
    }

    function label(start, now = Date.now()) {
        const epoch = parseUtc(start);
        if (epoch === null || typeof now !== "number" || !Number.isFinite(now) || !Number.isFinite(new Date(now).getTime()) || now < epoch) return null;
        const total = Math.floor((now - epoch) / 1000);
        const seconds = String(total % 60).padStart(2, "0");
        const minutes = Math.floor(total / 60);
        return minutes < 60 ? `${String(minutes).padStart(2, "0")}:${seconds}`
            : `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, "0")}:${seconds}`;
    }

    window.restaurantPreparationTiming = Object.freeze({ parseUtc, startedForItem, orderedForItem, compareTickets, label });
})();
