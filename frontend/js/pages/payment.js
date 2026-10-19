(function () {
    const paymentData = window.paymentData || {
        methods: [],
        bills: []
    };

    const state = {
        selectedBillId: paymentData.bills[0]?.id || null,
        method: paymentData.methods[0]?.value || "cash",
        searchTerm: "",
        isSubmitting: false,
        bills: paymentData.bills.map((bill) => ({ ...bill }))
    };

    const paymentSummary = document.querySelector("#paymentSummary");
    const billList = document.querySelector("#billList");
    const billCount = document.querySelector("#billCount");
    const billSearchInput = document.querySelector("#billSearchInput");
    const selectedBillStatus = document.querySelector("#selectedBillStatus");
    const billDetail = document.querySelector("#billDetail");
    const methodList = document.querySelector("#methodList");
    const paymentMethodLabel = document.querySelector("#paymentMethodLabel");
    const discountInput = document.querySelector("#discountInput");
    const paymentSubtotal = document.querySelector("#paymentSubtotal");
    const paymentTax = document.querySelector("#paymentTax");
    const paymentDiscount = document.querySelector("#paymentDiscount");
    const paymentGrandTotal = document.querySelector("#paymentGrandTotal");
    const confirmPaymentButton = document.querySelector("#confirmPaymentButton");
    const printBillButton = document.querySelector("#printBillButton");
    const navButtons = document.querySelectorAll("[data-page]");

    async function loadPaymentOrder() {
        const params = new URLSearchParams(window.location.search);
        const savedOrder = JSON.parse(window.localStorage.getItem("restaurant.lastOrder") || "null");
        const orderId = Number(params.get("orderId") || savedOrder?.orderId || 0);
        if (!orderId) {
            renderSummary();
            renderBillList();
            return;
        }
        try {
            const order = await window.restaurantApi.get(`/orders/${orderId}`);
            let table = null;
            try { table = await window.restaurantApi.get(`/restaurant-tables/${order.tableId}`); } catch (error) { table = null; }
            state.bills = [{
                id: order.orderId,
                orderCode: `ORD-${order.orderId}`,
                tableName: table ? `Bàn ${table.tableNumber}` : `Bàn ${order.tableId}`,
                guests: 0,
                createdAt: new Date(order.createdAt).toLocaleString("vi-VN"),
                cashier: "Chưa xác nhận",
                status: String(order.status).toLowerCase() === "paid" ? "paid" : "waiting",
                statusLabel: String(order.status).toLowerCase() === "paid" ? "Đã thanh toán" : "Chờ thanh toán",
                discount: Number(order.discountAmount) || 0,
                taxAmount: Number(order.taxAmount) || 0,
                items: order.items?.length
                    ? order.items.map((item) => ({
                        name: item.foodName,
                        sizeName: item.sizeName || "",
                        quantity: Number(item.quantity) || 0,
                        unitPrice: Number(item.unitPrice) || 0,
                        unit: window.foodUnits.normalize(item.saleUnit),
                        note: item.note || ""
                    }))
                    : [{
                        name: "Tổng món trong đơn",
                        sizeName: "",
                        quantity: 1,
                        unitPrice: Number(order.subtotal) || 0,
                        unit: "portion",
                        note: ""
                    }]
            }];
            state.selectedBillId = order.orderId;
            renderSummary(); renderBillList(); renderBillDetail(); renderTotals();
        } catch (error) {
            billList.innerHTML = `<p class="empty-state">${error.message}</p>`;
        }
    }

    function formatCurrency(value) {
        return `${value.toLocaleString("vi-VN")}đ`;
    }

    function getBillSubtotal(bill) {
        if (!bill) {
            return 0;
        }

        return bill.items.reduce((sum, item) => sum + item.unitPrice * item.quantity, 0);
    }

    function getTaxAmount(bill) {
        return Number(bill?.taxAmount) || 0;
    }

    function getBillTotals(bill) {
        const subtotal = getBillSubtotal(bill);
        const tax = getTaxAmount(bill);
        const requestedDiscount = Math.max(Number(bill?.discount) || 0, 0);
        const discount = Math.min(requestedDiscount, subtotal + tax);
        const calculatedTotal = subtotal + tax - discount;

        const grandTotal = bill?.status === "paid"
            ? bill.paidTotal ?? calculatedTotal
            : calculatedTotal;

        return { subtotal, tax, discount, grandTotal };
    }

    function getSelectedBill() {
        return state.bills.find((bill) => bill.id === state.selectedBillId);
    }

    function getFilteredBills() {
        const searchTerm = state.searchTerm.toLowerCase();

        return state.bills.filter((bill) => {
            const matchSearch = bill.tableName.toLowerCase().includes(searchTerm)
                || bill.orderCode.toLowerCase().includes(searchTerm);

            return matchSearch;
        });
    }

    function renderSummary() {
        const waitingBills = state.bills.filter((bill) => bill.status === "waiting");
        const paidBills = state.bills.filter((bill) => bill.status === "paid");
        const waitingAmount = waitingBills.reduce((sum, bill) => sum + getBillSubtotal(bill), 0);
        const paidAmount = paidBills.reduce((sum, bill) => sum + getBillTotals(bill).grandTotal, 0);
        const summary = [
            { label: "Chờ thanh toán", value: waitingBills.length },
            { label: "Đã thanh toán", value: paidBills.length },
            { label: "Tiền đang chờ", value: formatCurrency(waitingAmount) },
            { label: "Đã thu", value: formatCurrency(paidAmount) }
        ];

        paymentSummary.innerHTML = summary.map((item) => `
            <article class="payment-summary-card">
                <span>${item.label}</span>
                <strong>${item.value}</strong>
            </article>
        `).join("");
    }

    function renderBillList() {
        const bills = getFilteredBills();

        billCount.textContent = `${bills.length} đơn`;

        if (bills.length === 0) {
            billList.innerHTML = '<p class="empty-state">Không tìm thấy hóa đơn phù hợp.</p>';
            return;
        }

        billList.innerHTML = bills.map((bill) => {
            const subtotal = getBillSubtotal(bill);

            return `
                <button
                    class="bill-button${bill.id === state.selectedBillId ? " is-selected" : ""}"
                    type="button"
                    data-bill-id="${bill.id}"
                >
                    <div class="bill-button-top">
                        <strong>${bill.tableName}</strong>
                        <span class="status status-${bill.status === "paid" ? "empty" : "payment"}">${bill.statusLabel}</span>
                    </div>
                    <span>${bill.orderCode} - ${bill.guests} khách</span>
                    <div class="bill-button-bottom">
                        <span>${bill.createdAt}</span>
                        <strong>${formatCurrency(subtotal)}</strong>
                    </div>
                </button>
            `;
        }).join("");

        billList.querySelectorAll("[data-bill-id]").forEach((button) => {
            button.addEventListener("click", () => {
                state.selectedBillId = Number(button.dataset.billId);
                renderBillList();
                renderBillDetail();
                renderTotals();
            });
        });
    }

    function renderBillDetail() {
        const bill = getSelectedBill();

        if (!bill) {
            selectedBillStatus.textContent = "Chưa chọn";
            billDetail.innerHTML = '<p class="empty-state">Chọn một hóa đơn để xem chi tiết.</p>';
            return;
        }

        selectedBillStatus.textContent = bill.statusLabel;

        billDetail.innerHTML = `
            <div class="bill-heading">
                <strong>${bill.tableName}</strong>
                <span>${bill.orderCode} - ${bill.createdAt}</span>
            </div>

            <div class="bill-info-grid">
                <div class="bill-info-row">
                    <span>Số khách</span>
                    <strong>${bill.guests}</strong>
                </div>
                <div class="bill-info-row">
                    <span>Thu ngân</span>
                    <strong>${bill.cashier}</strong>
                </div>
                <div class="bill-info-row">
                    <span>Trạng thái</span>
                    <strong>${bill.statusLabel}</strong>
                </div>
            </div>

            <ul class="bill-items">
                ${bill.items.map((item) => `
                    <li>
                        <div>
                            <strong>${item.name}</strong>
                            <span>${item.sizeName ? `${item.sizeName} · ` : ""}${formatCurrency(item.unitPrice)} / ${window.foodUnits.getShortLabel(item.unit)}</span>
                            ${item.note ? `<span>${item.note}</span>` : ""}
                        </div>
                        <strong>${window.foodUnits.formatQuantity(item.quantity, item.unit)}</strong>
                        <strong>${formatCurrency(item.unitPrice * item.quantity)}</strong>
                    </li>
                `).join("")}
            </ul>
        `;
    }

    function renderMethods() {
        methodList.innerHTML = paymentData.methods.map((method) => `
            <button
                class="method-button${method.value === state.method ? " is-active" : ""}"
                type="button"
                data-method="${method.value}"
            >
                ${method.label}
            </button>
        `).join("");

        methodList.querySelectorAll("[data-method]").forEach((button) => {
            button.addEventListener("click", () => {
                state.method = button.dataset.method;
                renderMethods();
                renderTotals();
            });
        });
    }

    function renderTotals() {
        const bill = getSelectedBill();
        const method = paymentData.methods.find((item) => item.value === state.method);
        const { subtotal, tax, discount, grandTotal } = getBillTotals(bill);

        discountInput.value = discount;
        discountInput.disabled = true;
        paymentMethodLabel.textContent = method ? method.label : "Chưa chọn";
        paymentSubtotal.textContent = formatCurrency(subtotal);
        paymentTax.textContent = formatCurrency(tax);
        paymentDiscount.textContent = formatCurrency(discount);
        paymentGrandTotal.textContent = formatCurrency(grandTotal);

        confirmPaymentButton.disabled = state.isSubmitting || !bill || bill.status === "paid";
    }

    function setupNavigation() {
        navButtons.forEach((button) => {
            button.addEventListener("click", () => {
                window.location.href = button.dataset.page;
            });
        });
    }

    billSearchInput.addEventListener("input", () => {
        state.searchTerm = billSearchInput.value.trim();
        renderBillList();
    });

    discountInput.addEventListener("input", () => {
        const bill = getSelectedBill();

        if (!bill || bill.status === "paid") {
            return;
        }

        bill.discount = Math.max(Number(discountInput.value) || 0, 0);
        renderTotals();
    });

    confirmPaymentButton.addEventListener("click", async () => {
        const bill = getSelectedBill();

        if (state.isSubmitting || !bill || bill.status === "paid") {
            return;
        }

        const user = JSON.parse(window.sessionStorage.getItem("restaurant.currentUser") || "null");
        if (!user?.userId) { window.alert("Bạn cần đăng nhập trước khi thanh toán."); return; }
        state.isSubmitting = true;
        confirmPaymentButton.disabled = true;
        confirmPaymentButton.textContent = "Đang xử lý...";
        try {
            const payment = await window.restaurantApi.post("/payments", {
                orderId: bill.id,
                paymentMethod: state.method,
                transactionCode: "",
                createdBy: user.userId
            });
            bill.paidTotal = Number(payment.amount) || getBillTotals(bill).grandTotal;
            bill.status = "paid";
            bill.statusLabel = "Đã thanh toán";
            renderSummary(); renderBillList(); renderBillDetail(); renderTotals();
        } catch (error) {
            window.alert(error.message);
        } finally {
            state.isSubmitting = false;
            renderTotals();
            confirmPaymentButton.textContent = "Xác nhận thanh toán";
        }
    });

    printBillButton.addEventListener("click", () => {
        printBillButton.textContent = "Đã tạo phiếu tạm tính";

        window.setTimeout(() => {
            printBillButton.textContent = "In tạm tính";
        }, 1400);
    });

    setupNavigation();
    renderSummary();
    renderBillList();
    renderBillDetail();
    renderMethods();
    renderTotals();
    loadPaymentOrder();
})();
