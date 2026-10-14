(function () {
    const roleLabels = {
        Admin: "Quản trị",
        Waiter: "Phục vụ",
        Chef: "Bếp",
        Cashier: "Thu ngân",
        WarehouseManager: "Kho"
    };
    const state = {
        employees: [],
        search: "",
        role: "all",
        status: "all"
    };

    const list = document.querySelector("#employeeList");
    const count = document.querySelector("#employeeCount");
    const dialog = document.querySelector("#employeeDialog");
    const form = document.querySelector("#employeeForm");
    const submit = document.querySelector("#employeeSubmit");
    const formMessage = document.querySelector("#employeeFormMessage");
    const password = document.querySelector("#employeePassword");
    const confirmPassword = document.querySelector("#employeeConfirmPassword");

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function getFilteredEmployees() {
        const search = state.search.toLocaleLowerCase("vi");
        return state.employees.filter((employee) => {
            const searchable = [employee.fullName, employee.userName, employee.email, employee.phone]
                .join(" ")
                .toLocaleLowerCase("vi");
            const matchesRole = state.role === "all" || employee.roles.includes(state.role);
            const matchesStatus = state.status === "all"
                || (state.status === "active" && employee.isActive)
                || (state.status === "locked" && !employee.isActive);
            return searchable.includes(search) && matchesRole && matchesStatus;
        });
    }

    function renderSummary() {
        document.querySelector("#employeeTotal").textContent = state.employees.length;
        document.querySelector("#employeeActive").textContent = state.employees.filter((item) => item.isActive).length;
        document.querySelector("#employeeAdmins").textContent = state.employees.filter((item) => item.roles.includes("Admin")).length;
        document.querySelector("#employeeLocked").textContent = state.employees.filter((item) => !item.isActive).length;
    }

    function renderList() {
        const employees = getFilteredEmployees();
        count.textContent = `${employees.length} người`;

        if (!employees.length) {
            list.innerHTML = `
                <div class="empty-workspace">
                    <div>
                        <strong>Chưa có nhân viên phù hợp</strong>
                        <span>Thêm nhân viên mới hoặc thay đổi bộ lọc.</span>
                    </div>
                </div>`;
            return;
        }

        list.innerHTML = employees.map((employee) => {
            const roles = employee.roles.length
                ? employee.roles.map((role) => roleLabels[role] || role).join(", ")
                : "Chưa gán";
            return `
                <article class="management-row">
                    <div>
                        <strong>${escapeHtml(employee.fullName)}</strong>
                        <span>@${escapeHtml(employee.userName)} · ${escapeHtml(employee.email)}</span>
                        <span>${escapeHtml(employee.phone || "Chưa có số điện thoại")}</span>
                    </div>
                    <div>
                        <span>Vai trò</span>
                        <strong>${escapeHtml(roles)}</strong>
                    </div>
                    <div>
                        <span>Trạng thái</span>
                        <strong class="employee-status ${employee.isActive ? "is-active" : "is-locked"}">
                            ${employee.isActive ? "Đang hoạt động" : "Tạm khóa"}
                        </strong>
                    </div>
                </article>`;
        }).join("");
    }

    async function loadEmployees() {
        list.innerHTML = '<div class="empty-workspace"><div><strong>Đang tải nhân viên...</strong></div></div>';
        try {
            state.employees = await window.restaurantApi.get("/users");
            renderSummary();
            renderList();
        } catch (error) {
            list.innerHTML = `<div class="empty-workspace"><div><strong>Không tải được nhân viên</strong><span>${escapeHtml(error.message)}</span></div></div>`;
        }
    }

    function openDialog() {
        form.reset();
        formMessage.textContent = "";
        formMessage.className = "form-message";
        password.type = "password";
        confirmPassword.type = "password";
        dialog.showModal();
        form.elements.fullName.focus();
    }

    function closeDialog() {
        dialog.close();
        document.querySelector("#addEmployeeButton").focus();
    }

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        formMessage.textContent = "";

        if (!form.checkValidity()) {
            form.reportValidity();
            return;
        }

        if (password.value !== confirmPassword.value) {
            formMessage.textContent = "Mật khẩu xác nhận không khớp.";
            formMessage.className = "form-message is-error";
            confirmPassword.focus();
            return;
        }

        const data = new FormData(form);
        submit.disabled = true;
        submit.textContent = "Đang tạo...";

        try {
            await window.restaurantApi.post("/users", {
                fullName: data.get("fullName").trim(),
                roleName: data.get("roleName"),
                userName: data.get("userName").trim(),
                email: data.get("email").trim(),
                phone: data.get("phone").trim(),
                password: data.get("password"),
                confirmPassword: data.get("confirmPassword")
            });
            closeDialog();
            await loadEmployees();
        } catch (error) {
            formMessage.textContent = error.message;
            formMessage.className = "form-message is-error";
        } finally {
            submit.disabled = false;
            submit.textContent = "Tạo nhân viên";
        }
    });

    document.querySelector("#addEmployeeButton").addEventListener("click", openDialog);
    document.querySelectorAll("[data-close-dialog]").forEach((button) => {
        button.addEventListener("click", closeDialog);
    });
    document.querySelector("#showEmployeePassword").addEventListener("change", (event) => {
        const type = event.target.checked ? "text" : "password";
        password.type = type;
        confirmPassword.type = type;
    });
    document.querySelector("#employeeSearch").addEventListener("input", (event) => {
        state.search = event.target.value.trim();
        renderList();
    });
    document.querySelector("#employeeRoleFilter").addEventListener("change", (event) => {
        state.role = event.target.value;
        renderList();
    });
    document.querySelector("#employeeStatusFilter").addEventListener("change", (event) => {
        state.status = event.target.value;
        renderList();
    });

    loadEmployees();
})();
