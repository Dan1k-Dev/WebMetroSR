// main.js - Основная логика с кэшированием, без LAPS на клиенте

let currentAbortController = null;
let highlightTimeouts = new Map();
let isLoading = false;

const dom = {
    hostname: document.getElementById('display-hostname'),
    ip: document.getElementById('ip-addr'),
    user: document.getElementById('current-user'),
    lastBoot: document.getElementById('last-boot'),
    switchPort: document.getElementById('switch-port'),
    pcModel: document.getElementById('pc-model'),
    pcSerial: document.getElementById('pc-serial'),
    biosVersion: document.getElementById('bios-version'),
    osInfo: document.getElementById('os-info'),
    macAddr: document.getElementById('mac-addr'),
    netType: document.getElementById('net-type'),
    hddTotal: document.getElementById('hdd-total'),
    hddFree: document.getElementById('hdd-free'),
    ramInfo: document.getElementById('ram-info'),
    objectType: document.getElementById('object-type')
};

const resetFields = ['ip', 'user', 'lastBoot', 'switchPort', 'pcModel',
    'pcSerial', 'biosVersion', 'osInfo', 'macAddr', 'netType', 'hddTotal', 'hddFree', 'ramInfo'];

// ---- Вспомогательные функции для updateHostInfo ----

function endBatchSafe() {
    if (typeof endBatch === 'function') endBatch();
}

function resetHostInfoFields(name, el) {
    document.querySelectorAll('.host-item').forEach(i => i.classList.remove('active-host', 'found-highlight'));
    if (el) {
        el.classList.add('active-host');
    } else {
        const found = Array.from(document.querySelectorAll('.host-item')).find(i => i.textContent === name);
        if (found) found.classList.add('active-host');
    }
    store.setState({ currentHostname: name });
    dom.hostname.textContent = name;
    dom.hostname.classList.remove('loading-text');
    resetFields.forEach(field => {
        if (dom[field]) {
            dom[field].textContent = 'Запрос...';
            dom[field].classList.add('loading-text');
        }
    });
    dom.objectType.textContent = '—';
    store.setState({ isSystemInfoLoaded: false, currentUser: null });
    updateButtonsAndTabsState();
}

function handleActiveCheckFailed(name, el) {
    alert(`Хост ${name} не активен в SCCM. Информация недоступна.`);
    document.querySelectorAll('.host-item').forEach(i => i.classList.remove('active-host', 'found-highlight'));
    if (el) el.classList.add('active-host');
    dom.hostname.textContent = name;
    resetFields.forEach(field => { if (dom[field]) dom[field].textContent = '—'; });
    dom.objectType.textContent = '—';
    store.setState({
        currentHostname: name,
        currentIp: null,
        currentUser: null,
        isValidIp: false,
        isSystemInfoLoaded: false
    });
    updateButtonsAndTabsState();
}

function handleHostOffline(name, el) {
    // Уведомление
    alert(`Хост ${name} оффлайн. Информация недоступна.`);

    document.querySelectorAll('.host-item').forEach(i => i.classList.remove('active-host', 'found-highlight'));
    if (el) el.classList.add('active-host');
    dom.hostname.textContent = name;
    dom.hostname.classList.remove('loading-text');

    resetFields.forEach(field => {
        if (dom[field]) {
            dom[field].textContent = 'Оффлайн';
            dom[field].classList.remove('loading-text');
        }
    });
    dom.objectType.textContent = 'Оффлайн';
    dom.objectType.classList.remove('loading-text');

    store.setState({
        currentHostname: name,
        currentIp: null,
        currentUser: null,
        isValidIp: false,
        isSystemInfoLoaded: false
    });
    updateButtonsAndTabsState();
}

function processBasicInfo(data, name) {
    const ip = data.ip || "—";
    dom.ip.textContent = ip;
    dom.objectType.textContent = name.includes("LAP") ? "Ноутбук" : "Толстый клиент";
    dom.ip.classList.remove('loading-text');
    dom.objectType.classList.remove('loading-text');
    store.setState({
        currentHostname: name,
        currentIp: ip,
        isValidIp: (ip && ip !== '—' && ip !== 'Offline')
    });
    return ip;
}

