using WebMetroSR.Models;

namespace WebMetroSR.Interfaces
{
    public interface IAdService
    {
        Task<List<HostInfo>> GetHostsAsync(string dn);
        Task<FindHostResult> FindHostAsync(string hostname);
        Task<List<string>> GetUserGroupsAsync(string userName);
        Task<string> GetSccmIpAsync(string hostname);
        Task<string> GetCurrentUserAsync(string hostname, string ip);
        Task<string> GetLapsPasswordAsync(string hostname, bool forceRefresh = false);

        Task<bool> IsHostOnlineAsync(string ip, int port = 445, int timeoutMs = 3000, CancellationToken token = default);
        Task<Dictionary<string, string>> GetMultipleSccmIpsAsync(List<string> hostnames);
    }
}