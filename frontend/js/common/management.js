(function () {
    const navButtons = document.querySelectorAll("[data-page]");
    const feedbackButtons = document.querySelectorAll("[data-feedback]");
    const filterInputs = document.querySelectorAll("[data-filter-input]");
    const message = document.querySelector("#uiMessage");
    const reportButtons = document.querySelectorAll("[data-report]");

    navButtons.forEach((button) => {
        button.addEventListener("click", () => {
            window.location.href = button.dataset.page;
        });
    });

    feedbackButtons.forEach((button) => {
        button.addEventListener("click", () => {
            if (message) {
                message.textContent = button.dataset.feedback;
            }
        });
    });

    filterInputs.forEach((input) => {
        input.addEventListener("input", () => {
            if (message) {
                message.textContent = "Bộ lọc sẽ áp dụng khi kết nối nguồn dữ liệu.";
            }
        });
    });

    reportButtons.forEach((button) => {
        button.addEventListener("click", () => {
            reportButtons.forEach((item) => item.classList.remove("is-active"));
            button.classList.add("is-active");

            if (message) {
                message.textContent = `Đã chọn báo cáo: ${button.dataset.report}.`;
            }
        });
    });
})();
