// tree.js - работа с деревом контейнеров

async function fetchContainers() {
    const url = `${API_BASE}/containers`;
    console.log('Fetching containers from:', url);
    try {
        const response = await fetch(url);
        if (!response.ok) throw new Error(`HTTP ${response.status}: ${response.statusText}`);
        const data = await response.json();
        console.log('Containers received:', data);
        return data;
    } catch (err) {
        console.error('Error fetching containers:', err);
        throw err;
    }
}

async function fetchHosts(dn) {
    const url = `${API_BASE}/hosts?dn=${encodeURIComponent(dn)}`;
    console.log('Fetching hosts from:', url);
    try {
        const response = await fetch(url);
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return await response.json();
    } catch (err) {
        console.error('Error fetching hosts:', err);
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
        console.error('Error loading hosts:', err);
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

    // if (index !== 0) {
    //     const menuBtn = document.createElement('button');
    //     menuBtn.textContent = '⋮';
    //     menuBtn.className = 'container-menu-btn';
    //     menuBtn.onclick = (e) => {
    //         e.stopPropagation();
    //         const rect = menuBtn.getBoundingClientRect();
    //         if (typeof showContextMenu === 'function') showContextMenu(rect.left, rect.bottom + 5, containerName);
    //     };
    //     contentWrapper.appendChild(menuBtn);
    // }

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
    console.log('buildTree: начали');
    const treeContainer = document.getElementById('tree');
    if (!treeContainer) {
        console.error('Tree container not found');
        return;
    }
    treeContainer.innerHTML = '<div class="loading-text" style="padding:10px;">Синхронизация AD...</div>';
    try {
        const data = await fetchContainers();
        console.log('buildTree: получены контейнеры', data);
        treeContainer.innerHTML = '';
        if (!data || data.length === 0) {
            treeContainer.innerHTML = '<div style="padding:10px; color: gray;">Нет контейнеров</div>';
            return;
        }
        data.forEach((c, index) => {
            treeContainer.appendChild(createContainerElement(c, index));
        });
        console.log('buildTree: завершено успешно');
    } catch (err) {
        console.error('Error building tree:', err);
        treeContainer.innerHTML = '<div style="padding:10px; color: var(--danger);">Ошибка загрузки контейнеров</div>';
    }
}

// function closeAllMenus() {
//     document.querySelectorAll('.context-menu').forEach(m => m.remove());
// }

// function showContextMenu(x, y, containerName) {
//     console.log('showContextMenu called for container:', containerName);
//     closeAllMenus();
//     const menu = document.createElement('div');
//     menu.className = 'context-menu';
//     menu.style.left = x + 'px';
//     menu.style.top = y + 'px';

//     const infoBtn = document.createElement('button');
//     infoBtn.textContent = `📊 Информация о контейнере "${containerName}"`;
//     infoBtn.onclick = async () => {
//         console.log('Menu item clicked for container:', containerName);
//         closeAllMenus();
//         await showContainerInfo(containerName);
//     };
//     menu.appendChild(infoBtn);
//     document.body.appendChild(menu);

//     setTimeout(() => {
//         const onClickOutside = (e) => {
//             if (!menu.contains(e.target)) {
//                 closeAllMenus();
//                 document.removeEventListener('click', onClickOutside);
//             }
//         };
//         document.addEventListener('click', onClickOutside);
//     }, 0);
// }

// async function showContainerInfo(containerName) {
//     console.log('showContainerInfo called for:', containerName);
//     const details = Array.from(document.querySelectorAll('details')).find(d => {
//         const nameSpan = d.querySelector('.container-name');
//         return nameSpan && nameSpan.textContent === containerName;
//     });
//     if (!details || !details.dataset.dn) {
//         alert('Не удалось найти DN контейнера');
//         return;
//     }
//     const containerDn = details.dataset.dn;
//     // Используем dataset вместо getAttribute
//     const isDarkTheme = document.documentElement.dataset.theme === 'dark';
//     const theme = isDarkTheme ? 'dark' : 'light';
//     const url = `/container-info.html?name=${encodeURIComponent(containerName)}&dn=${encodeURIComponent(containerDn)}&theme=${theme}`;
//     window.open(url, '_blank');
// }

function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>]/g, m => (m === '&' ? '&amp;' : m === '<' ? '&lt;' : '&gt;'));
}