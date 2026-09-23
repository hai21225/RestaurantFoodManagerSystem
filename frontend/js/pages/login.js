(function () {
    const form = document.querySelector("#loginForm");
    const formView = document.querySelector("#loginFormView");
    const fields = document.querySelector("#loginFields");
    const username = document.querySelector("#loginUsername");
    const password = document.querySelector("#loginPassword");
    const usernameError = document.querySelector("#usernameError");
    const passwordError = document.querySelector("#passwordError");
    const togglePassword = document.querySelector("#togglePassword");
    const eye = togglePassword.querySelector(".login-eye");
    const eyeOff = togglePassword.querySelector(".login-eye-off");
    const submit = document.querySelector("#loginSubmit");
    const submitLabel = document.querySelector("#loginSubmitLabel");
    const spinner = submit.querySelector(".login-spinner");
    const errorMessage = document.querySelector("#loginError");
    const statusMessage = document.querySelector("#loginStatus");
    const success = document.querySelector("#loginSuccess");
    const restart = document.querySelector("#restartLogin");
    const forgotButton = document.querySelector("#forgotPassword");
    const forgotDialog = document.querySelector("#forgotDialog");

    // UI only: ?result=error previews rejection; otherwise preview success.
    // Remember me is a visual control. No credentials or login state are stored.
    const simulateError = new URLSearchParams(window.location.search).get("result") === "error";
    let pendingTimer = null;

    function setState(state) {
        form.dataset.state = state;
        const loading = state === "loading";
        fields.disabled = loading;
        submit.disabled = loading;
        form.setAttribute("aria-busy", String(loading));
        spinner.hidden = !loading;
        submitLabel.textContent = loading ? "Đang đăng nhập…" : "Đăng nhập";
        statusMessage.textContent = loading ? "Đang xử lý…" : "";
        errorMessage.hidden = state !== "error";
    }

    function showPassword(show) {
        password.type = show ? "text" : "password";
        togglePassword.setAttribute("aria-pressed", String(show));
        const label = show ? "Ẩn mật khẩu" : "Hiện mật khẩu";
        togglePassword.setAttribute("aria-label", label);
        togglePassword.title = label;
        eye.hidden = show;
        eyeOff.hidden = !show;
    }

    function validateField(input) {
        let message = "";

        if (input === username) {
            const value = input.value.trim();

            if (!value) {
                message = "Nhập tên đăng nhập hoặc email.";
            } else if (value.includes("@") && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) {
                message = "Email chưa đúng định dạng.";
            } else if (/\s/.test(value)) {
                message = "Tên đăng nhập không được chứa khoảng trắng.";
            }
        } else if (!input.value) {
            message = "Nhập mật khẩu.";
        }

        const target = input === username ? usernameError : passwordError;
        target.textContent = message;
        input.setAttribute("aria-invalid", String(Boolean(message)));
        return !message;
    }

    [username, password].forEach((input) => {
        input.addEventListener("blur", () => {
            if (form.dataset.state !== "loading") {
                validateField(input);
            }
        });

        input.addEventListener("input", () => {
            if (form.dataset.state === "error") {
                errorMessage.textContent = "";
                setState("default");
            }

            if (input.getAttribute("aria-invalid") === "true") {
                validateField(input);
            }
        });
    });

    togglePassword.addEventListener("click", () => showPassword(password.type === "password"));

    form.addEventListener("submit", (event) => {
        event.preventDefault();

        if (form.dataset.state === "loading" || form.dataset.state === "success") {
            return;
        }

        const validUsername = validateField(username);
        const validPassword = validateField(password);

        if (!validUsername || !validPassword) {
            errorMessage.textContent = "Kiểm tra lại thông tin đăng nhập.";
            setState("error");
            (validUsername ? password : username).focus();
            return;
        }

        showPassword(false);
        setState("loading");

        pendingTimer = window.setTimeout(() => {
            pendingTimer = null;

            if (simulateError) {
                errorMessage.textContent = "Tên đăng nhập hoặc mật khẩu không đúng.";
                setState("error");
                password.focus();
                password.select();
                return;
            }

            setState("success");
            password.value = "";
            formView.hidden = true;
            success.hidden = false;
            restart.focus();
        }, 1200);
    });

    restart.addEventListener("click", () => {
        form.reset();
        usernameError.textContent = "";
        passwordError.textContent = "";
        errorMessage.textContent = "";
        username.setAttribute("aria-invalid", "false");
        password.setAttribute("aria-invalid", "false");
        showPassword(false);
        success.hidden = true;
        formView.hidden = false;
        setState("default");
        username.focus();
    });

    forgotButton.addEventListener("click", () => forgotDialog.showModal());
    forgotDialog.addEventListener("close", () => forgotButton.focus());

    window.addEventListener("pagehide", () => {
        window.clearTimeout(pendingTimer);
        pendingTimer = null;
        password.value = "";
        showPassword(false);

        if (form.dataset.state === "loading") {
            setState("default");
        }
    });

    setState("default");
})();
