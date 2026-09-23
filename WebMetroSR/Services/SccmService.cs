using System.Collections.Concurrent;
using System.Management;
using System.Runtime.Versioning;
using WebMetroSR.Interfaces;

namespace WebMetroSR.Services
{
    public class SccmService(ICacheService cache) : ISccmService
    {
        private static readonly string _sccmServer = "mow11sccmps402.rumadm.net";
        private static string? _cachedSiteCode;
        private static readonly object _siteCodeLock = new();
        private readonly ICacheService _cache = cache;
        private readonly SemaphoreSlim _wmiSemaphore = new(3);

        [SupportedOSPlatform("windows")]
        public async Task<bool> IsClientActiveAsync(string hostName)
        {
            string cacheKey = CacheKeys.SccmActiveKey(hostName);
            if (_cache.TryGet<bool>(cacheKey, out bool cached))
                return cached;

            await _wmiSemaphore.WaitAsync();
            try
            {
                bool result = await Task.Run(() => IsClientActiveInternal(hostName));
                _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
                return result;
            }
            finally
            {
                _wmiSemaphore.Release();
            }
        }

        [SupportedOSPlatform("windows")]
        public bool IsClientActive(string hostName)
        {
            return IsClientActiveAsync(hostName).GetAwaiter().GetResult();
        }

        [SupportedOSPlatform("windows")]
        private static bool IsClientActiveInternal(string hostName)
        {
            try
            {
                string? siteCode = GetSiteCode();
                if (string.IsNullOrEmpty(siteCode)) return false;

                var options = new ConnectionOptions
                {
                    Authentication = AuthenticationLevel.PacketPrivacy,
                    Impersonation = ImpersonationLevel.Impersonate
                };
                string scopePath = $@"\\{_sccmServer}\root\sms\site_{siteCode}";
                var scope = new ManagementScope(scopePath, options);
                scope.Connect();

                string query = $"SELECT ResourceID FROM SMS_R_System WHERE NetbiosName = '{hostName.ToUpper()}'";
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
                return searcher.Get().Count > 0;
            }
            catch
            {
                return false;
            }
        }

        [SupportedOSPlatform("windows")]
        public async Task<Dictionary<string, bool>> GetMultipleClientsActivityAsync(List<string> hostnames)
        {
            var results = new ConcurrentDictionary<string, bool>();
            var tasks = hostnames.Select(async hostname =>
            {
                bool isActive = await IsClientActiveAsync(hostname);
                results[hostname] = isActive;
            });
            await Task.WhenAll(tasks);
            return results.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        [SupportedOSPlatform("windows")]
        private static string? GetSiteCode()
        {
            if (_cachedSiteCode != null) return _cachedSiteCode;
            lock (_siteCodeLock)
            {
                if (_cachedSiteCode != null) return _cachedSiteCode;
                try
                {
                    var baseScope = new ManagementScope($@"\\{_sccmServer}\root\sms");
                    baseScope.Connect();
                    var query = new ObjectQuery("SELECT SiteCode FROM SMS_ProviderLocation WHERE ProviderForLocalSite = 'True'");
                    using var searcher = new ManagementObjectSearcher(baseScope, query);
                    foreach (ManagementObject obj in searcher.Get().Cast<ManagementObject>())
                    {
                        _cachedSiteCode = obj["SiteCode"]?.ToString();
                        break;
                    }
                }
                catch { }
            }
            return _cachedSiteCode;
        }
    }
}