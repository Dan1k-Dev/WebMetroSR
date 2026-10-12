using System.DirectoryServices;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using WebMetroSR.Interfaces;
using WebMetroSR.Models;

namespace WebMetroSR.Services
{
    public class AdService(IPowerShellService powerShellService, ILogger<AdService> logger, ICacheService cache) : IAdService
    {
        private readonly IPowerShellService _ps = powerShellService;
        private readonly ILogger<AdService> _logger = logger;
        private readonly ICacheService _cache = cache;
        private static readonly SemaphoreSlim _sccmSemaphore = new(5, 10);

        public async Task<List<HostInfo>> GetHostsAsync(string dn)
        {
            if (string.IsNullOrEmpty(dn))
                return [];

            string script = $@"
                Import-Module ActiveDirectory -ErrorAction Stop; 
                $res = Get-ADComputer -Filter * -SearchBase '{dn}' -SearchScope Subtree | Select-Object Name, DistinguishedName;
                if ($res) {{ $res | ConvertTo-Json }} else {{ '[]' }}";
            script = _ps.InjectUserVariable(script);
            string output = await _ps.RunScriptAsync(script);
            if (string.IsNullOrWhiteSpace(output))
                return [];

            System.Text.Json.JsonSerializerOptions jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };
            var options = jsonSerializerOptions;
            var hosts = System.Text.Json.JsonSerializer.Deserialize<List<HostInfo>>(output, options);
            return hosts ?? [];
        }

        [SupportedOSPlatform("windows")]
        public async Task<FindHostResult> FindHostAsync(string hostname)
        {
            try
            {
                string targetHost = hostname.Trim();
                bool isIp = IPAddress.TryParse(targetHost, out _);
                string originalInput = targetHost;

                if (isIp)
                {
                    string? resolved = GetHostNameFromIp(targetHost);
                    if (string.IsNullOrEmpty(resolved))
                        return new FindHostResult { Success = false, ErrorMessage = $"Не удалось определить имя компьютера по IP-адресу {originalInput}" };
                    targetHost = resolved;
                }

                using var entry = new DirectoryEntry("LDAP://ru.rumadm.net");
                using var searcher = new DirectorySearcher(entry);
                searcher.Filter = $"(&(objectClass=computer)(name={targetHost}))";
                searcher.PropertiesToLoad.Add("distinguishedName");
                var result = searcher.FindOne();

                if (result != null)
                {
                    var dnProperty = result.Properties["distinguishedName"];
                    if (dnProperty != null && dnProperty.Count > 0)
                    {
                        string? dn = dnProperty[0]?.ToString();
                        if (!string.IsNullOrEmpty(dn))
                        {
                            int commaIndex = dn.IndexOf(',');
                            if (commaIndex >= 0 && commaIndex < dn.Length - 1)
                            {
                                string containerDn = dn[(commaIndex + 1)..];
                                return new FindHostResult { Success = true, ContainerDn = containerDn, HostName = targetHost };
                            }
                        }
                    }
                    return new FindHostResult { Success = false, ContainerDn = null, HostName = targetHost };
                }
                else
                {
                    string msg = isIp ? $"Компьютер с именем {targetHost} (полученным из IP {originalInput}) не найден в AD" : $"Хост {targetHost} не найден в AD";
                    return new FindHostResult { Success = false, ContainerDn = null, HostName = targetHost };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "FindHost error for {Hostname}", hostname);
                return new FindHostResult { Success = false, ErrorMessage = $"Ошибка при поиске: {ex.Message}" };
            }
        }