// ---- Запуск WMI-запросов с обновлением по мере поступления ----
function startWmiRequests(name, ip, signal, skip = {}) {
    const requestEntries = [
        { key: 'user', promise: fetchCurrentUser(name, ip, signal) },
        { key: 'port', promise: fetchSwitchPort(ip, signal) },
        { key: 'mac', promise: fetchMac(name, ip, signal) },
        { key: 'model', promise: fetchModel(name, ip, signal) },
        { key: 'biosVersion', promise: fetchBiosVersion(name, ip, signal) },
        { key: 'os', promise: fetchOs(name, ip, signal) },
        { key: 'lastboot', promise: fetchLastBoot(name, ip, signal) },
        { key: 'disk', promise: fetchDisk(name, ip, signal) },
        { key: 'ram', promise: fetchRam(name, ip, signal) },
        { key: 'netType', promise: fetchNetType(name, ip, signal) }
    ];

    // S/N запрашиваем только если он не был предзаполнен из поиска
    if (!skip.serial) {
        requestEntries.push({ key: 'serial', promise: fetchSerial(name, ip, signal) });
    }

    const promises = requestEntries.map(({ key, promise }) => {
        return promise
            .then(data => {
                switch (key) {
                    case 'user':
                        dom.user.textContent = data?.user || "Unknown";
                        dom.user.classList.remove('loading-text');
                        store.setState({ currentUser: data?.user || "Unknown" });
                        updateButtonsAndTabsState();
                        break;
                    case 'port':
                        dom.switchPort.textContent = data?.port || "Не найден";
                        dom.switchPort.classList.remove('loading-text');
                        break;
                    case 'mac':
                        dom.macAddr.textContent = data?.mac || "—";
                        dom.macAddr.classList.remove('loading-text');
                        break;
                    case 'model':
                        dom.pcModel.textContent = data?.model || "—";
                        dom.pcModel.classList.remove('loading-text');
                        break;
                    case 'serial':
                        dom.pcSerial.textContent = data?.serial || "—";
                        dom.pcSerial.classList.remove('loading-text');
                        break;
                    case 'biosVersion':
                        dom.biosVersion.textContent = data?.biosVersion || "—";
                        dom.biosVersion.classList.remove('loading-text');
                        break;
                    case 'os':
                        dom.osInfo.textContent = data?.os || "—";
                        dom.osInfo.classList.remove('loading-text');
                        break;
                    case 'lastboot':
                        dom.lastBoot.textContent = data?.lastBoot || "—";
                        dom.lastBoot.classList.remove('loading-text');
                        break;
                    case 'disk':
                        dom.hddTotal.textContent = data?.hddTotal || "—";
                        dom.hddFree.textContent = data?.hddFree || "—";
                        dom.hddTotal.classList.remove('loading-text');
                        dom.hddFree.classList.remove('loading-text');
                        break;
                    case 'ram':
                        dom.ramInfo.textContent = data?.ram || "—";
                        dom.ramInfo.classList.remove('loading-text');
                        break;
                    case 'netType':
                        dom.netType.textContent = data?.netType || "—";
                        dom.netType.classList.remove('loading-text');
                        break;
                }
            })
            .catch(error => {
                switch (key) {
                    case 'user':
                        dom.user.textContent = "Unknown";
                        dom.user.classList.remove('loading-text');
                        store.setState({ currentUser: "Unknown" });
                        updateButtonsAndTabsState();
                        break;
                    case 'port':
                        dom.switchPort.textContent = "Ошибка";
                        dom.switchPort.classList.remove('loading-text');
                        break;
                    case 'mac':
                        dom.macAddr.textContent = "—";
                        dom.macAddr.classList.remove('loading-text');
                        break;
                    case 'model':
                        dom.pcModel.textContent = "—";
                        dom.pcModel.classList.remove('loading-text');
                        break;
                    case 'serial':
                        dom.pcSerial.textContent = "—";
                        dom.pcSerial.classList.remove('loading-text');
                        break;
                    case 'os':
                        dom.osInfo.textContent = "—";
                        dom.osInfo.classList.remove('loading-text');
                        break;
                    case 'lastboot':
                        dom.lastBoot.textContent = "—";
                        dom.lastBoot.classList.remove('loading-text');
                        break;
                    case 'disk':
                        dom.hddTotal.textContent = "—";
                        dom.hddFree.textContent = "—";
                        dom.hddTotal.classList.remove('loading-text');
                        dom.hddFree.classList.remove('loading-text');
                        break;
                    case 'ram':
                        dom.ramInfo.textContent = "—";
                        dom.ramInfo.classList.remove('loading-text');
                        break;
                    case 'netType':
                        dom.netType.textContent = "—";
                        dom.netType.classList.remove('loading-text');
                        break;
                }
            });
    });

// Когда все WMI-запросы завершены — закрываем batch
    return Promise.allSettled(promises).finally(() => endBatchSafe());
}

