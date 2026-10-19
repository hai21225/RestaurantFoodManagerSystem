(async function () {
    const list = document.querySelector("#inventoryList");
    const count = document.querySelector("#inventoryCount");
    const summaryValues = document.querySelectorAll(".management-summary strong");

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    try {
        const [inventory, ingredients] = await Promise.all([
            window.restaurantApi.get("/management/inventory"),
            window.restaurantApi.get("/management/ingredients")
        ]);
        const ingredientMap = new Map(ingredients.map((item) => [item.ingredientId, item]));
        const rows = inventory.map((stock) => {
            const ingredient = ingredientMap.get(stock.ingredientId) || {};
            const quantity = Number(stock.quanlity) || 0;
            const minimum = Number(ingredient.minimumStock) || 0;
            return { stock, ingredient, quantity, minimum };
        });
        const low = rows.filter((row) => row.quantity > 0 && row.quantity <= row.minimum).length;
        const empty = rows.filter((row) => row.quantity <= 0).length;

        count.textContent = `${rows.length} nguyên liệu`;
        summaryValues[0].textContent = String(rows.length);
        summaryValues[1].textContent = String(low);
        summaryValues[2].textContent = String(empty);
        summaryValues[3].textContent = "0";

        list.className = "management-rows";
        list.innerHTML = rows.length ? rows.map(({ stock, ingredient, quantity, minimum }) => `
            <article class="management-row">
                <div><strong>${escapeHtml(ingredient.name || `Nguyên liệu #${stock.ingredientId}`)}</strong><span>${escapeHtml(ingredient.unit || "Chưa có đơn vị")}</span></div>
                <div><span>Tồn hiện tại</span><strong>${quantity.toLocaleString("vi-VN")}</strong></div>
                <div><span>Tối thiểu</span><strong>${minimum.toLocaleString("vi-VN")}</strong></div>
            </article>
        `).join("") : '<div class="empty-workspace"><div><strong>Chưa có dữ liệu kho</strong></div></div>';
    } catch (error) {
        list.innerHTML = `<div><strong>Không tải được dữ liệu kho</strong><span>${escapeHtml(error.message)}</span></div>`;
    }
})();
