using WebMetroSR.Models;

namespace WebMetroSR.Interfaces
{
    public interface IWmiService
    {
        Task<string> GetWmiPropertyAsync(string hostname, string wmiClass, string property, string? filter = null);
        Task<(string total, string free)> GetDiskInfoAsync(string hostname, string? targetIp = null);
        Task<string> GetRamAsync(string hostname, string? targetIp = null);
        Task<string> GetMacAddressAsync(string hostname);
        Task<string> GetNetworkTypeAsync(string hostname);
        Task<string> GetLastBootTimeAsync(string hostname);
        Task<string> GetOsInfoAsync(string hostname);
        Task<string> GetOs(string hostname);
        Task<string> GetBuildRevisionAsync(string hostname);
        Task<string> GetReleaseVersionAsync(string hostname);
    }
}