using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Serilog.Context;
using System.Text.Json;
using WebMetroSR.Helpers;
using WebMetroSR.Interfaces;
using WebMetroSR.Models;
using WebMetroSR.Services;

namespace WebMetroSR.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowAll")]
    public class AdController(
        ILogger<AdController> logger,
            IPowerShellService powerShellService,
            IWmiService wmiService,
            IMetroScopeService metroScopeService,
            IAdService adService,
            IPrintersService printersService,
            ISccmService sccmService,
            IFileService fileService,
            ICacheService cacheService,
            IUserCredentialService userCreds) : BaseApiController
    {
        private readonly ILogger<AdController> _logger = logger;
        private readonly IPowerShellService _ps = powerShellService;
        private readonly IWmiService _wmi = wmiService;
        private readonly IMetroScopeService _metro = metroScopeService;
        private readonly IAdService _ad = adService;
        private readonly IPrintersService _printers = printersService;
        private readonly ISccmService _sccm = sccmService;
        private readonly IFileService _file = fileService;
        private readonly ICacheService _cache = cacheService;
        private readonly IUserCredentialService _userCreds = userCreds;

        [HttpGet("containers")]
        public async Task<IActionResult> GetContainers()
        {
            // Этот метод можно оставить открытым (без авторизации), т.к. он используется для построения дерева до входа
            string targetPath = "OU=stores,OU=computers,OU=MCC,DC=ru,DC=rumadm,DC=net";
            string script = $@"
                Import-Module ActiveDirectory -ErrorAction Stop
                $i = 0; 
                Get-ADObject -Filter 'ObjectClass -eq ""container"" -or ObjectClass -eq ""organizationalUnit""' -SearchBase '{targetPath}' -SearchScope OneLevel | 
                Select-Object Name, DistinguishedName | 
                Where-Object {{ $global:i++; $global:i -ne 2 }} | 
                ConvertTo-Json";
            try
            {
                var (output, error, exitCode) = await _ps.RunScriptAsyncWithStatus(script);
                if (exitCode != 0) return StatusCode(500, error);
                return Content(output, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetContainers failed");
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("hosts")]
        public async Task<IActionResult> GetHosts([FromQuery] string dn)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((dn, nameof(dn)));
            if (validation != null) return validation;

            var hosts = await _ad.GetHostsAsync(dn);
            return Ok(hosts);
        }

        [HttpGet("find-host")]
        public async Task<IActionResult> FindHost(string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            var result = await _ad.FindHostAsync(hostname);
            return Ok(new
            {
                success = result.Success,
                containerDn = result.ContainerDn,
                hostName = result.HostName ?? hostname.ToUpper(),
                errorMessage = result.ErrorMessage
            });
        }

        [HttpGet("GetBasicInfo")]
        public async Task<IActionResult> GetBasicInfo(string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string cacheKey = CacheKeys.BasicInfoKey(hostname);
            if (_cache.TryGet<object>(cacheKey, out var cached))
                return Ok(cached);

            var lapsTask = _ps.ExecuteLapsCommandAsync(hostname);
            var metroTask = Task.Run(() => _metro.GetIpFromMetroScope(hostname));
            await Task.WhenAll(lapsTask, metroTask);

            string password = await lapsTask;
            string ip = await metroTask;

            if (string.IsNullOrEmpty(ip) || ip.Contains("Ошибка") || ip.Contains("Не найден"))
                ip = await _ad.GetSccmIpAsync(hostname);

            var result = new { success = true, ip = ip ?? "0.0.0.0" };
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
            return Ok(result);
        }

        [HttpGet("get-mac")]
        public async Task<IActionResult> GetMac(string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string mac = await _wmi.GetMacAddressAsync(hostname);
            return Ok(new { mac });
        }

        [HttpGet("get-model")]
        public async Task<IActionResult> GetModel(string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string model = await _wmi.GetWmiPropertyAsync(hostname, "Win32_Baseboard", "Manufacturer");
            return Ok(new { model });
        }

        [HttpGet("get-serial")]
        public async Task<IActionResult> GetSerial(string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string serial = await _wmi.GetWmiPropertyAsync(hostname, "Win32_Bios", "SerialNumber");
            return Ok(new { serial });
        }

        [HttpGet("get-os")]
        public async Task<IActionResult> GetOs(string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string os = await _wmi.GetOsInfoAsync(hostname);
            string rev = await _wmi.GetBuildRevisionAsync(hostname);
            string rel = await _wmi.GetReleaseVersionAsync(hostname);
            return Ok(new { os = $"{os}.{rev} ({rel})" });
        }

        [HttpGet("get-lastboot")]
        public async Task<IActionResult> GetLastBoot(string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string boot = await _wmi.GetLastBootTimeAsync(hostname);
            return Ok(new { lastBoot = boot });
        }

        [HttpGet("get-disk")]
        public async Task<IActionResult> GetDisk(string hostname, string ip)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            var (total, free) = await _wmi.GetDiskInfoAsync(hostname, ip);
            return Ok(new { hddTotal = total, hddFree = free });
        }

        [HttpGet("get-ram")]
        public async Task<IActionResult> GetRam(string hostname, string ip)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string ram = await _wmi.GetRamAsync(hostname, ip);
            return Ok(new { ram });
        }

        [HttpGet("get-nettype")]
        public async Task<IActionResult> GetNetType(string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string netType = await _wmi.GetNetworkTypeAsync(hostname);
            return Ok(new { netType });
        }

        [HttpGet("currentuser")]
        public async Task<IActionResult> GetCurrentUser(string hostname, string ip)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string user = await _ad.GetCurrentUserAsync(hostname, ip);
            return Ok(new { user });
        }

        [HttpGet("GetSwitchPort")]
        public async Task<IActionResult> GetSwitchPortApi(string ip)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            if (string.IsNullOrEmpty(ip) || ip == "Offline")
                return Ok(new { success = false, port = "IP не определен" });

            string result = await Task.Run(() => _metro.GetSwitchPortFromMetroScope(ip));
            return Ok(new { success = true, port = result });
        }

        [HttpGet("groups")]
        public async Task<IActionResult> GetUserGroups([FromQuery] string userName)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((userName, nameof(userName)));
            if (validation != null) return validation;

            var groups = await _ad.GetUserGroupsAsync(userName);
            return Ok(groups ?? []);
        }

        [HttpGet("get-printer-drivers")]
        public async Task<IActionResult> GetPrinterDrivers([FromQuery] string targetHost)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((targetHost, nameof(targetHost)));
            if (validation != null) return validation;

            var drivers = await _printers.GetPrinterDriversAsync(targetHost);
            return Ok(drivers);
        }

        [HttpGet("get-printers")]
        public async Task<IActionResult> GetPrinters([FromQuery] string targetHost)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((targetHost, nameof(targetHost)));
            if (validation != null) return validation;

            try
            {
                var printers = await _printers.GetPrintersAsync(targetHost);
                return Ok(printers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting printers for {TargetHost}", targetHost);
                return Ok(new List<PrinterInfo>());
            }
        }

        [HttpPost("connect-printer")]
        public async Task<IActionResult> ConnectPrinter(
            [FromForm] string targetHost,
            [FromForm] string ipPrint,
            [FromForm] string driverFullName,
            [FromForm] string printName)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired(
                (targetHost, nameof(targetHost)),
                (ipPrint, nameof(ipPrint)),
                (driverFullName, nameof(driverFullName)),
                (printName, nameof(printName)));
            if (validation != null) return validation;

            try
            {
                var (success, output) = await _printers.ConnectPrinterAsync(targetHost, ipPrint, driverFullName, printName);

                var clientIP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                string user;
                try { user = _userCreds.GetUsername() ?? "anonymous"; } catch { user = "anonymous"; }
                var requestName = "ConnectPrinter";
                var resultMessage = success ? "Success" : $"Error: {output}";

                _logger.LogInformation("ConnectPrinter result for {TargetHost}: {Output}", targetHost, output);

                using (LogContext.PushProperty("Category", "Change"))
                using (LogContext.PushProperty("ClientIP", clientIP))
                using (LogContext.PushProperty("User", user))
                using (LogContext.PushProperty("RequestName", requestName))
                using (LogContext.PushProperty("ResultMessage", resultMessage))
                {
                    _logger.LogInformation("Change action executed");
                }

                return Ok(new { success, output });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConnectPrinter exception");
                return Ok(new { success = false, message = $"Ошибка: {ex.Message}" });
            }
        }

        [HttpGet("check-printer-ip")]
        public async Task<IActionResult> CheckPrinterIp([FromQuery] string ip)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((ip, nameof(ip)));
            if (validation != null) return validation;

            try
            {
                bool isPrinter = await _printers.IsPrinterByIpAsync(ip);
                return Ok(new { success = true, isPrinter });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckPrinterIp failed for {Ip}", ip);
                return Ok(new { success = false, isPrinter = false, error = ex.Message });
            }
        }

        // Ниже добавляем ещё один эндпоинт для задачи 3 (проверка онлайна хоста)
        [HttpGet("check-host-online")]
        public async Task<IActionResult> CheckHostOnline([FromQuery] string ip)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((ip, nameof(ip)));
            if (validation != null) return validation;

            try
            {
                bool online = await _ad.IsHostOnlineAsync(ip, 445, 3000);
                return Ok(new { success = true, online });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CheckHostOnline failed for {Ip}", ip);
                return Ok(new { success = false, online = false, error = ex.Message });
            }
        }

        [HttpGet("remote-devices")]
        public async Task<IActionResult> GetRemoteDevices([FromQuery] string targetHost)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((targetHost, nameof(targetHost)));
            if (validation != null) return validation;

            string password = await _ps.ExecuteLapsCommandAsync(targetHost);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return Ok("[]");

            string script = $@"
                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                $ComputerName = ""{targetHost}""
                $PASSWORD = '{password.Replace("'", "''")}'
                $SecurePassword = ConvertTo-SecureString $PASSWORD -AsPlainText -Force
                $Cred = New-Object System.Management.Automation.PSCredential (""$ComputerName\RuAdmin"", $SecurePassword)
                $TargetClasses = @(""AudioEndpoint"", ""MEDIA"", ""Camera"", ""Keyboard"", ""Mouse"", ""USB"", ""Monitor"", ""PrintQueue"", ""Printer"", ""Net"", ""HIDClass"", ""Ports"", ""Image"", ""Scanner"", ""WSDPrintDevice"", ""Bluetooth"")
                $devices = Get-WmiObject -Class Win32_PnPEntity -ComputerName $ComputerName -Credential $Cred -Impersonation Impersonate -Authentication PacketPrivacy | 
                           Where-Object {{ $_.PNPClass -in $TargetClasses -or $_.Caption -match ""scan|scanner|оцифров|imaging|blue|зуб"" }} |
                           Select-Object @{{Name=""Category""; Expression={{$_.PNPClass}}}}, 
                                         @{{Name=""Name""; Expression={{$_.Caption}}}}, 
                                         @{{Name=""ConnectionType""; Expression={{ 
                                            if ($_.DeviceID -like ""*BTHENUM*"" -or $_.DeviceID -like ""*BTH\*"" ) {{ ""Bluetooth"" }} else {{ ""Физическое"" }} 
                                         }}}}
                $devices | Sort-Object Category | ConvertTo-Json -Compress";
            // Внедряем переменную $InitiatingUser
            script = _ps.InjectUserVariable(script);
            string output = await _ps.RunScriptAsync(script);
            string decoded = _ps.DecodeOutput(output);
            return Content(decoded, "application/json; charset=utf-8", System.Text.Encoding.UTF8);
        }

        [HttpGet("installed-apps")]
        public async Task<IActionResult> GetInstalledSoftware([FromQuery] string hostname, [FromQuery] string ip)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)), (ip, nameof(ip)));
            if (validation != null) return validation;

            string password = await _ps.ExecuteLapsCommandAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return Ok(new List<SoftwareInfo>());

            string script = $@"
                            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                            $ComputerName = ""{ip}""
                            $PASSWORD = '{password.Replace("'", "''")}'
                            $USER = ""$ComputerName\RuAdmin""
                            $SecPassword = ConvertTo-SecureString $PASSWORD -AsPlainText -Force
                            $cred = New-Object System.Management.Automation.PSCredential(""$USER"", $SecPassword)
                            $HKLM = [uint32]2147483650
                            $RegistryPaths = @(
                                ""SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"",
                                ""SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall""
                            )
                            $reg = Get-WmiObject -List -Namespace ""root\default"" -ComputerName $ComputerName -Credential $cred -Impersonation Impersonate -Authentication PacketPrivacy | 
                                   Where-Object {{ $_.Name -eq ""StdRegProv"" }}
                            $results = foreach ($path in $RegistryPaths) {{
                                $subKeys = $reg.EnumKey($HKLM, $path).sNames
                                foreach ($keyName in $subKeys) {{
                                    $fullPath = ""$path\$keyName""
                                    $displayName = $reg.GetStringValue($HKLM, $fullPath, ""DisplayName"").sValue
                                    if ($displayName) {{
                                        [PSCustomObject]@{{
                                            DisplayName  = $displayName
                                            Version      = $reg.GetStringValue($HKLM, $fullPath, ""DisplayVersion"").sValue
                                        }}
                                    }}
                                }}
                            }}
                            $results | Sort-Object DisplayName | ConvertTo-Json -Compress";
            script = _ps.InjectUserVariable(script);

            try
            {
                // Увеличенный таймаут до 180 секунд
                string output = await _ps.RunScriptAsync(script, timeoutSeconds: 180);
                string decoded = _ps.DecodeOutput(output);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var list = JsonSerializer.Deserialize<List<SoftwareInfo>>(decoded, options);
                return Ok(list ?? []);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetInstalledSoftware failed for {Hostname} with IP {Ip}", hostname, ip);
                return Ok(new List<SoftwareInfo>());
            }
        }

        public class CleanMsiRequest
        {
            public string? TargetHost { get; set; }
        }

        [HttpPost("clean-msi-msp")]
        public async Task<IActionResult> CleanMsiMsp([FromBody] CleanMsiRequest request)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var targetHost = request?.TargetHost;
            var validation = EnsureRequired((targetHost ?? "", nameof(targetHost)));
            if (validation != null) return validation;

            var (success, output) = await _file.CleanMsiMspAsync(targetHost!);

            var clientIP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string user;
            try { user = _userCreds.GetUsername() ?? "anonymous"; } catch { user = "anonymous"; }
            var requestName = "CleanMsiMsp";
            var resultMessage = success ? "Success" : $"Error: {output}";

            _logger.LogInformation("CleanMsiMsp result for {TargetHost}: {Output}", targetHost, output);

            using (LogContext.PushProperty("Category", "Change"))
            using (LogContext.PushProperty("ClientIP", clientIP))
            using (LogContext.PushProperty("User", user))
            using (LogContext.PushProperty("RequestName", requestName))
            using (LogContext.PushProperty("ResultMessage", resultMessage))
            {
                _logger.LogInformation("Change action executed");
            }

            return Ok(new { success, output });
        }

        [HttpGet("read-kixtart-log")]
        public Task<IActionResult> GetKixtartLog([FromQuery] string host, [FromQuery] string ip)
            => _file.ReadRemoteLogAsync(host, ip, @"\Temp\log_kixtart.log");

        [HttpGet("route")]
        public async Task<IActionResult> GetRoute([FromQuery] string hostname, [FromQuery] string ip, CancellationToken cancellationToken)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)), (ip, nameof(ip)));
            if (validation != null) return validation;

            try
            {
                string output = await _file.GetRouteAsync(hostname, ip, cancellationToken);
                return Ok(new { success = true, output });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRoute failed for {Host}/{Ip}", hostname, ip);
                return Ok(new { success = false, output = ex.Message });
            }
        }

        [HttpGet("traceroute")]
        public async Task<IActionResult> GetTraceroute([FromQuery] string ip, CancellationToken cancellationToken)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((ip, nameof(ip)));
            if (validation != null) return validation;

            try
            {
                string output = await _file.GetTracerouteAsync(ip, cancellationToken);
                return Ok(new { success = true, output });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetTraceroute failed for {Ip}", ip);
                return Ok(new { success = false, output = ex.Message });
            }
        }

        [HttpGet("ipconfig")]
        public async Task<IActionResult> GetIpConfig([FromQuery] string hostname, [FromQuery] string ip, CancellationToken cancellationToken)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)), (ip, nameof(ip)));
            if (validation != null) return validation;

            try
            {
                string output = await _file.GetIpConfigAsync(hostname, ip, cancellationToken);
                return Ok(new { success = true, output });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetIpConfig failed for {Host}/{Ip}", hostname, ip);
                return Ok(new { success = false, output = ex.Message });
            }
        }

        [HttpGet("get-bios-version")]
        public async Task<IActionResult> GetBiosVersion(string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            string biosVersion = await _wmi.GetWmiPropertyAsync(hostname, "Win32_BIOS", "SMBIOSBIOSVersion");
            return Ok(new { biosVersion });
        }

        [HttpGet("check-client-active")]
        public async Task<IActionResult> CheckClientActive([FromQuery] string hostname)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((hostname, nameof(hostname)));
            if (validation != null) return validation;

            bool isActive = await _sccm.IsClientActiveAsync(hostname);
            return Ok(new { success = true, isActive });
        }

        [HttpGet("find-host-by-serial")]
        public async Task<IActionResult> FindHostBySerial(string serial)
        {
            var auth = EnsureAuthenticated(_userCreds);
            if (auth != null) return auth;

            var validation = EnsureRequired((serial, nameof(serial)));
            if (validation != null) return validation;

            var hostname = await _ad.GetHostnameBySerialAsync(serial);
            return Ok(new
            {
                success = !string.IsNullOrEmpty(hostname),
                hostName = hostname,
                errorMessage = hostname == null
                    ? $"Устройство с серийным номером {serial} не найдено в SCCM"
                    : null
            });
        }
    }
}