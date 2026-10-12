const API_BASE = "/api/ad";
const INSIGHT_URL = "https://jirainvent.metro-cc.ru/secure/insight/search";
const CACHE_TTL = 60000;          // 1 минута — основная информация о ПК
const BUTTON_CACHE_TTL = 30000;   // 30 секунд — кнопки на вкладке доп. сведений
const LONG_CACHE_TTL = 3600000;   // 1 час