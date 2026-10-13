(function () {
    const checking = document.querySelector("#setupChecking");
    const retry = document.querySelector("#setupRetry");
    const formView = document.querySelector("#setupFormView");
    const form = document.querySelector("#setupForm");
    const fields = document.querySelector("#setupFields");
    const submit = document.querySelector("#setupSubmit");
    const submitLabel = document.querySelector("#setupSubmitLabel");
    const spinner = submit.querySelector(".login-spinner");
    const message = document.querySelector("#setupMessage");
    const success = document.querySelector("#setupSuccess");
    const password = document.querySelector("#setupPassword");
    const confirmPassword = document.querySelector("#setupConfirmPassword");

    function setLoading(loading) {
        fields.disabled = loading;
        submit.disabled = loading;
        spinner.hidden = !loading;
        submitLabel.textContent = loading ? "Đang tạo tài khoản..." : "Tạo tài khoản Admin";
        form.setAttribute("aria-busy", String(loading));
    }

    function showMessage(text, type) {
        message.textContent = text;
        message.classList.toggle("is-success", type === "success");
    }

    async function checkSetupStatus() {
        retry.hidden = true;
        checking.querySelector("strong").textContent = "Đang kiểm tra hệ thống";
        checking.querySelector("span").textContent = "Vui lòng chờ trong giây lát.";

        try {
            const status = await window.restaurantApi.get("/setup/status");
            if (!status.requiresSetup) {
                window.location.replace("login.html");
                return;
            }

            checking.hidden = true;
            formView.hidden = false;
            document.querySelector("#setupFullName").focus();
        } catch (error) {
            checking.querySelector("strong").textContent = "Không kết nối được máy chủ";
            checking.querySelector("span").textContent = error.message;
            retry.hidden = false;
        }
    }

    document.querySelectorAll("[data-password-toggle]").forEach((button) => {
        button.addEventListener("click", () => {
            const input = document.getElementById(button.dataset.passwordToggle);
            const show = input.type === "password";
            input.type = show ? "text" : "password";
            button.textContent = show ? "Ẩn" : "Hiện";
            button.setAttribute("aria-pressed", String(show));
            button.setAttribute("aria-label", show ? "Ẩn mật khẩu" : "Hiện mật khẩu");
            button.title = show ? "Ẩn mật khẩu" : "Hiện mật khẩu";
        });
    });

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        showMessage("");

        if (!form.checkValidity()) {
            form.reportValidity();
            return;
        }

        if (password.value !== confirmPassword.value) {
            showMessage("Mật khẩu xác nhận không khớp.");
            confirmPassword.focus();
            return;
        }

        setLoading(true);

        try {
            const data = new FormData(form);
            await window.restaurantApi.post("/setup/first-admin", {
                fullName: data.get("fullName").trim(),
                userName: data.get("userName").trim(),
                email: data.get("email").trim(),
                phone: data.get("phone").trim(),
                password: data.get("password"),
                confirmPassword: data.get("confirmPassword")
            });

            form.reset();
            formView.hidden = true;
            success.hidden = false;
            document.querySelector("#goToLogin").focus();
        } catch (error) {
            showMessage(error.message);
        } finally {
            setLoading(false);
        }
    });

    retry.addEventListener("click", checkSetupStatus);
    document.querySelector("#goToLogin").addEventListener("click", () => {
        window.location.href = "login.html";
    });

    checkSetupStatus();
})();
