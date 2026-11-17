(function () {
    const form = document.querySelector("#registerForm");
    const fields = document.querySelector("#registerFields");
    const submit = document.querySelector("#registerSubmit");
    const submitLabel = document.querySelector("#registerSubmitLabel");
    const spinner = submit.querySelector(".login-spinner");
    const errorMessage = document.querySelector("#registerError");
    const statusMessage = document.querySelector("#registerStatus");
    const togglePassword = document.querySelector("#toggleRegPassword");
    const eye = togglePassword.querySelector(".login-eye");
    const eyeOff = togglePassword.querySelector(".login-eye-off");

    const inputs = {
        fullName: document.querySelector("#regFullName"),
        userName: document.querySelector("#regUserName"),
        email: document.querySelector("#regEmail"),
        phone: document.querySelector("#regPhone"),
        password: document.querySelector("#regPassword"),
        confirmPassword: document.querySelector("#regConfirmPassword")
    };

    const errorSlots = {
        fullName: document.querySelector("#fullNameError"),
        userName: document.querySelector("#userNameError"),
        email: document.querySelector("#emailError"),
        phone: document.querySelector("#phoneError"),
        password: document.querySelector("#passwordError"),
        confirmPassword: document.querySelector("#confirmPasswordError")
    };

    function setState(state) {
        form.dataset.state = state;
        const loading = state === "loading";
        fields.disabled = loading;
        submit.disabled = loading;
        form.setAttribute("aria-busy", String(loading));
        spinner.hidden = !loading;
        submitLabel.textContent = loading ? "Đang đăng ký…" : "Đăng ký";
        statusMessage.textContent = loading ? "Đang tạo tài khoản…" : "";
        errorMessage.hidden = state !== "error";
    }

    function showPassword(show) {
        inputs.password.type = show ? "text" : "password";
        togglePassword.setAttribute("aria-pressed", String(show));
        const label = show ? "Ẩn mật khẩu" : "Hiện mật khẩu";
        togglePassword.setAttribute("aria-label", label);
        togglePassword.title = label;
        eye.hidden = show;
        eyeOff.hidden = !show;
    }

    function validateField(key) {
        const input = inputs[key];
        const value = input.value.trim();
        let message = "";

        if (key === "fullName" && !value) {
            message = "Nhập họ và tên.";
        } else if (key === "userName") {
            if (!value) message = "Nhập tên đăng nhập.";
            else if (/\s/.test(value)) message = "Tên đăng nhập không được chứa khoảng trắng.";
        } else if (key === "email") {
            if (!value) message = "Nhập email.";
            else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) message = "Email chưa đúng định dạng.";
        } else if (key === "phone") {
            if (!value) message = "Nhập số điện thoại.";
            else if (!/^[0-9+\s()-]{8,20}$/.test(value)) message = "Số điện thoại chưa hợp lệ.";
        } else if (key === "password") {
            if (!input.value) message = "Nhập mật khẩu.";
            else if (input.value.length < 6) message = "Mật khẩu tối thiểu 6 ký tự.";
        } else if (key === "confirmPassword") {
            if (!input.value) message = "Xác nhận lại mật khẩu.";
            else if (input.value !== inputs.password.value) message = "Mật khẩu xác nhận không khớp.";
        }

        errorSlots[key].textContent = message;
        input.setAttribute("aria-invalid", String(Boolean(message)));
        return !message;
    }

    Object.entries(inputs).forEach(([key, input]) => {
        input.addEventListener("blur", () => {
            if (form.dataset.state !== "loading") validateField(key);
        });
        input.addEventListener("input", () => {
            if (form.dataset.state === "error") {
                errorMessage.textContent = "";
                setState("default");
            }
            if (input.getAttribute("aria-invalid") === "true") validateField(key);
            if (key === "password" && inputs.confirmPassword.value) validateField("confirmPassword");
        });
    });

    togglePassword.addEventListener("click", () => showPassword(inputs.password.type === "password"));

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        if (form.dataset.state === "loading") return;

        const keys = Object.keys(inputs);
        let firstInvalid = null;
        for (const key of keys) {
            if (!validateField(key) && !firstInvalid) firstInvalid = inputs[key];
        }
        if (firstInvalid) {
            errorMessage.textContent = "Vui lòng kiểm tra lại thông tin đăng ký.";
            setState("error");
            firstInvalid.focus();
            return;
        }

        showPassword(false);
        setState("loading");

        try {
            const result = await window.restaurantApi.post("/auth/customer/register", {
                fullName: inputs.fullName.value.trim(),
                userName: inputs.userName.value.trim(),
                email: inputs.email.value.trim(),
                phone: inputs.phone.value.trim(),
                password: inputs.password.value,
                confirmPassword: inputs.confirmPassword.value
            });
            window.restaurantAuth.setSession(result.data);
            window.location.href = "reservations.html";
        } catch (error) {
            errorMessage.textContent = error.message || "Không thể đăng ký tài khoản.";
            setState("error");
        }
    });
})();
