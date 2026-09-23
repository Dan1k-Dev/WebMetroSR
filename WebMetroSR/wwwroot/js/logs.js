// Логи
let currentLogRequest = null;
let currentNetRequest = null;

async function loadLog(endpoint, fileName, hostname, ip) {
    const logsSection = document.getElementById('logsDataSection');
    if (!logsSection) return;

    let logDisplayArea = document.getElementById('logDisplayArea');
    if (!logDisplayArea) {
        const html = `
            <div id="logDisplayArea">
                <div class="log-header">
                    <h3 id="logFileName"></h3>
                    <button class="tag-btn" id="clearLogBtn" style="font-size: 11px;">Очистить</button>
                </div>
                <div id="logContent" class="log-content"></div>
            </div>
        `;
        logsSection.insertAdjacentHTML('beforeend', html);
        logDisplayArea = document.getElementById('logDisplayArea');
        const clearBtn = document.getElementById('clearLogBtn');
        if (clearBtn) {
            clearBtn.onclick = () => {
                const area = document.getElementById('logDisplayArea');
                const content = document.getElementById('logContent');
                if (area) area.style.display = 'none';
                if (content) content.innerHTML = '';
            };
        }
    }

    if (logDisplayArea) logDisplayArea.style.display = 'block';
    const logFileNameSpan = document.getElementById('logFileName');
    if (logFileNameSpan) logFileNameSpan.textContent = `📄 ${fileName} (${hostname})`;
    const logContentDiv = document.getElementById('logContent');
    if (logContentDiv) logContentDiv.innerHTML = '<div class="loading-text" style="padding:20px; text-align:center;">Загрузка лога...</div>';

    if (currentLogRequest) currentLogRequest.abort();
    currentLogRequest = new AbortController();

    try {
        const text = await fetchKixtartLog(hostname, ip, currentLogRequest.signal);
        if (logContentDiv) logContentDiv.innerHTML = `<pre>${escapeHtml(text)}</pre>`;
    } catch (err) {
        if (err.name === 'AbortError') return;
        console.error(err);
        if (logContentDiv) logContentDiv.innerHTML = `<div class="log-error">❌ Ошибка: ${escapeHtml(err.message)}</div>`;
    }
}

async function loadKixtartLog() {
    const state = store.getState();
    const hostname = state.currentHostname;
    const ip = state.currentIp;
    if (!hostname || hostname === '—' || hostname === 'Запрос...') {
        alert('Имя хоста не загружено');
        return;
    }
    if (!isValidIpClient(ip)) {
        alert('IP-адрес не загружен или некорректен');
        return;
    }
    await loadLog('read-kixtart-log', 'kixstart.log', hostname, ip);
}

// ---------- НОВЫЕ СЕТЕВЫЕ КОМАНДЫ ----------

function isValidIpClient(ip) {
    return ip && ip !== '—' && ip !== 'Offline' && ip !== 'Запрос...' && !ip.includes('Ошибка');
}

function ensureNetOutputArea() {
    let area = document.getElementById('netOutputArea');
    const logsSection = document.getElementById('logsDataSection');
    if (!logsSection) return null;

    if (!area) {
        const html = `
            <div id="netOutputArea" style="margin-top: 20px;">
                <div class="log-header">
                    <h3 id="netOutputTitle"></h3>
                    <button class="tag-btn" id="clearNetBtn" style="font-size: 11px;">Очистить</button>
                </div>
                <div id="netOutputContent" class="log-content"></div>
            </div>
        `;
        logsSection.insertAdjacentHTML('beforeend', html);
        area = document.getElementById('netOutputArea');
        const clearBtn = document.getElementById('clearNetBtn');
        if (clearBtn) {
            clearBtn.onclick = () => {
                const content = document.getElementById('netOutputContent');
                if (content) content.innerHTML = '';
                const a = document.getElementById('netOutputArea');
                if (a) a.style.display = 'none';
            };
        }
    }
    return area;
}

async function runNetCommand(title, loader) {
    const state = store.getState();
    const hostname = state.currentHostname;
    const ip = state.currentIp;

    if (!hostname || hostname === '—' || hostname === 'Запрос...') {
        alert('Имя хоста не загружено');
        return;
    }
    if (!isValidIpClient(ip)) {
        alert('IP-адрес не загружен или некорректен');
        return;
    }

    const area = ensureNetOutputArea();
    if (!area) return;
    area.style.display = 'block';
    document.getElementById('netOutputTitle').textContent = `🌐 ${title} (${hostname} / ${ip})`;
    const content = document.getElementById('netOutputContent');
    content.innerHTML = '<div class="loading-text" style="padding:20px; text-align:center;">Выполняется запрос...</div>';

    if (currentNetRequest) currentNetRequest.abort();
    currentNetRequest = new AbortController();

    try {
        const result = await loader(hostname, ip, currentNetRequest.signal);
        if (result.success) {
            content.innerHTML = `<pre>${escapeHtml(result.output || '(пусто)')}</pre>`;
        } else {
            content.innerHTML = `<div class="log-error">❌ Ошибка: ${escapeHtml(result.output || 'Неизвестная ошибка')}</div>`;
        }
    } catch (err) {
        if (err.name === 'AbortError') return;
        console.error(err);
        content.innerHTML = `<div class="log-error">❌ Ошибка: ${escapeHtml(err.message)}</div>`;
    }
}

async function loadRoute() { await runNetCommand('Маршруты (route)', (h, i, s) => fetchRoute(h, i, s)); }
async function loadTraceroute() { await runNetCommand('Трассировка (traceroute)', (h, i, s) => fetchTraceroute(i, s)); }
async function loadIpConfig() { await runNetCommand('ipconfig', (h, i, s) => fetchIpConfig(h, i, s)); }

window.loadKixtartLog = loadKixtartLog;
window.loadRoute = loadRoute;
window.loadTraceroute = loadTraceroute;
window.loadIpConfig = loadIpConfig;