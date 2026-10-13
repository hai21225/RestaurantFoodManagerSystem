(function () {
    const weightSalesEnabled = false;

    const units = Object.freeze({
        portion: { label: "Phần", shortLabel: "phần", fractional: false },
        kg: { label: "Kilôgam (kg)", shortLabel: "kg", fractional: true },
        piece: { label: "Cái", shortLabel: "cái", fractional: false },
        plate: { label: "Đĩa", shortLabel: "đĩa", fractional: false },
        bowl: { label: "Tô", shortLabel: "tô", fractional: false },
        glass: { label: "Ly", shortLabel: "ly", fractional: false },
        bottle: { label: "Chai", shortLabel: "chai", fractional: false }
    });

    function normalize(unit) {
        const value = String(unit || "portion").trim().toLowerCase();
        const normalized = units[value] ? value : "portion";
        return weightSalesEnabled ? normalized : "portion";
    }

    function get(unit) {
        return units[normalize(unit)];
    }

    function formatNumber(value) {
        return Number(value || 0).toLocaleString("vi-VN", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 3
        });
    }

    window.foodUnits = Object.freeze({
        weightSalesEnabled,
        options: units,
        normalize,
        getLabel: (unit) => get(unit).label,
        getShortLabel: (unit) => get(unit).shortLabel,
        allowsFractionalQuantity: (unit) => get(unit).fractional,
        formatQuantity: (value, unit) => `${formatNumber(value)} ${get(unit).shortLabel}`,
        formatNumber
    });
})();