        public async Task<List<string>> GetUserGroupsAsync(string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return [];

            string cacheKey = $"groups_{userName}";
            if (_cache.TryGet<List<string>>(cacheKey, out var cached) && cached is not null)
                return cached;

            try
            {
                string script = $@"
                                Import-Module ActiveDirectory -ErrorAction Stop
                                $user = Get-ADUser -Identity '{userName.Replace("'", "''")}' -Properties MemberOf -ErrorAction Stop
                                if ($user) {{
                                    $user.MemberOf | ForEach-Object {{ (Get-ADGroup $_).Name }}
                                }} else {{
                                    @()
                                }}";
                script = _ps.InjectUserVariable(script);
                string output = await _ps.RunScriptAsync(script);
                var groups = output.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries).ToList();

                _cache.Set(cacheKey, groups, TimeSpan.FromMinutes(60));
                return groups;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения групп для пользователя {UserName}", userName);
                return [];
            }
        }

        public async Task<string> GetSccmIpAsync(string hostname)
        {
            string cacheKey = CacheKeys.SccmIpKey(hostname);
            if (_cache.TryGet<string>(cacheKey, out var cached) && cached is not null)
                return cached;

            if (string.IsNullOrWhiteSpace(hostname))
                return "имя хоста не указано";

            string script = $@"
                $targetHost = '{hostname.Trim()}'
                $siteServer = 'mow11sccmps402.rumadm.net'

                try {{
                    $siteInfo = Get-WmiObject -ComputerName $siteServer -Namespace 'root/sms' -Class 'SMS_ProviderLocation' -ErrorAction Stop
                    $siteCode = $siteInfo.SiteCode
                    $namespace = ""root\sms\site_$siteCode""
                    $device = Get-WmiObject -ComputerName $siteServer -Namespace $namespace -Class 'SMS_R_System' -Filter ""Name = '$targetHost'""

                    if ($device) {{
                        $device.IPAddresses[0]
                    }}
                    else {{
                        'устройство не найдено'
                    }}
                }}
                catch {{
                    $_.Exception.Message
                }}";
            script = _ps.InjectUserVariable(script);
            string output = await _ps.RunScriptAsync(script);
            string result = string.IsNullOrEmpty(output) ? "устройство не найдено" : output;
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(30));
            return result;
        }

        public async Task<string> GetCurrentUserAsync(string hostname, string ip)
        {
            string password = await GetLapsPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка") || password.Contains("не найден"))
                return "LAPS Error";

            string target = (string.IsNullOrEmpty(ip) || ip.Contains("найден")) ? hostname : ip;
            string script = $@"
                $sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                $cred = New-Object System.Management.Automation.PSCredential('{hostname}\ruadmin', $sec)
                $params = @{{Class = 'Win32_ComputerSystem'; ComputerName = '{target}'; Credential = $cred; Impersonation = 'Impersonate'; Authentication = 'PacketPrivacy'}}
                $sys = Get-WmiObject @params
                if ($sys.UserName) {{ $sys.UserName.Split('\')[-1] }} else {{ 'Сессия не найдена' }}";
            script = _ps.InjectUserVariable(script);
            try
            {
                return (await _ps.RunScriptAsync(script)).Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCurrentUser error for {Hostname}", hostname);
                return "WMI Error";
            }
        }

        public async Task<bool> IsHostOnlineAsync(string ip, int port = 445, int timeoutMs = 3000, CancellationToken token = default)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(timeoutMs);
                using var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(ip, port, cts.Token);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? GetHostNameFromIp(string ip)
        {
            try
            {
                var hostEntry = Dns.GetHostEntry(ip);
                if (!string.IsNullOrEmpty(hostEntry.HostName))
                    return hostEntry.HostName.Split('.')[0];
            }
            catch { }
            return null;
        }

        public async Task<string> GetLapsPasswordAsync(string hostname, bool forceRefresh = false)
        {
            return await _ps.ExecuteLapsCommandAsync(hostname, forceRefresh);
        }

        /// <summary>
        /// Получает hostname по серийному номеру BIOS через SCCM.
        /// Возвращает null, если устройство не найдено.
        /// </summary>
        public async Task<string?> GetHostnameBySerialAsync(string serialNumber)
        {
            if (string.IsNullOrWhiteSpace(serialNumber))
                return null;

            string cacheKey = $"serial_{serialNumber.Trim().ToUpper()}";
            if (_cache.TryGet<string>(cacheKey, out var cached) && !string.IsNullOrEmpty(cached))
                return cached;

            string escapedSerial = serialNumber.Trim().Replace("'", "''");

            string script = $@"
                            $SerialNumber = '{escapedSerial}'
                            $SiteServer   = 'mow11sccmps402.rumadm.net'

                            try {{
                                $siteCode = (Get-WmiObject -ComputerName $SiteServer -Namespace 'root/sms' -Class 'SMS_ProviderLocation').SiteCode
                                $namespace = ""root\sms\site_$siteCode""

                                $bios = Get-WmiObject -ComputerName $SiteServer -Namespace $namespace -Query ""SELECT ResourceID FROM SMS_G_System_PC_BIOS WHERE SerialNumber = '$SerialNumber'""

                                if (-not $bios) {{
                                    return
                                }}

                                foreach ($b in $bios) {{
                                    $sys = Get-WmiObject -ComputerName $SiteServer -Namespace $namespace -Class 'SMS_R_System' -Filter ""ResourceId = $($b.ResourceID)""
                                    if ($sys) {{ $sys.Name }}
                                }}
                            }}
                            catch {{
                                Write-Error $_.Exception.Message
                            }}";
            script = _ps.InjectUserVariable(script);

            try
            {
                string output = (await _ps.RunScriptAsync(script, timeoutSeconds: 60)).Trim();
                if (string.IsNullOrWhiteSpace(output))
                    return null;

                // На всякий случай берём первую непустую строку (если вдруг несколько устройств с этим S/N)
                string? hostname = output
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .FirstOrDefault(s => !string.IsNullOrEmpty(s) && !s.StartsWith("Ошибка", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(hostname))
                    return null;

                _cache.Set(cacheKey, hostname, TimeSpan.FromMinutes(30));
                return hostname;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetHostnameBySerialAsync failed for {Serial}", serialNumber);
                return null;
            }
        }
    }
}