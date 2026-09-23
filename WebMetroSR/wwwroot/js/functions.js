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
window.openRdp = openRdp;

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
        return str.replace(/\\u([0-9A-Fa-f]{4})/g, (match, grp) => String.fromCharCode(parseInt(grp, 16)));
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
            loadInstalledSoftware();
            break;
        case 'devices':
            loadDevices();
            break;
        case 'share':
            openShare();
            break;
        case 'insight':
            const serial = document.getElementById('pc-serial')?.textContent.trim();
            if (!serial || serial === '—' || serial === 'Запрос...') {
                alert('S/N BIOS не загружен');
                return;
            }
            window.open(`${INSIGHT_URL}?criteria=${encodeURIComponent(serial)}&criteriaType=FREETEXT&schema=1`, '_blank');
            break;
        case 'msi':
            cleanMsiMsp();
            break;
        default:
            alert(`Функция ${funcType} для ${hostname} (${ip})`);
    }
}