// ---- Основная функция обновления информации о хосте ----
async function updateHostInfo(name, el, forceRefresh = false, prefill = null) {
    if (isLoading) {
        return;
    }
    isLoading = true;

    if (el && highlightTimeouts.has(el)) {
        clearTimeout(highlightTimeouts.get(el));
        highlightTimeouts.delete(el);
        el.classList.remove('found-highlight');
    }

    if (currentAbortController) currentAbortController.abort();
    currentAbortController = new AbortController();
    const signal = currentAbortController.signal;

    if (typeof startBatch === 'function') startBatch(name);

    resetHostInfoFields(name, el);

    if (prefill && prefill.serial) {
        dom.pcSerial.textContent = prefill.serial;
        dom.pcSerial.classList.remove('loading-text');
    }

    // -------- SCCM active check --------
    let notification = null;
    try {
        notification = document.createElement('div');
        notification.textContent = '⏳ Проверка активности хоста в SCCM...';
        notification.style.cssText = 'position:fixed;bottom:20px;right:20px;background:#333;color:#fff;padding:10px 15px;border-radius:8px;z-index:10000;font-size:14px;box-shadow:0 2px 10px rgba(0,0,0,0.2)';
        document.body.appendChild(notification);

        const activeCheck = await checkClientActive(name, signal, forceRefresh);
        if (!activeCheck.isActive) {
            handleActiveCheckFailed(name, el);
            if (notification) notification.remove();
            endBatchSafe();
            isLoading = false;   // ← СБРОС
            return;
        }
    } catch (err) {
    } finally {
        if (notification) notification.remove();
    }

    // -------- Get IP --------
    let ip = null;
    try {
        if (prefill && prefill.ip) {
            ip = prefill.ip;
            dom.ip.textContent = ip;
            dom.ip.classList.remove('loading-text');
            dom.objectType.textContent = name.includes("LAP") ? "Ноутбук" : "Толстый клиент";
            dom.objectType.classList.remove('loading-text');
            store.setState({
                currentHostname: name,
                currentIp: ip,
                isValidIp: (ip && ip !== '—' && ip !== 'Offline')
            });
        } else {
            const basicData = await fetchBasicInfo(name, signal, forceRefresh);
            if (basicData.success) {
                ip = processBasicInfo(basicData, name);
            }
        }
    } catch (err) {
        if (err.name === 'AbortError') {
            endBatchSafe();
            isLoading = false;   // ← СБРОС
            return;
        }
        console.error(err);
        endBatchSafe();
        isLoading = false;   // ← СБРОС
        return;
    }

    // -------- Online check + WMI --------
    const ipIsValid = ip && ip !== '—' && ip !== 'Offline' && ip !== '0.0.0.0';

    if (ipIsValid) {
        //  ПРОВЕРКА ОНЛАЙНА через порт 445
        try {
            const onlineCheck = await checkHostOnline(ip, signal);
            if (!onlineCheck.online) {
                handleHostOffline(name, el);
                endBatchSafe();
                isLoading = false;   // ← СБРОС
                return;
            }
        } catch (err) {
            // Если проверка упала — считаем оффлайн
            console.error('Online check failed');
            handleHostOffline(name, el);
            endBatchSafe();
            isLoading = false;
            return;
        }

        // Хост онлайн — запускаем WMI
        void startWmiRequests(name, ip, signal, { serial: !!(prefill && prefill.serial) });
    } else {
        // IP не получен — сразу оффлайн
        handleHostOffline(name, el);
        endBatchSafe();
        isLoading = false;   // ← СБРОС
        return;
    }

    store.setState({ isSystemInfoLoaded: true });
    updateButtonsAndTabsState();

    setTimeout(() => {
        isLoading = false;
    }, 500);
}

