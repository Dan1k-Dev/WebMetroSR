using System.Collections.Concurrent;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using WebMetroSR.Interfaces;
using WebMetroSR.Models;

namespace WebMetroSR.Services
{
    public class WmiService(IPowerShellService powerShellService, ILogger<WmiService> logger, ICacheService cache) : IWmiService
    {
        private readonly IPowerShellService _ps = powerShellService;
        private readonly ILogger<WmiService> _logger = logger;
        private readonly ICacheService _cache = cache;

        private async Task<string> GetPasswordAsync(string hostname)
        {
            return await _ps.ExecuteLapsCommandAsync(hostname);
        }

        private static string BuildWmiScript(string hostname, string password, string wmiClass, string? filter = null, string? property = null)
        {
            string filterCondition = string.IsNullOrEmpty(filter) ? "" : $" | Where-Object {{ {filter} }}";
            string propertySelect = string.IsNullOrEmpty(property) ? "" : $"$wmi.{property}";
            return $@"
                $cred = if ('{hostname}' -notin @('localhost', '.', $env:COMPUTERNAME)) {{
                    $sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                    New-Object System.Management.Automation.PSCredential('{hostname}\ruadmin', $sec)
                }} else {{ $null }}
                $params = @{{ Class = '{wmiClass}'; ComputerName = '{hostname}' }}
                if ($cred) {{ $params.Credential = $cred; $params.Impersonation = 'Impersonate'; $params.Authentication = 'PacketPrivacy' }}
                $wmi = Get-WmiObject @params {filterCondition}
                {propertySelect}
            ";
        }

        public async Task<string> GetWmiPropertyAsync(string hostname, string wmiClass, string property, string? filter = null)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "Ошибка получения пароля";

            string script = BuildWmiScript(hostname, password, wmiClass, filter, property);
            script = _ps.InjectUserVariable(script);
            return (await _ps.RunScriptAsync(script)).Trim();
        }

        [SupportedOSPlatform("windows")]
        public async Task<(string total, string free)> GetDiskInfoAsync(string hostname, string? targetIp = null)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return ("Ошибка пароля", "Ошибка пароля");

            string target = !string.IsNullOrEmpty(targetIp) && !targetIp.Contains("найден") ? targetIp : hostname;
            try
            {
                var options = new ConnectionOptions
                {
                    Username = $"{hostname}\\ruadmin",
                    Password = password,
                    Impersonation = ImpersonationLevel.Impersonate,
                    Authentication = AuthenticationLevel.PacketPrivacy,
                    Timeout = TimeSpan.FromSeconds(5)
                };
                string path = $@"\\{target}\root\cimv2";
                var scope = new ManagementScope(path, options);
                scope.Connect();
                var query = new ObjectQuery("SELECT Size, FreeSpace FROM Win32_LogicalDisk WHERE DeviceID = 'C:'");
                using var searcher = new ManagementObjectSearcher(scope, query);
                foreach (ManagementObject disk in searcher.Get().Cast<ManagementObject>())
                {
                    double totalGb = Math.Round(Convert.ToUInt64(disk["Size"]) / Math.Pow(1024, 3), 2);
                    double freeGb = Math.Round(Convert.ToUInt64(disk["FreeSpace"]) / Math.Pow(1024, 3), 2);
                    return (total: $"{totalGb} GB", free: $"{freeGb} GB");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetDiskInfoAsync error for target {Target}", target);
                return ("Ошибка", "Ошибка");
            }
            return ("Не найден", "Не найден");
        }

        public async Task<string> GetRamAsync(string hostname, string? targetIp = null)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "Ошибка пароля";

