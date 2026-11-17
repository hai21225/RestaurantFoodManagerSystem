(function () {
    const RS = "\x1e";
    const MAX_BACKOFF_MS = 30000;
    const BASE_BACKOFF_MS = 1000;
    const auth = window.restaurantAuth;
    const api = window.restaurantApi;

    let socket = null;
    let handshakeCompleted = false;
    let buffer = "";
    let reconnectAttempts = 0;
    let reconnectTimer = null;
    let manualClose = false;
    const joinedOrders = new Set();

    function getWsUrl(token) {
        const base = (api?.baseUrl || "http://localhost:5225/api").replace(/\/api$/, "").replace(/^http/, "ws");
        return `${base}/hubs/restaurant?access_token=${encodeURIComponent(token)}`;
    }

    function sendRaw(frame) {
        if (!socket || socket.readyState !== 1) return false;
        try {
            socket.send(frame);
            return true;
        } catch (_) {
            return false;
        }
    }

    function invoke(target, ...args) {
        if (!handshakeCompleted || !target) return false;
        return sendRaw(JSON.stringify({ type: 1, target, arguments: args }) + RS);
    }

    function joinOrder(orderId) {
        if (orderId === undefined || orderId === null || orderId === "") return false;
        joinedOrders.add(orderId);
        return invoke("JoinOrder", orderId);
    }

    function leaveOrder(orderId) {
        if (orderId === undefined || orderId === null || orderId === "") return false;
        joinedOrders.delete(orderId);
        return invoke("LeaveOrder", orderId);
    }

    function scheduleReconnect() {
        if (reconnectTimer || manualClose || !auth?.getToken()) return;
        const delay = Math.min(MAX_BACKOFF_MS, BASE_BACKOFF_MS * Math.pow(2, reconnectAttempts));
        reconnectAttempts += 1;
        reconnectTimer = window.setTimeout(() => {
            reconnectTimer = null;
            connect();
        }, delay);
    }

    function handleFrame(frame) {
        if (!frame) return;
        let msg;
        try {
            msg = JSON.parse(frame);
        } catch (_) {
            return;
        }
        if (!handshakeCompleted) {
            handshakeCompleted = true;
            if (msg.error) {
                try { socket?.close(); } catch (_) { /* ignore */ }
                return;
            }
            reconnectAttempts = 0;
            for (const orderId of joinedOrders) {
                invoke("JoinOrder", orderId);
            }
            window.dispatchEvent(new CustomEvent("restaurant:realtime-connected"));
            return;
        }
        if (msg.type === 6) {
            sendRaw(JSON.stringify({ type: 6 }) + RS);
            return;
        }
        if (msg.type === 1 && msg.target) {
            window.dispatchEvent(new CustomEvent("restaurant:realtime", {
                detail: { event: msg.target, args: Array.isArray(msg.arguments) ? msg.arguments : [] }
            }));
            return;
        }
        if (msg.type === 7) {
            try { socket?.close(); } catch (_) { /* ignore */ }
        }
    }

    function connect() {
        if (typeof window.WebSocket !== "function") return;
        if (socket && (socket.readyState === 0 || socket.readyState === 1)) return;
        const token = auth?.getToken();
        if (!token) return;

        manualClose = false;
        if (reconnectTimer) {
            window.clearTimeout(reconnectTimer);
            reconnectTimer = null;
        }

        let ws;
        try {
            ws = new window.WebSocket(getWsUrl(token));
        } catch (_) {
            scheduleReconnect();
            return;
        }

        socket = ws;
        handshakeCompleted = false;
        buffer = "";

        ws.addEventListener("open", () => {
            if (socket !== ws) return;
            sendRaw(JSON.stringify({ protocol: "json", version: 1 }) + RS);
        });

        ws.addEventListener("message", (event) => {
            if (socket !== ws || typeof event.data !== "string") return;
            buffer += event.data;
            const frames = buffer.split(RS);
            buffer = frames.pop() ?? "";
            for (const frame of frames) {
                handleFrame(frame);
            }
        });

        ws.addEventListener("error", () => {
            // Không ném lỗi để không làm gián đoạn màn hình vận hành khi offline.
        });

        ws.addEventListener("close", () => {
            if (socket === ws) {
                socket = null;
                handshakeCompleted = false;
            }
            if (!manualClose && auth?.getToken()) {
                scheduleReconnect();
            }
        });
    }

    function disconnect() {
        manualClose = true;
        if (reconnectTimer) {
            window.clearTimeout(reconnectTimer);
            reconnectTimer = null;
        }
        const active = socket;
        socket = null;
        handshakeCompleted = false;
        buffer = "";
        if (active) {
            try { active.close(); } catch (_) { /* ignore */ }
        }
    }

    function on(eventName, handler) {
        if (typeof handler !== "function") return () => {};
        const listener = (event) => {
            const detail = event.detail;
            if (!detail) return;
            if (eventName === "*" || detail.event === eventName) {
                handler(...(detail.args || []), detail);
            }
        };
        window.addEventListener("restaurant:realtime", listener);
        return () => window.removeEventListener("restaurant:realtime", listener);
    }

    window.restaurantRealtime = {
        connect,
        disconnect,
        invoke,
        joinOrder,
        leaveOrder,
        on,
        isConnected: () => Boolean(socket && socket.readyState === 1 && handshakeCompleted)
    };

    window.addEventListener("restaurant:auth-changed", () => {
        if (auth?.getToken()) {
            connect();
        } else {
            disconnect();
        }
    });

    if (auth?.getToken() && !auth?.isPublicPage?.()) {
        if (auth.ready && typeof auth.ready.then === "function") {
            auth.ready.then(() => {
                if (auth.getToken()) connect();
            });
        } else {
            connect();
        }
    }
})();