// ---- Построение панелей логов и функций ----
function buildLogsPane() {
    const logsSection = document.getElementById('logsDataSection');
    if (!logsSection) return;
    logsSection.innerHTML = `
        <div class="grid-layout">
            <button class="btn-act" id="kixtartLogBtn">kixstart.log</button>
            <button class="btn-act" id="routeBtn">Маршруты (route)</button>
            <button class="btn-act" id="tracerouteBtn">Трассировка (traceroute)</button>
            <button class="btn-act" id="ipconfigBtn">ipconfig</button>
            <button class="btn-act" id="softwareBtn">Установленное ПО</button>
            <button class="btn-act" id="devicesBtn">Список устройств</button>
            <button class="btn-act" id="insightBtn">Insight Card</button>
        </div>
    `;

    const kixtartBtn = document.getElementById('kixtartLogBtn');
    const routeBtn = document.getElementById('routeBtn');
    const tracerouteBtn = document.getElementById('tracerouteBtn');
    const ipconfigBtn = document.getElementById('ipconfigBtn');
    const softwareBtn = document.getElementById('softwareBtn');
    const devicesBtn = document.getElementById('devicesBtn');
    const insightBtn = document.getElementById('insightBtn');

    if (kixtartBtn) kixtartBtn.onclick = loadKixtartLog;
    if (routeBtn) routeBtn.onclick = loadRoute;
    if (tracerouteBtn) tracerouteBtn.onclick = loadTraceroute;
    if (ipconfigBtn) ipconfigBtn.onclick = loadIpConfig;
    if (softwareBtn) softwareBtn.onclick = () => { void loadInstalledSoftware(); };
    if (devicesBtn) devicesBtn.onclick = () => { void loadDevices(); };
    if (insightBtn) insightBtn.onclick = openInsightCard;   // <-- NEW
}

function buildFuncsPane() {
    const funcsSection = document.querySelector('#funcs .data-section');
    if (!funcsSection) return;
    funcsSection.innerHTML = `
        <div class="grid-layout">
            <button class="btn-act func-btn" id="rdpBtn" data-func="rdp" disabled>RDP</button>
            <button class="btn-act func-btn" data-func="share">Open Share</button>
            <button class="btn-act func-btn" data-func="msi">Очистка MSI/MSP</button>
            <button class="btn-act func-btn" data-func="printer">Принтеры на ПК</button>
        </div>
    `;

    document.querySelectorAll('.func-btn').forEach(btn => {
        btn.onclick = () => handleFuncButton(btn.dataset.func);
    });

    const rdpBtn = document.getElementById('rdpBtn');
    if (rdpBtn) rdpBtn.disabled = !store.getState().isValidIp;

    const oldPanel = document.getElementById('printerPanel');
    if (oldPanel) oldPanel.remove();
}

// ---- Управление состоянием кнопок и вкладок ----
function updateButtonsAndTabsState() {
    const state = store.getState();
    const isValidIp = state.isValidIp;
    const isSystemInfoLoaded = state.isSystemInfoLoaded;
    const currentUser = state.currentUser;

    const isUserLoaded = currentUser &&
        currentUser !== '—' &&
        currentUser !== 'Запрос...' &&
        currentUser !== 'Unknown' &&
        currentUser !== 'N/A' &&
        currentUser !== 'Ошибка';

    const groupsBtn = document.getElementById('groupsBtn');
    const rdpBtn = document.getElementById('rdpBtn');

    if (rdpBtn) rdpBtn.disabled = !isValidIp;
    if (groupsBtn) groupsBtn.disabled = !isUserLoaded;

    const tabs = document.querySelectorAll('.tab');
    const logsTab = tabs[1];
    const funcsTab = tabs[2];

    if (isSystemInfoLoaded) {
        if (logsTab) {
            logsTab.classList.remove('disabled');
            logsTab.removeAttribute('title');
        }
        if (funcsTab) {
            funcsTab.classList.remove('disabled');
            funcsTab.removeAttribute('title');
        }
        const logsDataSection = document.getElementById('logsDataSection');
        const funcsDataSection = document.querySelector('#funcs .data-section');
        if (logsDataSection && logsDataSection.innerHTML.includes('disabled-pane-message')) {
            buildLogsPane();
        }
        if (funcsDataSection && funcsDataSection.innerHTML.includes('disabled-pane-message')) {
            buildFuncsPane();
        }
    } else {
        if (logsTab) {
            logsTab.classList.add('disabled');
            logsTab.setAttribute('title', 'Ожидание загрузки системных данных');
        }
        if (funcsTab) {
            funcsTab.classList.add('disabled');
            funcsTab.setAttribute('title', 'Ожидание загрузки системных данных');
        }
        const logsDataSection = document.getElementById('logsDataSection');
        const funcsDataSection = document.querySelector('#funcs .data-section');
        if (logsDataSection && !logsDataSection.innerHTML.includes('disabled-pane-message')) {
            logsDataSection.innerHTML = '<div class="disabled-pane-message">⚠️ Для доступа к логам дождитесь загрузки системной информации</div>';
        }
        if (funcsDataSection && !funcsDataSection.innerHTML.includes('disabled-pane-message')) {
            funcsDataSection.innerHTML = '<div class="disabled-pane-message">⚠️ Для доступа к функциям дождитесь загрузки системной информации</div>';
        }
    }
}

