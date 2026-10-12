using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using WebMetroSR.Helpers;
using WebMetroSR.Interfaces;

namespace WebMetroSR.Services
{
    public class FileService(IPowerShellService powerShellService, ILogger<FileService> logger) : IFileService
    {
        private readonly IPowerShellService _powerShellService = powerShellService;
        private readonly ILogger<FileService> _logger = logger;

        private async Task<string> GetPasswordAsync(string hostname)
        {
            return await _powerShellService.ExecuteLapsCommandAsync(hostname);
        }

        public async Task<(bool success, string output)> CleanMsiMspAsync(string targetHost)
        {
            string password = await GetPasswordAsync(targetHost);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return (false, "Не удалось получить пароль LAPS");

            string targetPath = $@"\\{targetHost}\c$\Windows\Installer";
            string scriptFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DelMsiMsp.ps1");

            if (!File.Exists(scriptFilePath))
            {
                return (false, $"Файл скрипта не найден: {scriptFilePath}");
            }

            string escapedPassword = password.Replace("'", "''");

            string psCommand = $@"
                        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8;
                        $targetPath = '{targetPath}'
                        $username = '{targetHost}\RuAdmin'
                        $password = '{escapedPassword}'

                        # Отключаем возможные старые подключения, чтобы net use не подвис
                        cmd /c ""net use $targetPath /delete /y"" 2>$null | Out-Null

                        # Ограничиваем время ожидания SMB, чтобы не висеть минутами
                        $netResult = cmd /c ""net use $targetPath /user:$username $password"" 2>&1
                        if ($LASTEXITCODE -ne 0) {{
                            Write-Output ('Ошибка авторизации на удалённой машине: ' + ($netResult -join ' '))
                            exit 1
                        }}

                        & '{scriptFilePath}' -targetPath $targetPath | Where-Object {{ $_ -notmatch '^#< CLIXML' -and $_ -notmatch '^System.Management.Automation' -and $_ -notmatch '^\\\\.*was deleted successfully' }}

                        cmd /c ""net use $targetPath /delete /y"" 2>$null | Out-Null";

            psCommand = _powerShellService.InjectUserVariable(psCommand);

            var (output, error, exitCode) = await _powerShellService.RunScriptAsyncWithStatus(psCommand, timeoutSeconds: 300);

            string stdout = (output ?? "").Trim();
            string stderr = (error ?? "")
                .TrimStart('\uFEFF', '\u200B', '\r', '\n', ' ', '\t')
                .Trim();

            _logger.LogInformation(
                "CleanMsiMsp raw for {TargetHost}: exit={ExitCode}, stdoutLen={S}, stderrLen={E}",
                targetHost, exitCode, stdout.Length, stderr.Length);
            if (!string.IsNullOrEmpty(stderr))
                _logger.LogInformation("CleanMsiMsp stderr: [{Stderr}]", stderr);

            string combined = string.IsNullOrEmpty(stderr)
                ? stdout
                : (string.IsNullOrEmpty(stdout) ? stderr : stdout + "\n" + stderr);

            bool success = exitCode == 0
                           && !combined.Contains("Ошибка авторизации")
                           && !combined.Contains("PowerShell error");

            // ---------- Уборка служебного мусора ----------
            string cleanOutput = combined;
            cleanOutput = Regex.Replace(cleanOutput, @"\\\\[^\s]+\\c\$\\Windows\\Installer was deleted successfully\.", "");
            cleanOutput = Regex.Replace(cleanOutput, @"#< CLIXML[\s\S]*$", "");
            cleanOutput = Regex.Replace(cleanOutput, @"System\.Management\.Automation\.PSCustomObject.*$", "");
            cleanOutput = cleanOutput.Trim();

            if (success && string.IsNullOrEmpty(cleanOutput))
                return (false, "Скрипт отработал без вывода. Возможно, папка Installer не найдена или нет прав.");

            return (success, cleanOutput);
        }

        public async Task<IActionResult> ReadRemoteLogAsync(string hostname, string targetHost, string relativePath)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return new ObjectResult("Ошибка получения пароля LAPS") { StatusCode = 401 };

