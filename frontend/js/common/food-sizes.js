(function () {
    const sizes = Object.freeze([
        { value: "Nhỏ", aliases: ["nhỏ", "nho"] },
        { value: "Vừa", aliases: ["vừa", "vua"] },
        { value: "Lớn", aliases: ["lớn", "lon"] }
    ]);

    function normalize(value) {
        const input = String(value || "").trim().toLocaleLowerCase("vi");
        return sizes.find((size) => size.aliases.includes(input))?.value || "";
    }

    function sort(items) {
        return [...items].sort((left, right) => {
            const leftIndex = sizes.findIndex((size) => size.value === normalize(left.sizeName ?? left.name));
            const rightIndex = sizes.findIndex((size) => size.value === normalize(right.sizeName ?? right.name));
            return leftIndex - rightIndex;
        });
    }

    window.foodSizes = Object.freeze({
        options: sizes.map((size) => size.value),
        normalize,
        isSupported: (value) => Boolean(normalize(value)),
        sort
    });
})();
