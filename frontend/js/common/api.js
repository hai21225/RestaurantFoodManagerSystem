(function () {
    const configuredUrl = window.localStorage.getItem("restaurant.apiBaseUrl");
    const baseUrl = (configuredUrl || "http://localhost:5225/api").replace(/\/+$/, "");
    const auth = window.restaurantAuth;

    class ApiError extends Error {
        constructor(message, status, payload) {
            super(message);
            this.name = "RestaurantApiError";
            this.status = status;
            this.payload = payload;
        }
    }

    function anonymous(path, method) {
        const route = path.split("?")[0].replace(/\/$/, "");
        return (method === "POST" && ["/auth/login", "/setup/first-admin"].includes(route))
            || (method === "GET" && route === "/setup/status");
    }

    function errorMessage(status, payload) {
        if (status >= 500) return "Máy chủ gặp lỗi. Vui lòng thử lại hoặc báo quản trị viên.";
        if (payload?.message) return payload.message;
        if (status === 401) return "Phiên đăng nhập không hợp lệ hoặc đã hết hạn. Vui lòng đăng nhập lại.";
        if (status === 403) return "Bạn không đủ quyền thực hiện thao tác này.";
        if (payload?.errors) return Object.values(payload.errors).flat().join(" ");
        if (typeof payload === "string" && payload && !payload.includes("<")) return payload;
        return payload?.title || "Yêu cầu không thành công.";
    }

    async function readPayload(response) {
        if (response.status === 204) return null;
        const contentType = response.headers.get("content-type") || "";
        return contentType.includes("json") ? response.json() : response.text();
    }

    async function request(path, options = {}, asBlob = false) {
        // Path chỉ thuộc API đang cấu hình, không nhận URL của máy chủ khác.
        if (!path.startsWith("/") || path.startsWith("//") || path.includes("\\") || path.split("/").includes("..")) {
            throw new ApiError("Đường dẫn API không hợp lệ.", 0, null);
        }
        const method = (options.method || "GET").toUpperCase();
        const isAnonymous = anonymous(path, method);
        const requestToken = auth.getToken();
        const headers = new Headers(options.headers || {});
        if (options.body !== undefined && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");
        if (!isAnonymous && requestToken) headers.set("Authorization", `Bearer ${requestToken}`);
        if (!isAnonymous && !requestToken) {
            auth.clearSession();
            auth.redirectToLogin();
            throw new ApiError(errorMessage(401), 401, null);
        }

        let response;
        try {
            response = await fetch(`${baseUrl}${path}`, { ...options, method, headers });
        } catch (error) {
            if (error.name === "AbortError") throw error;
            throw new ApiError("Không kết nối được Backend. Hãy kiểm tra server và cấu hình CORS.", 0, null);
        }

        // Kể cả body lỗi rỗng/hỏng, 401 vẫn phải xoá đúng phiên đã bị từ chối.
        if (response.status === 401 && !isAnonymous && auth.getToken() === requestToken) {
            auth.clearSession();
            auth.redirectToLogin();
        }
        if (!response.ok) {
            let payload = null;
            try { payload = await readPayload(response); } catch (_) { /* Dùng thông báo theo HTTP status. */ }
            throw new ApiError(errorMessage(response.status, payload), response.status, payload);
        }
        if (asBlob) return { blob: await response.blob(), headers: response.headers };
        return readPayload(response);
    }

    async function download(path, filename, options = {}) {
        const { blob, headers } = await request(path, options, true);
        const disposition = headers.get("content-disposition") || "";
        const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
        const plain = /filename="?([^";]+)"?/i.exec(disposition);
        let name = filename || plain?.[1] || "bao-cao.csv";
        if (!filename && encoded) {
            try { name = decodeURIComponent(encoded[1]); } catch (_) { /* Giữ tên dự phòng. */ }
        }
        name = name.replace(/[\\/\x00-\x1f]/g, "_");
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = name;
        document.body.appendChild(link);
        link.click();
        link.remove();
        window.setTimeout(() => URL.revokeObjectURL(url), 1000);
        return name;
    }

    const write = (method) => (path, body, options = {}) => request(path, {
        ...options, method, body: body === undefined ? undefined : JSON.stringify(body)
    });
    window.restaurantApi = {
        baseUrl, ApiError, download,
        get: (path, options = {}) => request(path, { ...options, method: "GET" }),
        post: write("POST"), put: write("PUT"), patch: write("PATCH"),
        delete: (path, options = {}) => request(path, { ...options, method: "DELETE" })
    };

    // Lấy hồ sơ thật khi mở trang; backend vẫn quyết định mọi quyền truy cập.
    const bootstrapToken = auth.getToken();
    auth.ready = auth.isPublicPage() || !bootstrapToken ? Promise.resolve(null)
        : window.restaurantApi.get("/users/me").then((user) => {
            if (auth.getToken() === bootstrapToken) auth.updateUser(user);
            return user;
        }).catch((error) => {
            if (error.status !== 401) window.dispatchEvent(new CustomEvent("restaurant:auth-error", { detail: error.message }));
            return null;
        });
})();
