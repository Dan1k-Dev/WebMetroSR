using System.Text.Json;

namespace WebMetroSR.Interfaces
{
    public interface IPowerShellService
    {
        Task<(string Output, string Error, int ExitCode)> RunScriptAsyncWithStatus(
            string script,
            int timeoutSeconds = 60,
        CancellationToken cancellationToken = default);

        Task<string> RunScriptAsync(
            string script,
            int timeoutSeconds = 60,
            CancellationToken cancellationToken = default);

        Task<string> ExecuteLapsCommandAsync(string hostname, bool forceRefresh = false);
        string DecodeOutput(string output);
        Task<T?> RunScriptAndDeserializeAsync<T>(string script, JsonSerializerOptions? options = null, CancellationToken cancellationToken = default);

        // Новые методы
        string GetInitiatingUser();
        string InjectUserVariable(string script);
    }
}