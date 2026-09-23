using Microsoft.Extensions.Caching.Memory;
using WebMetroSR.Interfaces;
using System.Diagnostics.CodeAnalysis;

namespace WebMetroSR.Services
{
    public class MemoryCacheService(IMemoryCache cache, ILogger<MemoryCacheService> logger) : ICacheService
    {
        private readonly IMemoryCache _cache = cache;
        private readonly ILogger<MemoryCacheService> _logger = logger;

        public T? Get<T>(string key)
        {
            _cache.TryGetValue(key, out T? value);
            return value;
        }

        public void Set<T>(string key, T value, TimeSpan? absoluteExpiration = null)
        {
            var options = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = absoluteExpiration ?? TimeSpan.FromMinutes(5)
            };
            _cache.Set(key, value, options);
        }

        public void Remove(string key)
        {
            _cache.Remove(key);
        }

        public bool TryGet<T>(string key, [MaybeNull] out T value)
        {
            if (_cache.TryGetValue(key, out object? obj))
            {
                value = (T?)obj;
                return true;
            }
            value = default;
            return false;
        }
    }
}