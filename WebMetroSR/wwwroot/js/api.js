let currentBatch = {
    id: null,
    hostname: null
};

function startBatch(hostname) {
    currentBatch.id = crypto.randomUUID();
    currentBatch.hostname = hostname;
}

function endBatch() {
    if (!currentBatch.id) return;                   
    const id = currentBatch.id;
    currentBatch.id = null;
    currentBatch.hostname = null;

    fetch('/api/ad/batch-end', {
        method: 'POST',
        headers: { 'X-Correlation-Id': id },
        keepalive: true
    }).catch(() => { });
}

window.startBatch = startBatch;
window.endBatch = endBatch;

function getCached(key) {
    return store.cacheGet(key);
}
function setCache(key, data, ttl = CACHE_TTL) {
    store.cacheSet(key, data, ttl);
}

async function apiFetch(url, options = {}) {
    const headers = options.headers || {};
    if (currentBatch.id) {
        headers['X-Correlation-Id'] = currentBatch.id;
        headers['X-Hostname'] = currentBatch.hostname;
    }
    const response = await fetch(url, {
        ...options,
        headers
    });
    if (!response.ok) {
        const errorText = await response.text();
        throw new Error(errorText || `HTTP ${response.status}`);
    }
    return response;
}

async function cachedFetch(url, cacheKey, ttl = CACHE_TTL, signal = null) {
    const cached = getCached(cacheKey);
    if (cached) return cached;
    const response = await apiFetch(url, { signal });
    const data = await response.json();
    setCache(cacheKey, data, ttl);
    return data;
}

// Конфигурация эндпоинтов
const endpoints = {
    containers: { path: '/containers', method: 'GET' },
    hosts: { path: '/hosts', method: 'GET' },
    basicInfo: { path: '/GetBasicInfo', method: 'GET' },
    currentUser: { path: '/currentuser', method: 'GET' },
    switchPort: { path: '/GetSwitchPort', method: 'GET' },
    mac: { path: '/get-mac', method: 'GET' },
    model: { path: '/get-model', method: 'GET' },
    serial: { path: '/get-serial', method: 'GET' },
    os: { path: '/get-os', method: 'GET' },
    lastBoot: { path: '/get-lastboot', method: 'GET' },
    disk: { path: '/get-disk', method: 'GET' },
    ram: { path: '/get-ram', method: 'GET' },
    netType: { path: '/get-nettype', method: 'GET' },
    groups: { path: '/groups', method: 'GET' },
    installedApps: { path: '/installed-apps', method: 'GET' },
    remoteDevices: { path: '/remote-devices', method: 'GET' },
    biosVersion: { path: '/get-bios-version', method: 'GET' },
    findHost: { path: '/find-host', method: 'GET' },
    clientActive: { path: '/check-client-active', method: 'GET' },
    route: { path: '/route', method: 'GET' },
    traceroute: { path: '/traceroute', method: 'GET' },
    ipconfig: { path: '/ipconfig', method: 'GET' },
    kixtartLog: { path: '/read-kixtart-log', method: 'GET' },
    cleanMsi: { path: '/clean-msi-msp', method: 'POST' }
};

async function request(endpoint, params = {}, options = {}) {
    const { path, method } = endpoints[endpoint];
    const url = new URL(`${API_BASE}${path}`, window.location.origin);
    Object.entries(params).forEach(([key, value]) => {
        if (value !== undefined && value !== null) {
            url.searchParams.append(key, String(value));
        }
    });
    const { signal, forceRefresh = false, ttl = CACHE_TTL } = options;

    if (method === 'GET' && !forceRefresh) {
        const cacheKey = `${endpoint}_${url.search}`;
        const cached = getCached(cacheKey);
        if (cached) return cached;
        const response = await apiFetch(url.toString(), { signal });
        const text = await response.text();
        setCache(cacheKey, text, ttl);
        return text;
    }

    const response = await apiFetch(url.toString(), { signal, method });
    return await response.text();
}

// Глобальные функции – для JSON-ответов оборачиваем в JSON.parse
async function fetchContainers(force) {
    const text = await request('containers', {}, { forceRefresh: force });
    return JSON.parse(text);
}

async function fetchHosts(dn, force) {
    const text = await request('hosts', { dn }, { forceRefresh: force });
    return JSON.parse(text);
}

