// tree.js - работа с деревом контейнеров

async function fetchContainers() {
    const url = `${API_BASE}/containers`;
    try {
        const response = await fetch(url);
        if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        const data = await response.json();
        return data;
    } catch (err) {
        throw err;
    }
}

async function fetchHosts(dn) {
    const url = `${API_BASE}/hosts?dn=${encodeURIComponent(dn)}`;
    try {
        const response = await fetch(url);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return await response.json();
    } catch (err) {
        return [];
    }
}

async function loadHosts(dn, container) {
    if (container.dataset.loaded === "true") return;
    const list = container.querySelector('.hosts-list');
    if (!list) return;
    list.innerHTML = '<div class="loading-text" style="padding:8px 0 8px 24px;">Загрузка...</div>';
    try {
        const hosts = await fetchHosts(dn);
        list.innerHTML = '';
        hosts.forEach(h => {
            const div = document.createElement('div');
            div.className = 'host-item';
            div.textContent = (h.Name || h.name).toUpperCase();
            div.onclick = (e) => {
                e.stopPropagation();
                if (typeof updateHostInfo === 'function') void updateHostInfo(div.textContent, div);
            };
            list.appendChild(div);
        });
        container.dataset.loaded = "true";
        const activeHostname = store?.getState()?.currentHostname;
        if (activeHostname) {
            const activeDiv = Array.from(list.querySelectorAll('.host-item')).find(d => d.textContent === activeHostname);
            if (activeDiv) activeDiv.classList.add('active-host');
        }
    } catch (err) {
        list.innerHTML = '<div style="color: var(--danger); padding:8px 0 8px 24px;">Ошибка загрузки</div>';
    }
}

function createContainerElement(c, index) {
    const det = document.createElement('details');
    det.dataset.dn = c.DistinguishedName || c.distinguishedName;
    const containerName = c.Name || c.name;
    const summary = document.createElement('summary');
    const contentWrapper = document.createElement('div');
    contentWrapper.className = 'summary-content';
    const nameSpan = document.createElement('span');
    nameSpan.className = 'container-name';
    nameSpan.textContent = containerName;
    contentWrapper.appendChild(nameSpan);

    summary.appendChild(contentWrapper);
    det.appendChild(summary);
    const hostsDiv = document.createElement('div');
    hostsDiv.className = 'hosts-list';
    det.appendChild(hostsDiv);
    det.addEventListener('toggle', () => {
        if (det.open) loadHosts(det.dataset.dn, det);
    });
    return det;
}

async function buildTree() {
    const treeContainer = document.getElementById('tree');
    if (!treeContainer) {
        return;
    }
    treeContainer.innerHTML = '<div class="loading-text" style="padding:10px;">Синхронизация AD...</div>';
    try {
        const data = await fetchContainers();
        treeContainer.innerHTML = '';
        if (!data || data.length === 0) {
            treeContainer.innerHTML = '<div style="padding:10px; color: gray;">Нет контейнеров</div>';
            return;
        }
        data.forEach((c, index) => {
            treeContainer.appendChild(createContainerElement(c, index));
        });
    } catch (err) {
        treeContainer.innerHTML = '<div style="padding:10px; color: var(--danger);">Ошибка загрузки контейнеров</div>';
    }
}

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>]/g, m => (m === '&' ? '&amp;' : m === '<' ? '&lt;' : '&gt;'));
}