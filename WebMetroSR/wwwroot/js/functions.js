// ---------- Функция RDP ----------
function openRdp() {
    const state = store.getState();
    const ip = state.currentIp;

    if (!ip || ip === '—' || ip === 'Offline' || ip === 'Запрос...') {
        alert('IP-адрес некорректен');
        return;
    }

    const cmdContent = `@echo off
                        setlocal

                        set "rdpFile=%TEMP%\\conn_${Date.now()}.rdp"

                        echo full address:s:${ip}>"%rdpFile%"
                        echo username:s:${ip}\\ruadmin>>"%rdpFile%"
                        echo prompt for credentials:i:1>>"%rdpFile%"

                        start "" mstsc.exe "%rdpFile%"

                        timeout /t 3 /nobreak >nul

                        del /f /q "%rdpFile%"

                        start /b "" cmd /c "timeout /t 1 /nobreak >nul & del /f /q "%~f0"" & exit`;

    const blob = new Blob([cmdContent], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `rdp_${ip}.cmd`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}

// ---------- Функция открытия шары ----------
function openShare() {
    const state = store.getState();
    const ip = state.currentIp;

    if (!ip || ip === '—' || ip === 'Offline' || ip === 'Запрос...' || ip.includes('Ошибка')) {
        alert('IP-адрес не загружен или некорректен');
        return;
    }

    const cmdContent = `@echo off
                        setlocal

                        set /p PASSWORD=Введите пароль для ${ip}\\ruadmin: 

                        net use \\\\${ip}\\c$ /user:${ip}\\ruadmin %PASSWORD%
                        if errorlevel 1 (
                            echo Ошибка подключения. Проверьте пароль.
                            pause
                            exit /b 1
                        )
                        start explorer \\\\${ip}\\c$

                        timeout /t 2 /nobreak >nul

                        start /b "" cmd /c "timeout /t 1 /nobreak >nul & del /f /q "%~f0"" & exit`;

    const blob = new Blob([cmdContent], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `open_c_${ip}.cmd`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
window.openShare = openShare;

// ---------- Insight Card ----------
function openInsightCard() {
    const serial = document.getElementById('pc-serial')?.textContent.trim();
    if (!serial || serial === '—' || serial === 'Запрос...') {
        alert('S/N BIOS не загружен');
        return;
    }
    window.open(
        `${INSIGHT_URL}?criteria=${encodeURIComponent(serial)}&criteriaType=FREETEXT&schema=1`,
        '_blank'
    );
}
window.openInsightCard = openInsightCard;

// ---------- Очистка MSI/MSP ----------
async function cleanMsiMsp() {
    const state = store.getState();
    const hostname = state.currentHostname;

    if (!hostname || hostname === '—' || hostname === 'Запрос...') {
        alert('Имя хоста не загружено');
        return;
    }

    if (!confirm('Пожалуйста, подтвердите действие очистки MSI/MSP. Продолжить?')) {
        console.log('Очистка MSI/MSP отменена пользователем');
        return;
    }

    showMsiCleanLog();

    const msiCleanBtn = document.querySelector('.func-btn[data-func="msi"]');
    if (msiCleanBtn) {
        msiCleanBtn.disabled = true;
        msiCleanBtn.textContent = '⏳ Очистка...';
    }

    try {
        const result = await cleanMsiMspApi(hostname);
        if (result.success) {
            let decodedOutput = result.output || 'Очистка завершена успешно!';
            decodedOutput = decodeUnicodeEscapes(decodedOutput);
            appendToMsiLog(`✅ ${decodedOutput}`);
            store.cacheClear(`disk_${hostname}`);
            await updateDiskInfo();
        } else {
            let decodedOutput = result.output || 'Неизвестная ошибка';
            decodedOutput = decodeUnicodeEscapes(decodedOutput);
            appendToMsiLog(`❌ Ошибка: ${decodedOutput}`);
        }
    } catch (err) {
        console.error('Error cleaning MSI/MSP:', err);
        appendToMsiLog(`❌ Ошибка подключения: ${err.message}`);
    } finally {
        if (msiCleanBtn) {
            msiCleanBtn.disabled = false;
            msiCleanBtn.textContent = 'Очистить MSI';
        }
    }
}
window.cleanMsiMsp = cleanMsiMsp;

// ---------- Вспомогательные функции для MSI/MSP ----------
function decodeUnicodeEscapes(str) {
    if (!str) return str;
    try {
        return str.replace(/\\u([0-9A-Fa-f]{4})/g, (match, grp) => String.fromCharCode(Number.parseInt(grp, 16)));
    } catch (e) {
        console.error('Decode error:', e);
        return str;
    }
}

function showMsiCleanLog() {
    const oldLog = document.getElementById('msiCleanLogArea');
    if (oldLog) oldLog.remove();

    const funcsSection = document.querySelector('#funcs .data-section');
    if (!funcsSection) return;

    const logHtml = `
        <div id="msiCleanLogArea" class="printer-log" style="margin-top: 20px; padding: 15px; background: #1e1e2f; border-radius: 8px; color: #d4d4d4; font-family: 'JetBrains Mono', monospace; font-size: 12px;">
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; padding-bottom: 8px; border-bottom: 1px solid #333;">
                <strong style="color: #fff;">📋 Результат очистки MSI/MSP файлов</strong>
                <button id="clearMsiLogBtn" class="tag-btn" style="font-size: 10px; padding: 2px 8px;">Очистить</button>
            </div>
            <div id="msiCleanLogContent" style="white-space: pre-wrap; word-break: break-word; max-height: 300px; overflow-y: auto; font-family: 'JetBrains Mono', monospace; font-size: 12px; line-height: 1.5;">
                <span style="color: #ffd93d;">⏳ Выполняется очистка, пожалуйста подождите...</span>
            </div>
        </div>
    `;
    funcsSection.insertAdjacentHTML('beforeend', logHtml);

    const clearBtn = document.getElementById('clearMsiLogBtn');
    if (clearBtn) {
        clearBtn.onclick = () => {
            const logContent = document.getElementById('msiCleanLogContent');
            if (logContent) logContent.innerHTML = '';
        };
    }
}

function appendToMsiLog(message) {
    let logContent = document.getElementById('msiCleanLogContent');
    if (!logContent) {
        showMsiCleanLog();
        logContent = document.getElementById('msiCleanLogContent');
        if (!logContent) return;
    }

    const timestamp = new Date().toLocaleTimeString();
    const loadingIndicator = '<span style="color: #ffd93d;">⏳ Выполняется очистка, пожалуйста подождите...</span>';
    if (logContent.innerHTML.includes(loadingIndicator)) {
        logContent.innerHTML = '';
    }

    let cleanMessage = message;
    cleanMessage = decodeUnicodeEscapes(cleanMessage);
    try {
        if (cleanMessage.includes('%') && !cleanMessage.includes('\\u')) {
            cleanMessage = decodeURIComponent(escape(cleanMessage));
        }
    } catch (e) { /* ignore */ }

    cleanMessage = cleanMessage.replace(/[^\x20-\x7E\u0400-\u04FF\n\r\t]/g, (c) => {
        if (c >= 'А' && c <= 'я') return c;
        if (c === ' ' || c === '\n' || c === '\r' || c === '\t') return c;
        return '';
    });

    logContent.innerHTML = `
        <div style="margin-bottom: 5px; padding-bottom: 5px; border-bottom: 1px solid #333;">
            <span style="color: #888;">[${timestamp}]</span> ${cleanMessage}
        </div>
    ` + logContent.innerHTML;
}

// ---------- Обновление информации о диске ----------
async function updateDiskInfo() {
    const state = store.getState();
    const hostname = state.currentHostname;
    const ip = state.currentIp;
    if (!hostname || hostname === '—' || hostname === 'Запрос...') return;
    try {
        const diskData = await fetchDisk(hostname, ip);
        if (diskData) {
            const hddTotalSpan = document.getElementById('hdd-total');
            const hddFreeSpan = document.getElementById('hdd-free');
            if (hddTotalSpan) hddTotalSpan.textContent = diskData.hddTotal || "—";
            if (hddFreeSpan) hddFreeSpan.textContent = diskData.hddFree || "—";
        }
    } catch (err) {
        console.error('Error updating disk info:', err);
    }
}

let printerDriversList = [];

// Загрузка драйверов в select
async function loadPrinterDrivers(forceRefresh = true) {
    const state = store.getState();
    const hostname = state.currentHostname;
    if (!hostname || hostname === '—' || hostname === 'Запрос...') {
        alert('Имя хоста не загружено');
        return;
    }

    const driverSelect = document.getElementById('printerDriverSelect');
    if (!driverSelect) return;

    driverSelect.innerHTML = '<option value="">Загрузка драйверов...</option>';
    driverSelect.disabled = true;

    try {
        const drivers = await fetchPrinterDrivers(hostname, null, forceRefresh);
        printerDriversList = Array.isArray(drivers) ? drivers : [];

        driverSelect.innerHTML = '<option value="">-- Выберите драйвер --</option>';
        printerDriversList.forEach(driver => {
            const option = document.createElement('option');
            option.value = driver;
            option.textContent = driver;
            driverSelect.appendChild(option);
        });
        driverSelect.disabled = false;

        if (printerDriversList.length === 0) {
            driverSelect.innerHTML = '<option value="">Нет доступных драйверов</option>';
        }
    } catch (err) {
        console.error('Error loading printer drivers:', err);
        driverSelect.innerHTML = `<option value="">Ошибка: ${err.message}</option>`;
        driverSelect.disabled = true;
    }
}

// Модалка "Список принтеров" – создаём при необходимости
function ensurePrintersModalExists() {
    if (!document.getElementById('printersListModal')) {
        const html = `
            <div id="printersListModal" class="modal">
                <div class="modal-content" style="max-width: 600px;">
                    <div class="modal-header">
                        <h2>Подключенные принтеры <span id="printersHostname"></span></h2>
                        <button class="close-modal closePrintersModalBtn">&times;</button>
                    </div>
                    <div class="table-container">
                        <table class="data-table" id="printersTable">
                            <thead><tr><th>Название принтера</th><th>Порт</th><th>Тип</th></tr></thead>
                            <tbody id="printersTableBody"><tr><td colspan="3" style="text-align:center;">Загрузка...</td></tr></tbody>
                        </table>
                    </div>
                    <div class="groups-count" id="printersCount"></div>
                </div>
            </div>
        `;
        document.body.insertAdjacentHTML('beforeend', html);
        const closeBtn = document.querySelector('.closePrintersModalBtn');
        if (closeBtn) {
            closeBtn.onclick = () => {
                const modal = document.getElementById('printersListModal');
                if (modal) modal.style.display = 'none';
            };
        }
    }
}

function renderPrintersList(printersData) {
    const normalized = (printersData || []).map(p => ({
        name: p.Name || p.name || '—',
        portName: p.Info || p.PortName || p.portName || '—',
        type: p.Type || p.type || '—'
    }));
    return normalized.filter(p => p.type === 'Printer' || p.type === 'Принтер');
}

async function showPrintersList(forceRefresh = true) {
    const state = store.getState();
    const hostname = state.currentHostname;
    if (!hostname || hostname === '—' || hostname === 'Запрос...') {
        alert('Имя хоста не загружено');
        return;
    }

    ensurePrintersModalExists();
    const modal = document.getElementById('printersListModal');
    const printersHostnameSpan = document.getElementById('printersHostname');
    const printersTableBody = document.getElementById('printersTableBody');
    const printersCountSpan = document.getElementById('printersCount');

    if (printersHostnameSpan) printersHostnameSpan.textContent = hostname;
    if (printersTableBody) printersTableBody.innerHTML = '<tr><td colspan="3" style="text-align:center;">Загрузка...</td></tr>';
    if (modal) modal.style.display = 'flex';

    try {
        const printersData = await fetchPrinters(hostname, null, forceRefresh);
        const onlyPrinters = renderPrintersList(printersData);

        if (onlyPrinters.length === 0) {
            if (printersTableBody) printersTableBody.innerHTML = '<tr><td colspan="3" style="text-align:center;">Нет подключенных принтеров</td></tr>';
            if (printersCountSpan) printersCountSpan.textContent = 'Всего: 0';
        } else {
            if (printersTableBody) {
                printersTableBody.innerHTML = onlyPrinters.map(printer => `
                    <tr>
                        <td>${escapeHtml(printer.name)}</td>
                        <td>${escapeHtml(printer.portName)}</td>
                        <td>${escapeHtml(printer.type)}</td>
                    </tr>
                `).join('');
            }
            if (printersCountSpan) printersCountSpan.textContent = `Всего принтеров: ${onlyPrinters.length}`;
        }
    } catch (err) {
        console.error('Error loading printers:', err);
        if (printersTableBody) {
            printersTableBody.innerHTML = `<tr><td colspan="3" style="color: var(--danger); text-align:center;">Ошибка: ${escapeHtml(err.message)}</td></tr>`;
        }
    }
}

// Подключение принтера
async function connectPrinter() {
    const printerName = document.getElementById('printerNameInput')?.value.trim();
    const printerIp = document.getElementById('printerIpInput')?.value.trim();
    const driverSelect = document.getElementById('printerDriverSelect');
    const selectedDriver = driverSelect?.options[driverSelect.selectedIndex]?.value;

    if (!printerName) { alert('Введите имя принтера'); return; }
    if (!printerIp) { alert('Введите IP адрес принтера'); return; }
    if (!selectedDriver || selectedDriver === '') { alert('Выберите драйвер принтера'); return; }

    if (!confirm('Подключаемый принтер не будет назначен по умолчанию, данное действие невозможно вне пользовательской сессии. Продолжить?')) {
        console.log('Подключение принтера отменено пользователем');
        return;
    }

    const connectBtn = document.getElementById('connectPrinterBtn');

    // ============ ПРОВЕРКА IP НА ПРИНТЕР ============
    if (connectBtn) {
        connectBtn.disabled = true;
        connectBtn.textContent = '⏳ Проверка IP...';
    }

    try {
        const checkResult = await checkPrinterIp(printerIp);
        if (!checkResult.isPrinter) {
            alert('Указанный IP адрес устройства не является принтером');
            if (connectBtn) {
                connectBtn.disabled = false;
                connectBtn.textContent = '🔌 Подключить';
            }
            return;
        }
    } catch (err) {
        console.error('IP check error:', err);
        alert('Не удалось проверить IP: ' + err.message);
        if (connectBtn) {
            connectBtn.disabled = false;
            connectBtn.textContent = '🔌 Подключить';
        }
        return;
    }
    // ================================================

    showPrinterProgress();

    const state = store.getState();
    const hostname = state.currentHostname;

    if (connectBtn) {
        connectBtn.disabled = true;
        connectBtn.textContent = '⏳ Подключение...';
    }

    try {
        const result = await connectPrinterApi(hostname, printerIp, selectedDriver, printerName);
        let cleanOutput = result.output || '';
        cleanOutput = cleanOutput.replace(/Производится подключение принтера, пожалуйста подождите\.\.\./g, '');
        cleanOutput = cleanOutput.replace(/\n\s*\n/g, '\n');

        if (result.success) {
            appendToPrinterLog(`✅ Принтер "${printerName}" успешно подключен!\n${cleanOutput}`);
            store.cacheClear('printers_');
        } else {
            appendToPrinterLog(`❌ Ошибка подключения: ${result.error || result.output || 'Неизвестная ошибка'}`);
        }
    } catch (err) {
        console.error('Error connecting printer:', err);
        appendToPrinterLog(`❌ Ошибка: ${err.message}`);
    } finally {
        if (connectBtn) {
            connectBtn.disabled = false;
            connectBtn.textContent = '🔌 Подключить';
        }
    }
}

// Лог подключения принтера
function showPrinterProgress() {
    const oldLog = document.getElementById('printerLogArea');
    if (oldLog) oldLog.remove();

    const printerPanel = document.getElementById('printerPanel');
    if (!printerPanel) return;

    const logHtml = `
        <div id="printerLogArea" class="printer-log">
            <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 10px; padding-bottom: 8px; border-bottom: 1px solid #333;">
                <strong style="color: #fff;">📋 Результат подключения принтера</strong>
                <button id="clearPrinterLogBtn" class="tag-btn" style="font-size: 10px; padding: 2px 8px;">Очистить</button>
            </div>
            <div id="printerLogContent" style="white-space: pre-wrap; word-break: break-word; max-height: 300px; overflow-y: auto; font-family: 'JetBrains Mono', monospace; font-size: 12px; line-height: 1.5;">
                <span style="color: #ffd93d;">⏳ Выполняется подключение принтера, пожалуйста подождите...</span>
            </div>
        </div>
    `;
    printerPanel.insertAdjacentHTML('beforeend', logHtml);

    const clearBtn = document.getElementById('clearPrinterLogBtn');
    if (clearBtn) {
        clearBtn.onclick = () => {
            const logContent = document.getElementById('printerLogContent');
            if (logContent) logContent.innerHTML = '';
        };
    }
}

function appendToPrinterLog(message) {
    let logContent = document.getElementById('printerLogContent');
    if (!logContent) {
        const printerPanel = document.getElementById('printerPanel');
        if (printerPanel) {
            showPrinterProgress();
            logContent = document.getElementById('printerLogContent');
        }
    }
    if (!logContent) return;

    const timestamp = new Date().toLocaleTimeString();
    const loadingIndicator = '<span style="color: #ffd93d;">⏳ Выполняется подключение принтера, пожалуйста подождите...</span>';
    if (logContent.innerHTML.includes(loadingIndicator)) {
        logContent.innerHTML = '';
    }

    const lines = message.split(/\r?\n/);
    const filteredLines = lines
        .map(line => line.trim())
        .filter(line => line.length > 0 &&
            !line.includes('Path :') &&
            !line.includes('RelativePath :') &&
            !line.includes('Server :') &&
            !line.includes('NamespacePath :') &&
            !line.includes('ClassName :') &&
            !line.includes('IsClass :') &&
            !line.includes('IsInstance :') &&
            !line.includes('IsSingleton :'));

    if (filteredLines.length === 0 && message.trim().length > 0) {
        filteredLines.push(message.trim());
    }

    for (let line of filteredLines) {
        let displayLine = line;
        if (line.includes('успешно') || line.includes('подключен')) displayLine = `✅ ${line}`;
        else if (line.includes('Ошибка') || line.includes('ошибка')) displayLine = `❌ ${line}`;
        logContent.innerHTML =
            `<div style="margin-bottom: 5px; padding-bottom: 5px; border-bottom: 1px solid #333;">
                <span style="color: #888;">[${timestamp}]</span> ${displayLine}
            </div>` + logContent.innerHTML;
    }
}

// Панель подключения принтера
function showPrinterPanel() {
    // ============ TOGGLE: если панель уже открыта — закрываем ============
    const existingPanel = document.getElementById('printerPanel');
    if (existingPanel) {
        existingPanel.remove();
        const oldLog = document.getElementById('printerLogArea');
        if (oldLog) oldLog.remove();
        return;
    }
    // ======================================================================

    const funcsSection = document.querySelector('#funcs .data-section');
    if (!funcsSection) return;

    const panelHtml = `
        <div id="printerPanel" class="printer-panel">
            <h3>🖨️ Добавление сетевого принтера</h3>
            <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 15px; margin-bottom: 15px;">
                <div>
                    <label for="printerNameInput">Имя принтера</label>
                    <input type="text" id="printerNameInput" class="search-input-table" placeholder="Например: HP_LaserJet_4015" style="margin-bottom: 0;">
                </div>
                <div>
                    <label for="printerIpInput">IP адрес принтера</label>
                    <input type="text" id="printerIpInput" class="search-input-table" placeholder="192.168.1.100" style="margin-bottom: 0;">
                </div>
                <div>
                    <label for="printerDriverSelect">Драйвер принтера</label>
                    <select id="printerDriverSelect" class="search-input-table" style="margin-bottom: 0;">
                        <option value="">-- Загрузка драйверов --</option>
                    </select>
                </div>
                <div style="display: flex; gap: 10px; align-items: flex-end;">
                    <button class="btn-act" id="connectPrinterBtn" style="min-width: auto; padding: 10px 15px;">🔌 Подключить</button>
                    <button class="btn-act" id="listPrintersBtn" style="min-width: auto; padding: 10px 15px;">📋 Список принтеров</button>
                </div>
            </div>
        </div>
    `;
    funcsSection.insertAdjacentHTML('beforeend', panelHtml);
    void loadPrinterDrivers();
    document.getElementById('connectPrinterBtn').onclick = connectPrinter;
    document.getElementById('listPrintersBtn').onclick = () => { void showPrintersList(); };
}

// ---------- Обработчик функциональных кнопок ----------
function handleFuncButton(funcType) {
    const hostname = store.getState().currentHostname;
    const ip = store.getState().currentIp;
    if (!hostname || hostname === '—' || hostname === 'Запрос...') {
        alert('Информация о хосте не загружена');
        return;
    }

    switch (funcType) {
        case 'software':
            void loadInstalledSoftware();
            break;
        case 'devices':
            void loadDevices();
            break;
        case 'rdp':                 
            openRdp();              
            break;                  
        case 'share':
            openShare();
            break;
        case 'msi':
            void cleanMsiMsp();
            break;
        case 'printer':
            showPrinterPanel();
            break;
        default:
            alert(`Функция ${funcType} для ${hostname} (${ip})`);
    }
}