async function fetchBasicInfo(hostname, signal, force) {
    const text = await request('basicInfo', { hostname }, { signal, forceRefresh: force });
    return JSON.parse(text);
}

async function fetchCurrentUser(hostname, ip, signal) {
    const text = await request('currentUser', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchSwitchPort(ip, signal) {
    const text = await request('switchPort', { ip }, { signal });
    return JSON.parse(text);
}

async function fetchMac(hostname, ip, signal) {
    const text = await request('mac', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchModel(hostname, ip, signal) {
    const text = await request('model', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchSerial(hostname, ip, signal) {
    const text = await request('serial', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchBiosVersion(hostname, ip, signal) {
    const text = await request('biosVersion', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchOs(hostname, ip, signal) {
    const text = await request('os', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchLastBoot(hostname, ip, signal) {
    const text = await request('lastBoot', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchDisk(hostname, ip, signal) {
    const text = await request('disk', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchRam(hostname, ip, signal) {
    const text = await request('ram', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchNetType(hostname, ip, signal) {
    const text = await request('netType', { hostname, ip }, { signal });
    return JSON.parse(text);
}

async function fetchUserGroupsApi(userName, signal, force) {
    const text = await request('groups', { userName }, { signal, forceRefresh: force });
    return JSON.parse(text);
}

// Для установленного ПО и устройств возвращаем сырой текст (клиент сам распарсит)
async function fetchInstalledSoftware(hostname, ip, signal, force) {
    return request('installedApps', { hostname, ip }, { signal, forceRefresh: force, ttl: 600000 });
}

async function fetchDevices(targetHost, signal, force) {
    return request('remoteDevices', { targetHost }, { signal, forceRefresh: force });
}

// Логи возвращают текст
async function fetchKixtartLog(hostname, ip, signal) {
    return request('kixtartLog', { host: hostname, ip }, { signal });
}

async function fetchRoute(hostname, ip, signal) {
    const text = await request('route', { hostname, ip }, { signal, forceRefresh: true });
    return JSON.parse(text);
}

async function fetchTraceroute(ip, signal) {
    const text = await request('traceroute', { ip }, { signal, forceRefresh: true });
    return JSON.parse(text);
}

async function fetchIpConfig(hostname, ip, signal) {
    const text = await request('ipconfig', { hostname, ip }, { signal, forceRefresh: true });
    return JSON.parse(text);
}

async function findHost(hostname, signal, force) {
    const text = await request('findHost', { hostname }, { signal, forceRefresh: force });
    return JSON.parse(text);
}

async function checkClientActive(hostname, signal, force) {
    const text = await request('clientActive', { hostname }, { signal, forceRefresh: force });
    return JSON.parse(text);
}

async function cleanMsiMspApi(targetHost, signal) {
    const text = await request('cleanMsi', { targetHost }, { signal });
    return JSON.parse(text);
}

window.fetchContainers = fetchContainers;
window.fetchHosts = fetchHosts;
window.fetchBasicInfo = fetchBasicInfo;
window.fetchCurrentUser = fetchCurrentUser;
window.fetchSwitchPort = fetchSwitchPort;
window.fetchMac = fetchMac;
window.fetchModel = fetchModel;
window.fetchSerial = fetchSerial;
window.fetchBiosVersion = fetchBiosVersion;
window.fetchOs = fetchOs;
window.fetchLastBoot = fetchLastBoot;
window.fetchDisk = fetchDisk;
window.fetchRam = fetchRam;
window.fetchNetType = fetchNetType;
window.fetchUserGroupsApi = fetchUserGroupsApi;
window.fetchInstalledSoftware = fetchInstalledSoftware;
window.fetchDevices = fetchDevices;
window.fetchKixtartLog = fetchKixtartLog;
window.fetchRoute = fetchRoute;
window.fetchTraceroute = fetchTraceroute;
window.fetchIpConfig = fetchIpConfig;
window.findHost = findHost;
window.checkClientActive = checkClientActive;
window.fetchPrinterDrivers = fetchPrinterDrivers;
window.fetchPrinters = fetchPrinters;
window.connectPrinterApi = connectPrinterApi;
window.cleanMsiMspApi = cleanMsiMspApi;