// Модальные окна
let currentSoftwareRequest = null;
let softwareData = [];

// ---- Вспомогательные функции для групп ----
function getGroupsModalElements() {
    return {
        userNameSpan: document.getElementById('groupsUserName'),
        loadingDiv: document.getElementById('groupsLoading'),
        listUl: document.getElementById('groupsList'),
        countSpan: document.getElementById('groupsCount'),
        modal: document.getElementById('groupsModal')
    };
}

async function fetchUserGroups() {
    const userName = document.getElementById('current-user')?.textContent.trim();
    if (!userName || userName === '—' || userName === 'Запрос...' || userName === 'Unknown') {
        alert('Имя пользователя не загружено');
        return;
    }
    const elements = getGroupsModalElements();
    if (elements.userNameSpan) elements.userNameSpan.textContent = userName;
    if (elements.loadingDiv) elements.loadingDiv.style.display = 'block';
    if (elements.listUl) {
        elements.listUl.style.display = 'none';
        elements.listUl.innerHTML = '';
    }
    if (elements.countSpan) elements.countSpan.textContent = '';
    if (elements.modal) elements.modal.style.display = 'flex';

    try {
        const groups = await fetchUserGroupsApi(userName);
        if (elements.listUl) {
            if (groups.length === 0) {
                elements.listUl.innerHTML = '<li style="color: gray; text-align:center;">Нет групп</li>';
            } else {
                groups.forEach(group => {
                    const li = document.createElement('li');
                    li.textContent = group;
                    elements.listUl.appendChild(li);
                });
            }
        }
        if (elements.countSpan) elements.countSpan.textContent = `Всего групп: ${groups.length}`;
    } catch (err) {
        if (elements.listUl) {
            elements.listUl.innerHTML = `<li style="color: var(--danger);">Ошибка: ${escapeHtml(err.message)}</li>`;
        }
    } finally {
        if (elements.loadingDiv) elements.loadingDiv.style.display = 'none';
        if (elements.listUl) elements.listUl.style.display = 'block';
    }
}

function openGroupsModal() {
    const groupsBtn = document.getElementById('groupsBtn');
    if (groupsBtn && !groupsBtn.disabled) void fetchUserGroups();
}
function closeGroupsModal() {
    const modal = document.getElementById('groupsModal');
    if (modal) modal.style.display = 'none';
}

// ---- Установленное ПО ----
function getSoftwareModalElements() {
    return {
        hostnameSpan: document.getElementById('softwareHostname'),
        tableBody: document.getElementById('softwareTableBody'),
        modal: document.getElementById('softwareModal'),
        searchInput: document.getElementById('softwareSearchInput'),
        countSpan: document.getElementById('softwareCount')
    };
}

async function loadInstalledSoftware(forceRefresh = false) {
    const state = store.getState();
    const hostname = state.currentHostname;
    const ip = state.currentIp;
    if (!hostname || hostname === '—' || hostname === 'Запрос...') {
        alert('Имя хоста не загружено');
        return;
    }
    if (!ip || ip === '—' || ip === 'Offline' || ip === 'Запрос...' || ip.includes('Ошибка')) {
        alert('IP-адрес не загружен или некорректен');
        return;
    }

    const elements = getSoftwareModalElements();
    if (elements.hostnameSpan) elements.hostnameSpan.textContent = hostname;
    if (elements.tableBody) elements.tableBody.innerHTML = '<tr><td colspan="2" style="text-align:center;">Загрузка...</td></tr>';
    if (elements.modal) elements.modal.style.display = 'flex';
    if (elements.searchInput) elements.searchInput.value = '';

    if (currentSoftwareRequest) currentSoftwareRequest.abort();
    currentSoftwareRequest = new AbortController();

    try {
        const text = await fetchInstalledSoftware(hostname, ip, currentSoftwareRequest.signal, forceRefresh);
        const cleanText = text.replace(/^\uFEFF/, '');
        softwareData = JSON.parse(cleanText) || [];
        store.setState({ softwareData });
        renderSoftwareTable();
        if (elements.countSpan) elements.countSpan.textContent = `Всего программ: ${softwareData.length}`;
    } catch (err) {
        if (err.name === 'AbortError') return;
        if (elements.tableBody) {
            elements.tableBody.innerHTML = `<tr><td colspan="2" style="color: var(--danger);">Ошибка: ${escapeHtml(err.message)}</td></tr>`;
        }
    }
}