            string networkPath = $@"\\{targetHost}\c$";
            string filePath = $@"\\{targetHost}\c${relativePath}";
            string user = $@"{hostname}\RuAdmin";

            try
            {
                await RunNetCommandAsync($@"use {networkPath} /delete /y");
                int exitCode = await RunNetCommandAsync($@"use {networkPath} ""{password}"" /user:""{user}"" /persistent:no");

                if (exitCode != 0)
                    return new ObjectResult($"Ошибка авторизации на {targetHost}. Проверьте пароль.") { StatusCode = 401 };

                await Task.Delay(300);

                if (!File.Exists(filePath))
                    return new NotFoundObjectResult($"Файл не найден: {filePath}");

                string logContent = await File.ReadAllTextAsync(filePath, Encoding.GetEncoding("windows-1251"));
                await RunNetCommandAsync($@"use {networkPath} /delete /y");
                return new ContentResult { Content = logContent, ContentType = "text/plain; charset=windows-1251", StatusCode = 200 };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка чтения лога {FilePath} на {TargetHost}", filePath, targetHost);
                return new ObjectResult($"Ошибка при чтении лога: {ex.Message}") { StatusCode = 500 };
            }
        }

        private static async Task<int> RunNetCommandAsync(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo("net.exe", arguments)
                {
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };

                using var process = Process.Start(psi);

                if (process == null)
                    return -1; // Не удалось запустить процесс

                await process.WaitForExitAsync();
                return process.ExitCode;
            }
            catch (Exception)
            {
                // Любая ошибка (например, net.exe не найден, недостаточно прав)
                return -1;
            }
        }

        // ============ НОВЫЕ МЕТОДЫ ============

        public async Task<string> GetRouteAsync(string hostname, string ip, CancellationToken cancellationToken = default)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "Не удалось получить пароль LAPS";

            string escPass = password.Replace("'", "''");
            string escHost = hostname.Replace("'", "''");
            string escIp = ip.Replace("'", "''");

            // PowerShell-скрипт: подключение через DCOM CIM, вывод маршрутов
            string script = $@"
                            $ErrorActionPreference = 'Stop'
                            $computer = '{escIp}'
                            $username = '{escHost}\ruadmin'
                            $password = '{escPass}'

                            $secPass = ConvertTo-SecureString $password -AsPlainText -Force
                            $myCred  = New-Object System.Management.Automation.PSCredential($username, $secPass)

                            $option  = New-CimSessionOption -Protocol Dcom
                            $session = New-CimSession -ComputerName $computer -SessionOption $option -Credential $myCred

                            $out = New-Object System.Collections.Generic.List[string]

                            try {{
                                $adapters   = Get-CimInstance -CimSession $session -Namespace 'root\cimv2' -ClassName Win32_NetworkAdapterConfiguration | Where-Object {{ $_.IPAddress }}
                                $ipv4Routes = Get-CimInstance -CimSession $session -Namespace 'root\cimv2' -ClassName Win32_IP4RouteTable
                                $ipv6Routes = Get-CimInstance -CimSession $session -Namespace 'root\StandardCimv2' -ClassName MSFT_NetRoute | Where-Object {{ $_.AddressFamily -eq 23 }}

                                $out.Add('===========================================================================')
                                $out.Add('Список интерфейсов:')
                                foreach ($adapter in $adapters) {{
                                    $out.Add(('{{0,2}}... {{1,-50}} [{{2}}]' -f $adapter.InterfaceIndex, $adapter.Description, ($adapter.IPAddress -join ', ')))
                                }}

                                $out.Add('')
                                $out.Add('===========================================================================')
                                $out.Add('Активные маршруты IPv4:')
                                $out.Add('  Сетевой адрес          Маска сети      Адрес шлюза      Интерфейс  Метрика')
                                $out.Add('---------------------------------------------------------------------------')
                                foreach ($route in $ipv4Routes | Sort-Object Destination) {{
                                    $out.Add(('{{0,-15}} {{1,-15}} {{2,-15}} {{3,10}} {{4,8}}' -f $route.Destination, $route.Mask, $route.NextHop, $route.InterfaceIndex, $route.Metric1))
                                }}

                                $out.Add('')
                                $out.Add('===========================================================================')
                                $out.Add('Постоянные маршруты IPv4:')
                                $persistentV4 = $ipv4Routes | Where-Object {{ $_.Type -eq 3 }}
                                if ($persistentV4) {{
                                    $out.Add('  Сетевой адрес          Маска сети      Адрес шлюза      Метрика')
                                    $out.Add('---------------------------------------------------------------------------')
                                    foreach ($route in $persistentV4 | Sort-Object Destination) {{
                                        $out.Add(('{{0,-15}} {{1,-15}} {{2,-15}} {{3,8}}' -f $route.Destination, $route.Mask, $route.NextHop, $route.Metric1))
                                    }}
                                }} else {{
                                    $out.Add('  Постоянные маршруты IPv4 отсутствуют.')
                                }}

                                $out.Add('')
                                $out.Add('===========================================================================')
                                $out.Add('Активные маршруты IPv6:')
                                $out.Add('  Иф  Метрика   Сетевой префикс                           Шлюз')
                                $out.Add('---------------------------------------------------------------------------')
                                foreach ($route in $ipv6Routes | Sort-Object DestinationPrefix) {{
                                    $nextHop = if ([string]::IsNullOrEmpty($route.NextHop) -or $route.NextHop -eq '::') {{ 'Cвязанный (On-link)' }} else {{ $route.NextHop }}
                                    $out.Add(('{{0,3}} {{1,8}}   {{2,-40}} {{3}}' -f $route.InterfaceIndex, $route.RouteMetric, $route.DestinationPrefix, $nextHop))
                                }}
                                $out.Add('===========================================================================')
                            }}
                            finally {{
                                if ($session) {{ Remove-CimSession $session -ErrorAction SilentlyContinue }}
                            }}

                            $out | Out-String";
            script = _powerShellService.InjectUserVariable(script);
            try
            {
                string output = await _powerShellService.RunScriptAsync(script, timeoutSeconds: 120, cancellationToken: cancellationToken);
                return _powerShellService.DecodeOutput(output);
            }
            catch (OperationCanceledException) { return "Запрос отменён"; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetRouteAsync failed for {Host}/{Ip}", hostname, ip);
                return $"Ошибка: {ex.Message}";
            }
        }

        public async Task<string> GetTracerouteAsync(string ip, CancellationToken cancellationToken = default)
        {
            string escIp = ip.Replace("'", "''");
            string script = $@"
                            [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                            tracert -d -w 1000 {escIp}";
            try
            {
                string output = await _powerShellService.RunScriptAsync(script, timeoutSeconds: 120, cancellationToken: cancellationToken);
                return _powerShellService.DecodeOutput(output);
            }
            catch (OperationCanceledException) { return "Запрос отменён"; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetTracerouteAsync failed for {Ip}", ip);
                return $"Ошибка: {ex.Message}";
            }
        }

        public async Task<string> GetIpConfigAsync(string hostname, string ip, CancellationToken cancellationToken = default)
        {
            string password = await GetPasswordAsync(hostname);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return "Не удалось получить пароль LAPS";

            string escPass = password.Replace("'", "''");
            string escHost = hostname.Replace("'", "''");
            string escIp = ip.Replace("'", "''");

            string script = $@"
                            $ProgressPreference = 'SilentlyContinue'
                            $ErrorActionPreference = 'Stop'

                            $computer = '{escIp}'
                            $username = '{escHost}\ruadmin'
                            $password = '{escPass}'

                            $secPass = ConvertTo-SecureString $password -AsPlainText -Force
                            $myCred  = New-Object System.Management.Automation.PSCredential($username, $secPass)

                            $option  = New-CimSessionOption -Protocol Dcom
                            $session = New-CimSession -ComputerName $computer -SessionOption $option -Credential $myCred

                            $out = New-Object System.Collections.Generic.List[string]

                            # Ширина разделителя подобрана под типовой ipconfig
                            $line = '=' * 78
                            $thin = '-' * 78

                            function Add-Header($list, $title) {{
                                $list.Add('')
                                $list.Add($line)
                                $list.Add(""  $title"")
                                $list.Add($line)
                            }}
                            function Add-SubHeader($list, $title) {{
                                $list.Add('')
                                $list.Add($thin)
                                $list.Add(""  $title"")
                                $list.Add($thin)
                            }}

                            try {{
                                $ipConfigs = Get-CimInstance -CimSession $session -Namespace 'root\cimv2' -ClassName Win32_NetworkAdapterConfiguration | Where-Object {{ $_.MACAddress }}
                                $sysConfig = $ipConfigs | Where-Object {{ $_.DNSHostName }} | Select-Object -First 1

                                # ---- Тип узла ----
                                $nodeType = 'Смешанный'
                                try {{
                                    $regResult = Invoke-CimMethod -CimSession $session -Namespace 'root\default' -ClassName 'StdRegProv' -MethodName 'GetDWORDValue' -Arguments @{{
                                        hDefKey     = [uint32]2147483650
                                        sSubKeyName = 'System\CurrentControlSet\Services\NetBT\Parameters'
                                        sValueName  = 'NodeType'
                                    }} -ErrorAction Stop
                                    $nodeTypes = @{{ 1 = 'Одноранговый'; 2 = 'Широковещательный'; 4 = 'Смешанный'; 8 = 'Гибридный' }}
                                    if ($regResult.uValue -and $nodeTypes[[int]$regResult.uValue]) {{
                                        $nodeType = $nodeTypes[[int]$regResult.uValue]
                                    }}
                                }} catch {{ }}

                                Add-Header $out 'Настройка протокола IP для Windows'
                                $out.Add('')
                                $out.Add(""   Имя компьютера  . . . . . . . . . : $($sysConfig.DNSHostName)"")
                                $out.Add(""   Основной DNS-суффикс  . . . . . . : $($sysConfig.DNSDomain)"")
                                $out.Add(""   Тип узла. . . . . . . . . . . . . : $nodeType"")
                                $out.Add(""   IP-маршрутизация включена . . . . : $(if($sysConfig.IPForwardingEnabled){{'Да'}}else{{'Нет'}})"")
                                $out.Add(""   WINS-прокси включен . . . . . . . : $(if($sysConfig.WinsProxyEnabled){{'Да'}}else{{'Нет'}})"")

                                if ($sysConfig.DNSDomainSuffixSearchOrder) {{
                                    $out.Add(""   Порядок просмотра суффиксов DNS . : $($sysConfig.DNSDomainSuffixSearchOrder -join "","")"")
                                }} else {{
                                    $out.Add('   Порядок просмотра суффиксов DNS . :')
                                }}

                                # ============================================================
                                # АДАПТЕРЫ
                                # ============================================================
                                foreach ($config in $ipConfigs) {{
                                    $mac = if ($config.MACAddress) {{ $config.MACAddress.Replace(':', '-').ToUpper() }} else {{ '' }}

                                    $adapterType = 'Адаптер Ethernet'
                                    if ($config.Description -like '*Wi-Fi*' -or $config.Description -like '*Wireless*' -or $config.Description -like '*802.11*') {{
                                        $adapterType = 'Адаптер беспроводной локальной сети'
                                    }} elseif ($config.Description -like '*Bluetooth*') {{
                                        $adapterType = 'Адаптер Ethernet Bluetooth Device'
                                    }}

                                    # Заголовок адаптера как подзаголовок с линией
                                    Add-SubHeader $out ""$adapterType $($config.Description)""
                                    $out.Add('')

                                    $hasIp = [bool]$config.IPAddress
                                    if (-not $hasIp) {{
                                        $out.Add('   Состояние среды. . . . . . . . . : Среда передачи недоступна.')
                                        $out.Add('')
                                    }}

                                    $out.Add(""   DNS-суффикс подключения . . . . . : $($config.DNSDomain)"")
                                    $out.Add(""   Описание. . . . . . . . . . . . . : $($config.Description)"")
                                    $out.Add(""   Физический адрес. . . . . . . . . : $mac"")
                                    $out.Add(""   DHCP включен. . . . . . . . . . . : $(if($config.DHCPEnabled){{'Да'}}else{{'Нет'}})"")
                                    $out.Add(""   Автонастройка включена. . . . . . : $(if($config.DHCPEnabled){{'Да'}}else{{'Нет'}})"")

                                    if ($hasIp) {{
                                        $ipv4 = @(); $ipv6 = @()
                                        foreach ($a in $config.IPAddress) {{
                                            if ($a -like '*:*') {{ $ipv6 += $a }} else {{ $ipv4 += $a }}
                                        }}

                                        foreach ($v6 in $ipv6) {{
                                            $out.Add(""   IPv6-адрес. . . . . . . . . . . . : $v6"")
                                        }}

                                        for ($i = 0; $i -lt $ipv4.Count; $i++) {{
                                            $out.Add(""   IPv4-адрес. . . . . . . . . . . . : $($ipv4[$i])(Основной)"")
                                            if ($config.IPSubnet[$i] -and $config.IPSubnet[$i] -notlike '*:*') {{
                                                $out.Add(""   Маска подсети . . . . . . . . . . : $($config.IPSubnet[$i])"")
                                            }}
                                        }}

                                        if ($config.DHCPEnabled -and $config.DHCPLeaseObtained) {{
                                            $culture = [System.Globalization.CultureInfo]::GetCultureInfo('ru-RU')
                                            try {{
                                                if ($config.DHCPLeaseObtained -notlike '00000000*') {{
                                                    $dtO = [Management.ManagementDateTimeConverter]::ToDateTime($config.DHCPLeaseObtained)
                                                    $out.Add(""   Аренда получена. . . . . . . . . . : $($dtO.ToString('d MMMM yyyy г. H:mm:ss', $culture))"")
                                                }}
                                            }} catch {{ }}
                                            try {{
                                                if ($config.DHCPLeaseExpires -notlike '00000000*') {{
                                                    $dtE = [Management.ManagementDateTimeConverter]::ToDateTime($config.DHCPLeaseExpires)
                                                    $out.Add(""   Срок аренды истекает. . . . . . . : $($dtE.ToString('d MMMM yyyy г. H:mm:ss', $culture))"")
                                                }}
                                            }} catch {{ }}
                                        }}

                                        if ($config.DefaultIPGateway) {{
                                            $out.Add(""   Основной шлюз. . . . . . . . . . : $($config.DefaultIPGateway -join ', ')"")
                                        }}

                                        if ($config.DHCPEnabled -and $config.DHCPServer) {{
                                            $out.Add(""   DHCP-сервер. . . . . . . . . . . : $($config.DHCPServer)"")
                                        }}

                                        if ($config.DNSServerSearchOrder) {{
                                            $out.Add(""   DNS-серверы. . . . . . . . . . . : $($config.DNSServerSearchOrder -join ', ')"")
                                        }}

                                        $netbiosStatus = switch ($config.NetBIOSOptions) {{
                                            0 {{ 'Включен через DHCP' }}
                                            1 {{ 'Включен' }}
                                            2 {{ 'Отключен' }}
                                            default {{ 'Включен' }}
                                        }}
                                        $out.Add(""   NetBios через TCP/IP. . . . . . . : $netbiosStatus"")
                                    }}
                                }}

                                $out.Add('')
                                $out.Add($line)
                                $out.Add('')
                            }}
                            finally {{
                                if ($session) {{ Remove-CimSession $session -ErrorAction SilentlyContinue }}
                            }}

                            $out -join ""`n""";
            script = _powerShellService.InjectUserVariable(script);
            try
            {
                string output = await _powerShellService.RunScriptAsync(script, timeoutSeconds: 120, cancellationToken: cancellationToken);
                return CommonHelpers.CleanPowerShellOutput(_powerShellService.DecodeOutput(output));
            }
            catch (OperationCanceledException) { return "Запрос отменён"; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetIpConfigAsync failed for {Host}/{Ip}", hostname, ip);
                return $"Ошибка: {ex.Message}";
            }
        }
    }
}