function switchToTab(tabId) {
    document.querySelectorAll('.pane').forEach(p => p.classList.remove('active'));
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    const activePane = document.getElementById(tabId);
    if (activePane) activePane.classList.add('active');
    const activeTab = Array.from(document.querySelectorAll('.tab')).find(t => t.dataset.tab === tabId);
    if (activeTab) activeTab.classList.add('active');
}

// ---- Валидация IP ----
function isValidIPv4(ip) {
    const ipv4Regex = /^(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)$/;
    return ipv4Regex.test(ip);
}

// ---- Вспомогательные функции для поиска ----
function validateSearchInput(val) {
    if (!val) return false;

    // Длина: от 6 до 16
    if (val.length < 6 || val.length > 16) {
        alert('Невомзожное значение Lenght для введенных данных');
        return false;
    }

    if (!/^[A-Za-z0-9.]+$/.test(val)) {
        alert('Hostname, IP или S/N содержат недопустимые символы');
        return false;
    }

    // Проверка формата IPv4, если ввод похож на IP
    if (val.includes('.') && !val.includes('-')
        && !Number.isNaN(Number(val.split('.')[0]))
        && !isValidIPv4(val)) {
        alert('Некорректный формат IPv4 адреса');
        return false;
    }

    return true;
}

function getContainerByDn(dn) {
    return Array.from(document.querySelectorAll('details')).find(d => d.dataset.dn === dn);
}

function findHostInContainer(container, hostName) {
    return Array.from(container.querySelectorAll('.host-item')).find(h => h.textContent === hostName);
}

async function navigateToHostInTree(hostName, containerDn, prefill = null) {
    if (typeof containerDn !== 'string') {
        console.error('Invalid containerDn type');
        return false;
    }
    document.querySelectorAll('details').forEach(d => d.open = false);
    const det = getContainerByDn(containerDn);
    if (!det) return false;
    det.open = true;
    if (det.dataset.loaded !== "true") await loadHosts(containerDn, det);
    document.querySelectorAll('.host-item').forEach(i => i.classList.remove('found-highlight'));
    const item = findHostInContainer(det, hostName);
    if (!item) return false;

    if (highlightTimeouts.has(item)) {
        clearTimeout(highlightTimeouts.get(item));
        highlightTimeouts.delete(item);
    }
    item.classList.remove('found-highlight');
    if (typeof updateHostInfo === 'function') void updateHostInfo(hostName, item, false, prefill);
    item.scrollIntoView({ behavior: 'smooth', block: 'center' });
    return true;
}

