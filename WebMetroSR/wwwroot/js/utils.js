// Утилиты

// Экранирование HTML
function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>]/g, function(m) {
        if (m === '&') return '&amp;';
        if (m === '<') return '&lt;';
        if (m === '>') return '&gt;';
        return m;
    });
}

function decodeRussian(str) {
    if (!str) return str;
    try {
        return decodeURIComponent(escape(str));
    } catch(e) {
        return str;
    }
}

function debounce(func, delay) {
    let timeout;
    return function(...args) {
        clearTimeout(timeout);
        timeout = setTimeout(() => func.apply(this, args), delay);
    };
}

// Предустановленные задержки
const DEBOUNCE_DELAYS = {
    search: 300,      // поиск по хостам
    software: 500,    // поиск по установленному ПО (увеличено с 300)
    printerFilter: 400 // фильтр принтеров
};