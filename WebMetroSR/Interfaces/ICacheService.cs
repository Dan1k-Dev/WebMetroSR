namespace WebMetroSR.Interfaces
{
    using System.Diagnostics.CodeAnalysis;

    public interface ICacheService
    {
        T? Get<T>(string key);
        void Set<T>(string key, T value, TimeSpan? absoluteExpiration = null);
        void Remove(string key);
        bool TryGet<T>(string key, [MaybeNull] out T value); // добавлен атрибут
    }
}
