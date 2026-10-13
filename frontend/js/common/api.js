(function () {
    const configuredUrl = window.localStorage.getItem("restaurant.apiBaseUrl");
    const baseUrl = (configuredUrl || "http://localhost:5225/api").replace(/\/$/, "");

    async function request(path, options = {}) {
        const headers = new Headers(options.headers || {});
        if (options.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");

        let response;
        try {
            response = await fetch(`${baseUrl}${path}`, { ...options, headers });
        } catch (error) {
            throw new Error("Không kết nối được Backend. Hãy kiểm tra server và cấu hình CORS.");
        }

        const contentType = response.headers.get("content-type") || "";
        const payload = response.status === 204 ? null : contentType.includes("application/json")
            ? await response.json() : await response.text();

        if (!response.ok) {
            throw new Error(payload?.message || (typeof payload === "string" ? payload : "Yêu cầu không thành công."));
        }
        return payload;
    }

    window.restaurantApi = {
        baseUrl,
        get: (path) => request(path),
        post: (path, body) => request(path, { method: "POST", body: JSON.stringify(body) }),
        put: (path, body) => request(path, { method: "PUT", body: JSON.stringify(body) }),
        patch: (path, body) => request(path, { method: "PATCH", body: body === undefined ? undefined : JSON.stringify(body) }),
        delete: (path) => request(path, { method: "DELETE" })
    };
})();