async function performSearch(val, searchInput, searchContainer) {
    searchInput.disabled = true;
    if (searchContainer) searchContainer.classList.add('searching');
    try {
        const isIp = isValidIPv4(val);

        // 1) Обычный поиск (hostname или IP)
        const result = await findHost(val);
        if (result.success) {
            const hostName = (result.hostName || val).toUpperCase();
            const targetDn = result.containerDn || result.containerDN;

            // Если искали по IP — сразу подставим его, чтобы не делать повторный запрос
            const prefill = isIp ? { ip: val } : null;

            const ok = await navigateToHostInTree(hostName, targetDn, prefill);
            if (!ok) alert(`Контейнер или хост ${hostName} не найдены в дереве`);
            return;
        }

        // 2) Если это не IP — пробуем как серийный номер через SCCM
        if (!isIp) {
            try {
                const serialResult = await findHostBySerial(val);
                if (serialResult.success && serialResult.hostName) {
                    const hostName = serialResult.hostName.toUpperCase();

                    // Найденный hostname ищем как обычно
                    const hostResult = await findHost(hostName);
                    if (hostResult.success) {
                        const targetDn = hostResult.containerDn || hostResult.containerDN;

                        // Серийник предзаполняем — не будем запрашивать S/N через WMI
                        const prefill = { serial: val.trim() };

                        const ok = await navigateToHostInTree(hostName, targetDn, prefill);
                        if (ok) return;
                        alert(`Хост ${hostName} (найден по S/N ${val}) не отображается в дереве`);
                        return;
                    }
                    alert(`По серийному номеру ${val} найден хост ${hostName}, но он отсутствует в AD`);
                    return;
                }
            } catch (serialErr) {
                console.error('Serial search error');
            }
        }

        // 3) Ничего не найдено
        if (isIp) {
            alert(`Не удалось найти хост по IP-адресу ${val}. Возможно, отсутствует обратная DNS-запись или компьютер не в AD.`);
        } else {
            alert(`Хост "${val}" не найден в AD, устройство с таким серийным номером также не найдено в SCCM.`);
        }
    } catch (err) {
        console.error('Search error');
        alert('Ошибка поиска: ' + err.message);
    } finally {
        searchInput.disabled = false;
        if (searchContainer) searchContainer.classList.remove('searching');
    }
}

// ---- Поиск хоста ----
async function searchHandler(e) {
    if (e.key !== 'Enter') return;
    const val = e.target.value.trim().toUpperCase();
    if (!val) return;
    if (!validateSearchInput(val)) return;
    const searchInput = e.target;
    const searchContainer = document.getElementById('search-container');
    await performSearch(val, searchInput, searchContainer);
}

// ---- Инициализация ----
function init() {
    console.log('init started');
    updateButtonsAndTabsState();
    void buildTree();

    const groupsBtnElem = document.getElementById('groupsBtn');
    const closeGroupsBtn = document.getElementById('closeGroupsModalBtn');

    if (groupsBtnElem) groupsBtnElem.onclick = openGroupsModal;
    if (closeGroupsBtn) closeGroupsBtn.onclick = closeGroupsModal;

    document.querySelectorAll('.closeSoftwareModalBtn').forEach(btn => btn.onclick = closeSoftwareModal);
    document.querySelectorAll('.closeDevicesModalBtn').forEach(btn => btn.onclick = closeDevicesModal);

    const softwareSearch = document.getElementById('softwareSearchInput');
    if (softwareSearch) softwareSearch.oninput = debounce(renderSoftwareTable, 300);

    const searchField = document.getElementById('search');
    if (searchField) {
        searchField.removeEventListener('keydown', searchHandler);
        searchField.addEventListener('keydown', searchHandler);
    }

    const ipSpan = document.getElementById('ip-addr');
    if (ipSpan) {
        const observer = new MutationObserver(() => {
            const ip = ipSpan.textContent.trim();
            store.setState({
                isValidIp: ip && ip !== '—' && ip !== 'Offline' && ip !== 'Запрос...' && !ip.includes('Ошибка')
            });
            updateButtonsAndTabsState();
        });
        observer.observe(ipSpan, { childList: true, subtree: true, characterData: true });
    }

    document.querySelectorAll('.tab').forEach(tab => {
        tab.addEventListener('click', (e) => {
            if (tab.classList.contains('disabled')) return;
            switchToTab(tab.dataset.tab);
        });
    });

    window.onclick = (event) => {
        if (event.target === document.getElementById('groupsModal')) closeGroupsModal();
        if (event.target === document.getElementById('softwareModal')) closeSoftwareModal();
        if (event.target === document.getElementById('devicesModal')) closeDevicesModal();
    };

    window.addEventListener('beforeunload', () => {
        endBatchSafe();
        if (currentAbortController) {
            currentAbortController.abort();
        }
    });

    console.log('init finished');
}

document.addEventListener('DOMContentLoaded', init);