function renderSoftwareTable() {
    const searchInput = document.getElementById('softwareSearchInput');
    const tableBody = document.getElementById('softwareTableBody');
    const countSpan = document.getElementById('softwareCount');
    if (!tableBody) return;

    const searchTerm = searchInput ? searchInput.value.toLowerCase() : '';
    const filtered = softwareData.filter(app =>
        app.DisplayName && app.DisplayName.toLowerCase().includes(searchTerm)
    );

    if (filtered.length === 0) {
        tableBody.innerHTML = '<tr><td colspan="2" style="text-align:center;">Нет данных</td></tr>';
        if (countSpan) countSpan.textContent = `Найдено: 0 из ${softwareData.length}`;
        return;
    }

    tableBody.innerHTML = filtered.map(app => `
        <tr>
            <td>${escapeHtml(decodeRussian(app.DisplayName || '—'))}</td>
            <td>${escapeHtml(decodeRussian(app.Version || '—'))}</td>
        </tr>
    `).join('');
    if (countSpan) countSpan.textContent = `Найдено: ${filtered.length} из ${softwareData.length}`;
}

function closeSoftwareModal() {
    if (currentSoftwareRequest) currentSoftwareRequest.abort();
    const modal = document.getElementById('softwareModal');
    if (modal) modal.style.display = 'none';
}

// ---- Устройства ----
let currentDevicesRequest = null;

function getDevicesModalElements() {
    return {
        hostnameSpan: document.getElementById('devicesHostname'),
        tableBody: document.getElementById('devicesTableBody'),
        modal: document.getElementById('devicesModal'),
        countSpan: document.getElementById('devicesCount')
    };
}

async function loadDevices(forceRefresh = false) {
    const state = store.getState();
    const hostname = state.currentHostname;
    const ip = state.currentIp;
    if (!hostname || hostname === '—' || hostname === 'Запрос...') {
        alert('Имя хоста не загружено');
        return;
    }
    if (!ip || ip === '—' || ip === 'Offline' || ip === 'Запрос...' || ip.includes('Ошибка')) {
        alert('IP-адрес не загружен или некорректен');
        return;
    }

    const elements = getDevicesModalElements();
    if (elements.hostnameSpan) elements.hostnameSpan.textContent = hostname;
    if (elements.tableBody) elements.tableBody.innerHTML = '<tr><td colspan="3" style="text-align:center;">Загрузка...</td></tr>';
    if (elements.modal) elements.modal.style.display = 'flex';

    if (currentDevicesRequest) currentDevicesRequest.abort();
    currentDevicesRequest = new AbortController();

    try {
        const text = await fetchDevices(hostname, currentDevicesRequest.signal, forceRefresh);
        const cleanText = text.replace(/^\uFEFF/, '');
        let devices = JSON.parse(cleanText);
        if (!Array.isArray(devices)) devices = [devices];

        if (devices.length === 0 || (devices.length === 1 && !devices[0].Category && !devices[0].Name)) {
            if (elements.tableBody) elements.tableBody.innerHTML = '<tr><td colspan="3" style="text-align:center;">Нет данных</td></tr>';
            if (elements.countSpan) elements.countSpan.textContent = 'Всего устройств: 0';
        } else {
            if (elements.tableBody) {
                elements.tableBody.innerHTML = devices.map(device => `
                    <tr>
                        <td>${escapeHtml(decodeRussian(device.Category || '—'))}</td>
                        <td>${escapeHtml(decodeRussian(device.Name || '—'))}</td>
                        <td>${escapeHtml(decodeRussian(device.ConnectionType || '—'))}</td>
                    </tr>
                `).join('');
            }
            if (elements.countSpan) elements.countSpan.textContent = `Всего устройств: ${devices.length}`;
        }
    } catch (err) {
        if (err.name === 'AbortError') return;
        if (elements.tableBody) {
            elements.tableBody.innerHTML = `<tr><td colspan="3" style="color: var(--danger);">Ошибка: ${escapeHtml(err.message)}</td></tr>`;
        }
    }
}

function closeDevicesModal() {
    if (currentDevicesRequest) currentDevicesRequest.abort();
    const modal = document.getElementById('devicesModal');
    if (modal) modal.style.display = 'none';
}