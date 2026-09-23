using System.Diagnostics;
using System.Security;
using System.Text;
using System.Text.Json;
using WebMetroSR.Interfaces;

namespace WebMetroSR.Services
{
    public class PowerShellService(ILogger<PowerShellService> logger, ICacheService cache, IUserCredentialService userCreds) : IPowerShellService
    {
        private readonly ILogger<PowerShellService> _logger = logger;
        private readonly ICacheService _cache = cache;
        private readonly IUserCredentialService _userCreds = userCreds;
        private static readonly SemaphoreSlim _lapsSemaphore = new(1, 1);
        private static readonly JsonSerializerOptions _defaultOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static string WrapWithUtf8(string script)
        {
            return $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; {script}";
        }

        public async Task<(string Output, string Error, int ExitCode)> RunScriptAsyncWithStatus(string script, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            string wrappedScript = WrapWithUtf8(script);
            byte[] bytes = Encoding.Unicode.GetBytes(wrappedScript);
            string encoded = Convert.ToBase64String(bytes);

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            var output = new StringBuilder();
            var error = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);

            return (output.ToString().Trim(), error.ToString().Trim(), process.ExitCode);
        }

        public async Task<string> RunScriptAsync(string script, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            var (output, error, exitCode) = await RunScriptAsyncWithStatus(script, timeoutSeconds, cancellationToken);
            if (exitCode != 0 && !string.IsNullOrEmpty(error))
                throw new Exception($"PowerShell error: {error}");
            return output;
        }

       
        public async Task<string> ExecuteLapsCommandAsync(string hostname, bool forceRefresh = false)
        {
            string cacheKey = CacheKeys.LapsKey(hostname);
            if (!forceRefresh && _cache.TryGet<string>(cacheKey, out string? cached) && cached is not null)
                return cached;

            await _lapsSemaphore.WaitAsync();
            try
            {
                string script = $@"
                    $ErrorActionPreference = 'Stop'
                    $cred = Get-StoredCredential -Target 'testCI'
                    if (-not $cred) {{ throw 'Недопустимые CD' }}
                    $password = Get-LapsADPassword -Identity '{hostname}' -Domain 'ru.rumadm.net' -Credential $cred -AsPlainText | Select-Object -ExpandProperty Password
                    if (-not $password) {{ throw 'Ошибка data_CI' }}
                    $password";

                string output = await RunScriptAsync(script);

                if (string.IsNullOrEmpty(output) || output.Contains("Ошибка") || output.Contains("throw"))
                {
                    _logger.LogError("LAPS command failed for {Hostname}: {Output}", hostname, output);
                    output = $"Ошибка: {output}";
                }

                _cache.Set(cacheKey, output, TimeSpan.FromSeconds(40));
                return output;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LAPS error for {Hostname}", hostname);
                return "Ошибка LAPS";
            }
            finally
            {
                _lapsSemaphore.Release();
            }
        }

        public async Task<T?> RunScriptAndDeserializeAsync<T>(
            string script,
            JsonSerializerOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            string output = await RunScriptAsync(script, cancellationToken: cancellationToken);
            if (string.IsNullOrWhiteSpace(output))
                return default; // вернёт null для ссылочных типов, default(T) для значимых

            string decoded = DecodeOutput(output);
            options ??= _defaultOptions;
            return JsonSerializer.Deserialize<T>(decoded, options); // может вернуть null
        }

        public string DecodeOutput(string output)
        {
            if (string.IsNullOrEmpty(output)) return output;
            if (output.Contains('�') || output.Any(c => c > 255 && c < 1000))
            {
                try
                {
                    byte[] bytes = Encoding.GetEncoding(1251).GetBytes(output);
                    return Encoding.UTF8.GetString(bytes);
                }
                catch
                {
                    return output;
                }
            }
            return output;
        }

        public string GetInitiatingUser()
        {
            return _userCreds.GetUsername() ?? "Unknown";
        }

        public string InjectUserVariable(string script)
        {
            string user = GetInitiatingUser();
            return $"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $InitiatingUser = '{user}'; " + script;
        }
    }
}