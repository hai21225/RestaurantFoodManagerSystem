const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

const RS = '\x1e';

function loadRealtime({ token = 'jwt-token', baseUrl = 'http://localhost:5225/api', hasWebSocket = true, throwOnConnect = false } = {}) {
    const sockets = [];
    const timers = [];
    const events = new EventTarget();
    let currentToken = token;

    class MockWebSocket {
        constructor(url) {
            if (throwOnConnect) throw new Error('ws blocked');
            this.url = url;
            this.readyState = 0;
            this.sent = [];
            this.listeners = new Map();
            sockets.push(this);
        }
        addEventListener(type, fn) {
            if (!this.listeners.has(type)) this.listeners.set(type, []);
            this.listeners.get(type).push(fn);
        }
        send(data) { this.sent.push(data); }
        close() {
            this.readyState = 3;
            this.emit('close', {});
        }
        emit(type, payload = {}) {
            if (type === 'open') this.readyState = 1;
            for (const fn of this.listeners.get(type) || []) fn(payload);
        }
    }

    const window = {
        WebSocket: hasWebSocket ? MockWebSocket : undefined,
        restaurantAuth: {
            getToken: () => currentToken,
            isPublicPage: () => false,
            ready: Promise.resolve(null)
        },
        restaurantApi: { baseUrl },
        setTimeout: (fn, delay) => {
            const id = timers.length + 1;
            timers.push({ id, fn, delay, cleared: false });
            return id;
        },
        clearTimeout: (id) => {
            const t = timers.find((item) => item.id === id);
            if (t) t.cleared = true;
        },
        addEventListener: events.addEventListener.bind(events),
        removeEventListener: events.removeEventListener.bind(events),
        dispatchEvent: events.dispatchEvent.bind(events)
    };

    const context = vm.createContext({ window, CustomEvent });
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../js/common/realtime.js'), 'utf8'), context, { filename: 'realtime.js' });

    return {
        realtime: window.restaurantRealtime,
        sockets,
        timers,
        window,
        setToken: (val) => { currentToken = val; }
    };
}

test('realtime: handshake, ping reply, JoinOrder/LeaveOrder và dispatch sự kiện', async () => {
    const env = loadRealtime();
    await Promise.resolve();
    assert.equal(env.sockets.length, 1);
    const ws = env.sockets[0];
    assert.equal(ws.url, 'ws://localhost:5225/hubs/restaurant?access_token=jwt-token');

    ws.emit('open');
    assert.equal(ws.sent[0], JSON.stringify({ protocol: 'json', version: 1 }) + RS);

    ws.emit('message', { data: '{}' + RS });
    assert.equal(env.realtime.isConnected(), true);

    env.realtime.joinOrder(42);
    assert.equal(ws.sent[1], JSON.stringify({ type: 1, target: 'JoinOrder', arguments: [42] }) + RS);

    env.realtime.leaveOrder(42);
    assert.equal(ws.sent[2], JSON.stringify({ type: 1, target: 'LeaveOrder', arguments: [42] }) + RS);

    ws.emit('message', { data: JSON.stringify({ type: 6 }) + RS });
    assert.equal(ws.sent[3], JSON.stringify({ type: 6 }) + RS);

    const received = [];
    const off = env.realtime.on('OrderCreated', (arg1) => received.push(arg1));
    const domEvents = [];
    env.window.addEventListener('restaurant:realtime', (e) => domEvents.push(e.detail));

    ws.emit('message', { data: JSON.stringify({ type: 1, target: 'OrderCreated', arguments: [{ orderId: 42 }] }) + RS });
    assert.deepEqual(JSON.parse(JSON.stringify(received)), [{ orderId: 42 }]);
    assert.deepEqual(JSON.parse(JSON.stringify(domEvents)), [{ event: 'OrderCreated', args: [{ orderId: 42 }] }]);

    off();
    ws.emit('message', { data: JSON.stringify({ type: 1, target: 'OrderCreated', arguments: [{ orderId: 43 }] }) + RS });
    assert.equal(received.length, 1);
});

test('realtime: reconnect exponential backoff khi đóng kết nối và dừng khi mất token', async () => {
    const env = loadRealtime();
    await Promise.resolve();
    const ws1 = env.sockets[0];
    ws1.emit('open');
    ws1.emit('message', { data: '{}' + RS });

    ws1.close();
    assert.equal(env.timers.length, 1);
    assert.equal(env.timers[0].delay, 1000);
    env.timers[0].fn();

    assert.equal(env.sockets.length, 2);
    env.sockets[1].close();
    assert.equal(env.timers[1].delay, 2000);

    env.setToken(null);
    env.window.dispatchEvent(new CustomEvent('restaurant:auth-changed'));
    assert.equal(env.timers[1].cleared, true);
});

test('realtime: không ném lỗi khi thiếu WebSocket hoặc constructor thất bại', async () => {
    const noWs = loadRealtime({ hasWebSocket: false });
    await Promise.resolve();
    assert.equal(noWs.realtime.isConnected(), false);
    assert.equal(noWs.realtime.joinOrder(1), false);

    const blocked = loadRealtime({ throwOnConnect: true });
    await Promise.resolve();
    assert.equal(blocked.realtime.isConnected(), false);
    assert.equal(blocked.timers.length, 1);
});
