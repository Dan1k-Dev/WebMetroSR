// store.js - с расширенным кэшем, без пароля
const store = {
    state: {
        currentHostname: null,
        currentIp: null,
        currentUser: null,
        isValidIp: false,
        softwareData: [],
        isSystemInfoLoaded: false,
        cache: new Map()
    },
    listeners: [],
    subscribe(listener) {
        this.listeners.push(listener);
        return () => { this.listeners = this.listeners.filter(l => l !== listener); };
    },
    setState(newState) {
        Object.assign(this.state, newState);
        this.listeners.forEach(listener => listener(this.state));
    },
    getState() {
        return { ...this.state };
    },

    cacheSet(key, data, ttlMs = CACHE_TTL) {
        this.state.cache.set(key, {
            data: data,
            timestamp: Date.now(),
            ttl: ttlMs
        });
    },

    cacheGet(key) {
        const item = this.state.cache.get(key);
        if (item && (Date.now() - item.timestamp) < item.ttl) {
            return item.data;
        }
        if (item) this.state.cache.delete(key);
        return null;
    },

    cacheClear(pattern = null) {
        if (!pattern) {
            this.state.cache.clear();
            return;
        }
        for (const key of this.state.cache.keys()) {
            if (key.includes(pattern)) this.state.cache.delete(key);
        }
    },

    getCachedHost(hostname) {
        return this.cacheGet(`host_${hostname}`);
    },
    setCachedHost(hostname, data) {
        this.cacheSet(`host_${hostname}`, data, CACHE_TTL);
    },
    clearHostCache(hostname = null) {
        if (hostname) {
            this.state.cache.delete(`host_${hostname}`);
        } else {
            this.cacheClear('host_');
        }
    }
};