using Microsoft.AspNetCore.Mvc;

namespace WebMetroSR.Interfaces
{
    public interface IFileService
    {
        Task<(bool success, string output)> CleanMsiMspAsync(string targetHost);
        Task<IActionResult> ReadRemoteLogAsync(string hostname, string targetHost, string relativePath);

        Task<string> GetRouteAsync(string hostname, string ip, CancellationToken cancellationToken = default);
        Task<string> GetTracerouteAsync(string ip, CancellationToken cancellationToken = default);
        Task<string> GetIpConfigAsync(string hostname, string ip, CancellationToken cancellationToken = default);
    }
}