            string target = !string.IsNullOrEmpty(targetIp) && !targetIp.Contains("найден") ? targetIp : hostname;
            string script = $@"
                $cred = if ('{target}' -notin @('localhost', '.', $env:COMPUTERNAME)) {{
                    $sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                    New-Object System.Management.Automation.PSCredential('{hostname}\ruadmin', $sec)
                }} else {{ $null }}
                $params = @{{ Class = 'Win32_PhysicalMemory'; ComputerName = '{target}'; ErrorAction = 'Stop' }}
                if ($cred) {{ $params.Credential = $cred; $params.Impersonation = 'Impersonate'; $params.Authentication = 'PacketPrivacy' }}
                $sum = (Get-WmiObject @params | Measure-Object -Property Capacity -Sum).Sum
                [Math]::Round($sum / 1GB)";
            script = _ps.InjectUserVariable(script);
            try
            {
                string result = (await _ps.RunScriptAsync(script)).Trim();
                return string.IsNullOrEmpty(result) ? "0" : result + " GB";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRamAsync error for target {Target}", target);
                return "Ошибка";
            }
        }

        public async Task<string> GetMacAddressAsync(string hostname)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "";

            string script = $@"
                    $cred = if ('{hostname}' -notin @('localhost', '.', $env:COMPUTERNAME)) {{
                        $sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                        New-Object System.Management.Automation.PSCredential('{hostname}\ruadmin', $sec)
                    }} else {{ $null }}
                    $params = @{{ Class = 'Win32_NetworkAdapterConfiguration'; ComputerName = '{hostname}' }}
                    if ($cred) {{ $params.Credential = $cred; $params.Impersonation = 'Impersonate'; $params.Authentication = 'PacketPrivacy' }}
                    $res = Get-WmiObject @params | Where-Object {{ $_.IPEnabled -eq $true }} | Select-Object -First 1
                    if ($res) {{ $res.MACAddress }} else {{ '' }}";
            script = _ps.InjectUserVariable(script);
            string result = (await _ps.RunScriptAsync(script)).Trim();
            return result;
        }

        public async Task<string> GetNetworkTypeAsync(string hostname)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "Ошибка пароля";

            string script = $@"
                $cred = if ('{hostname}' -notin @('localhost', '.', $env:COMPUTERNAME)) {{
                    $sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                    New-Object System.Management.Automation.PSCredential('{hostname}\ruadmin', $sec)
                }} else {{ $null }}
                $params = @{{ Class = 'Win32_NetworkAdapter'; ComputerName = '{hostname}'; ErrorAction = 'Stop' }}
                if ($cred) {{ $params.Credential = $cred; $params.Impersonation = 'Impersonate'; $params.Authentication = 'PacketPrivacy' }}
                $adapters = Get-WmiObject @params | Where-Object {{ $_.PhysicalAdapter -eq $true -and $_.NetConnectionID -ne $null }}
                foreach ($a in $adapters) {{
                    $status = if($a.NetConnectionStatus -eq 2) {{ 'Up' }} else {{ 'Down' }}
                    write-output ($a.NetConnectionID + ':' + $status)
                }}";
            script = _ps.InjectUserVariable(script);
            try
            {
                string output = (await _ps.RunScriptAsync(script)).Trim();
                if (string.IsNullOrEmpty(output)) return "Не определен";
                var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
                bool isLanUp = lines.Any(l => l.Contains("ethernet", StringComparison.CurrentCultureIgnoreCase) && l.EndsWith(":Up"));
                bool isWifiUp = lines.Any(l => (l.Contains("wi-fi", StringComparison.CurrentCultureIgnoreCase) || l.Contains("беспроводная", StringComparison.CurrentCultureIgnoreCase)) && l.EndsWith(":Up"));
                if (isLanUp) return "LAN";
                if (isWifiUp) return "WI-FI";
                return "Не определен";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetNetworkTypeAsync error for {Hostname}", hostname);
                return "Ошибка";
            }
        }

        public async Task<string> GetLastBootTimeAsync(string hostname)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "Ошибка пароля";

            string script = $@"
                $cred = if ('{hostname}' -notin @('localhost', '.', $env:COMPUTERNAME)) {{
                    $sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                    New-Object System.Management.Automation.PSCredential('{hostname}\ruadmin', $sec)
                }} else {{ $null }}
                $params = @{{ Class = 'Win32_OperatingSystem'; ComputerName = '{hostname}'; ErrorAction = 'Stop' }}
                if ($cred) {{ $params.Credential = $cred }}
                $wmi = Get-WmiObject @params
                $wmi.ConvertToDateTime($wmi.LastBootUpTime).ToString('dd.MM.yyyy HH:mm:ss')";
            script = _ps.InjectUserVariable(script);
            try
            {
                return (await _ps.RunScriptAsync(script)).Trim();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetLastBootTimeAsync error for {Hostname}", hostname);
                return "Ошибка времени";
            }
        }

        public async Task<string> GetOsInfoAsync(string hostname)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "Ошибка пароля";

            string script = $@"
                $cred = if ('{hostname}' -notin @('localhost', '.', $env:COMPUTERNAME)) {{
                    $sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                    New-Object System.Management.Automation.PSCredential('{hostname}\ruadmin', $sec)
                }} else {{ $null }}
                $params = @{{ Class = 'Win32_OperatingSystem'; ComputerName = '{hostname}' }}
                if ($cred) {{ $params.Credential = $cred }}
                $os = Get-WmiObject @params
                $caption = $os.Caption
                $version = $os.Version
                $text = ""$caption $version""
                $bytes = [System.Text.Encoding]::GetEncoding(1251).GetBytes($text)
                [Convert]::ToBase64String($bytes)";
            script = _ps.InjectUserVariable(script);
            string output = await _ps.RunScriptAsync(script);
            if (string.IsNullOrWhiteSpace(output)) return string.Empty;

            byte[] base64Bytes = Convert.FromBase64String(output.Trim());
            return Encoding.GetEncoding(1251).GetString(base64Bytes);
        }

        public async Task<string> GetBuildRevisionAsync(string hostname)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "Ошибка пароля";

            string script = $@"
                $HOSTNAME = ""{hostname}""
                $PASSWORD = '{password.Replace("'", "''")}'
                $USER = ""$HOSTNAME\RuAdmin""
                $params = @{{
                    Query = ""SELECT Version FROM CIM_DataFile WHERE Name = 'C:\\Windows\\System32\\ntoskrnl.exe'""
                    ErrorAction = 'Stop'
                }}
                $SecPassword = ConvertTo-SecureString $PASSWORD -AsPlainText -Force
                $params.Credential = New-Object System.Management.Automation.PSCredential(""$USER"", $SecPassword)
                $params.ComputerName = $HOSTNAME
                try {{
                    $File = Get-WmiObject @params
                    if ($File.Version) {{
                        $File.Version.Split('.')[-1]
                    }} else {{
                        ""Файл не найден или доступ ограничен""
                    }}
                }}
                catch {{
                    ""Ошибка подключения: $($_.Exception.Message)""
                }}";
            script = _ps.InjectUserVariable(script);
            return (await _ps.RunScriptAsync(script)).Trim();
        }

        public async Task<string> GetReleaseVersionAsync(string hostname)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "Ошибка пароля";

            string script = $@"
                $cred = if ('{hostname}' -notin @('localhost', '.', $env:COMPUTERNAME)) {{
                    $sec = ConvertTo-SecureString '{password.Replace("'", "''")}' -AsPlainText -Force
                    New-Object System.Management.Automation.PSCredential('{hostname}\ruadmin', $sec)
                }} else {{ $null }}
                $wmi = Get-WmiObject -Namespace 'root\default' -List -ClassName 'StdRegProv' -ComputerName '{hostname}' -Authentication PacketPrivacy -Impersonation Impersonate
                if ($cred) {{ $wmi = Get-WmiObject -Namespace 'root\default' -List -ClassName 'StdRegProv' -ComputerName '{hostname}' -Credential $cred -Authentication PacketPrivacy -Impersonation Impersonate }}
                $result = $wmi.GetStringValue(2147483650, 'SOFTWARE\Microsoft\Windows NT\CurrentVersion', 'DisplayVersion')
                $result.sValue";
            script = _ps.InjectUserVariable(script);
            return (await _ps.RunScriptAsync(script)).Trim();
        }

        public async Task<string> GetOs(string hostname)
        {
            if (string.IsNullOrWhiteSpace(hostname))
                return "Ошибка: Имя хоста не указано";

            string? os = null, rev = null, rel = null;
            var errors = new List<string>();

            try { os = await GetOsInfoAsync(hostname); }
            catch (Exception ex) { errors.Add($"OS: {ex.Message}"); _logger.LogWarning(ex, "GetOsInfo failed for {Hostname}", hostname); }

            try { rev = await GetBuildRevisionAsync(hostname); }
            catch (Exception ex) { errors.Add($"Rev: {ex.Message}"); _logger.LogWarning(ex, "GetBuildRevision failed for {Hostname}", hostname); }

            try { rel = await GetReleaseVersionAsync(hostname); }
            catch (Exception ex) { errors.Add($"Rel: {ex.Message}"); _logger.LogWarning(ex, "GetReleaseVersion failed for {Hostname}", hostname); }

            if (string.IsNullOrEmpty(os) && string.IsNullOrEmpty(rev) && string.IsNullOrEmpty(rel))
                return $"Ошибка: {string.Join(", ", errors)}";

            var result = new StringBuilder();
            if (!string.IsNullOrEmpty(os)) result.Append(os);
            if (!string.IsNullOrEmpty(rev)) result.Append($".{rev}");
            if (!string.IsNullOrEmpty(rel)) result.Append($" ({rel})");

            return result.ToString().Trim();
        }
    }
}