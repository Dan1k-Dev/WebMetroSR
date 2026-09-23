using System.Text.Json;
using WebMetroSR.Helpers;
using WebMetroSR.Interfaces;
using WebMetroSR.Models;

namespace WebMetroSR.Services
{
    public class PrintersService(IPowerShellService powerShellService, ILogger<PrintersService> logger) : IPrintersService
    {
        private readonly IPowerShellService _powerShellService = powerShellService;
        private readonly ILogger<PrintersService> _logger = logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private async Task<string> GetPasswordAsync(string hostname)
        {
            return await _powerShellService.ExecuteLapsCommandAsync(hostname);
        }

        public async Task<List<string>> GetPrinterDriversAsync(string targetHost)
        {
            string password = await GetPasswordAsync(targetHost);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return [];

            string psScript = $@"
                $ProgressPreference = 'SilentlyContinue'
                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                $HOSTNAME = ""{targetHost}""
                $PASSWORD = '{password.Replace("'", "''")}'
    
                $SecurePassword = ConvertTo-SecureString $PASSWORD -AsPlainText -Force
                $Cred = New-Object System.Management.Automation.PSCredential (""$HOSTNAME\ruadmin"", $SecurePassword)
    
                $fromPrinters = Get-WmiObject -Class Win32_Printer -ComputerName $HOSTNAME -Credential $Cred -Impersonation Impersonate -Authentication PacketPrivacy | 
                                Where-Object {{ $_.Name -notlike ""*OneNote*"" -and $_.Name -notlike ""*Fax*"" -and $_.Name -notlike ""*XPS*"" -and $_.Name -notlike ""*PDF*"" }} |
                                Select-Object -ExpandProperty DriverName
    
                $fromDrivers = Get-WmiObject -Class Win32_PrinterDriver -ComputerName $HOSTNAME -Credential $Cred -Impersonation Impersonate -Authentication PacketPrivacy | 
                               ForEach-Object {{ $_.Name.Split(',')[0] }}
    
                $result = @($fromPrinters; $fromDrivers) | Where-Object {{ $_ }} | Sort-Object -Unique
                @($result) | ConvertTo-Json -Compress";
            psScript = _powerShellService.InjectUserVariable(psScript);
            var (output, error, exitCode) = await _powerShellService.RunScriptAsyncWithStatus(psScript);
            if (exitCode != 0 || !string.IsNullOrEmpty(error))
            {
                _logger.LogError("GetPrinterDriversAsync failed. ExitCode: {ExitCode}, Error: {Error}", exitCode, error);
                return [];
            }
            if (string.IsNullOrWhiteSpace(output)) return [];

            var jsonPart = CommonHelpers.ExtractJson(output);
            try
            {
                return JsonSerializer.Deserialize<List<string>>(jsonPart, _jsonOptions) ?? [];
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON error in GetPrinterDriversAsync. Output: {Output}", jsonPart);
                return [];
            }
        }

        public async Task<List<PrinterInfo>> GetPrintersAsync(string targetHost)
        {
            string password = await GetPasswordAsync(targetHost);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return [];

            string psScript = $@"
                        [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                        $ProgressPreference = 'SilentlyContinue'
                        $HOSTNAME = ""{targetHost}""
                        $PASSWORD = '{password.Replace("'", "''")}'

                        $SecurePassword = ConvertTo-SecureString $PASSWORD -AsPlainText -Force
                        $Cred = New-Object System.Management.Automation.PSCredential ('$HOSTNAME\ruadmin', $SecurePassword)

                        $FullList = @(
                            Get-WmiObject -Class Win32_TcpIpPrinterPort -ComputerName $HOSTNAME -Credential $Cred -Impersonation Impersonate -Authentication PacketPrivacy | 
                                Select-Object Name, @{{Name='Info'; Expression={{$_.HostAddress}}}}, @{{Name='Type'; Expression={{'Port'}}}}
                            Get-WmiObject -ComputerName $HOSTNAME -Credential $Cred -ClassName Win32_Printer -Impersonation Impersonate -Authentication PacketPrivacy | 
                                Select-Object Name, @{{Name='Info'; Expression={{$_.PortName}}}}, @{{Name='Type'; Expression={{'Printer'}}}}
                        )
                        $FullList | ConvertTo-Json -Compress";
            psScript = _powerShellService.InjectUserVariable(psScript);
            var (output, error, exitCode) = await _powerShellService.RunScriptAsyncWithStatus(psScript);

            string decodedOutput = _powerShellService.DecodeOutput(output);
            string decodedError = _powerShellService.DecodeOutput(error);

            if (exitCode != 0 || !string.IsNullOrEmpty(decodedError))
            {
                _logger.LogError("GetPrintersAsync failed. ExitCode: {ExitCode}, Error: {Error}", exitCode, decodedError);
                return [];
            }
            if (string.IsNullOrWhiteSpace(decodedOutput))
                return [];

            string json = CommonHelpers.ExtractJson(decodedOutput);
            if (string.IsNullOrEmpty(json))
            {
                _logger.LogError("ExtractJson returned empty. Raw output: {Output}", decodedOutput);
                return [];
            }

            try
            {
                var tempList = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json, _jsonOptions);
                if (tempList == null) return [];

                var printers = new List<PrinterInfo>();
                foreach (var item in tempList)
                {
                    string? name = item.GetValueOrDefault("Name") ?? item.GetValueOrDefault("name");
                    string? info = item.GetValueOrDefault("Info") ?? item.GetValueOrDefault("info");
                    string? type = item.GetValueOrDefault("Type") ?? item.GetValueOrDefault("type");

                    if (string.IsNullOrEmpty(name)) continue;
                    if (type != null && type.Equals("Printer", StringComparison.OrdinalIgnoreCase))
                    {
                        printers.Add(new PrinterInfo
                        {
                            Name = name,
                            PortName = info ?? "",
                            Type = type
                        });
                    }
                }
                return printers;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON deserialization error. JSON: {Json}", json);
                return [];
            }
        }

        public async Task<(bool success, string output)> ConnectPrinterAsync(string targetHost, string ipPrint, string driverFullName, string printName)
        {
            string password = await GetPasswordAsync(targetHost);
            if (string.IsNullOrEmpty(password) || password.Contains("Ошибка"))
                return (false, "Не удалось получить пароль LAPS");

            try
            {
                var existingPrinters = await GetPrintersAsync(targetHost);
                bool existsByIp = existingPrinters.Any(p => p.PortName != null && p.PortName.Contains(ipPrint));
                bool existsByName = existingPrinters.Any(p => p.Name != null && p.Name.Equals(printName, StringComparison.OrdinalIgnoreCase));

                if (existsByIp || existsByName)
                {
                    string message = existsByIp ? $"Принтер с IP {ipPrint} уже подключен" : $"Принтер с именем \"{printName}\" уже подключен";
                    return (false, message);
                }

                string escapedPassword = password.Replace("'", "''");
                string psScript = $@"
                                [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
                                $ProgressPreference = 'SilentlyContinue'
                                $WarningPreference = 'SilentlyContinue'
                                $ErrorActionPreference = 'SilentlyContinue'
                                $TargetHost = '{targetHost}'
                                $Password = '{escapedPassword}'
                                $IP_Print = '{ipPrint}'
                                $DriverFullName = '{driverFullName.Replace("'", "''")}'
                                $PrintName = '{printName}'

                                $username = ""$TargetHost\RuAdmin""

                                $options = New-Object System.Management.ConnectionOptions
                                $options.Username = $username
                                $options.SecurePassword = $Password | ConvertTo-SecureString -AsPlainText -Force
                                $options.Authentication = [System.Management.AuthenticationLevel]::PacketPrivacy

                                $scope = New-Object System.Management.ManagementScope(""\\$TargetHost\root\cimv2"", $options)
                                $scope.Connect()

                                $portClass = New-Object System.Management.ManagementClass($scope, (New-Object System.Management.ManagementPath(""Win32_TCPIPPrinterPort"")), $null)
                                $newPort = $portClass.CreateInstance()
                                $newPort[""Name""] = $IP_Print
                                $newPort[""Protocol""] = 1
                                $newPort[""HostAddress""] = $IP_Print
                                $null = $newPort.Put()

                                Write-Output ""Производится подключение принтера, пожалуйста подождите...""

                                $printerClass = New-Object System.Management.ManagementClass($scope, (New-Object System.Management.ManagementPath(""Win32_Printer"")), $null)
                                $newPrinter = $printerClass.CreateInstance()
                                $newPrinter[""Name""] = $PrintName
                                $newPrinter[""DriverName""] = $DriverFullName
                                $newPrinter[""PortName""] = $IP_Print
                                $newPrinter[""DeviceID""] = $PrintName
                                $null = $newPrinter.Put()

                                Write-Output ""Принтер был подключен""";
                psScript = _powerShellService.InjectUserVariable(psScript);
                var (output, error, exitCode) = await _powerShellService.RunScriptAsyncWithStatus(psScript);

                string decodedOutput = _powerShellService.DecodeOutput(output);
                string decodedError = _powerShellService.DecodeOutput(error);
                string cleanOutput = CommonHelpers.CleanPowerShellOutput(decodedOutput);
                string cleanError = CommonHelpers.CleanPowerShellOutput(decodedError);

                if (exitCode != 0 || !string.IsNullOrEmpty(cleanError))
                {
                    _logger.LogError("ConnectPrinterAsync PowerShell error. ExitCode: {ExitCode}, Error: {Error}", exitCode, cleanError);
                    return (false, $"Ошибка PowerShell: {cleanError}\n{cleanOutput}");
                }
                return (true, cleanOutput);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConnectPrinterAsync exception for target {TargetHost}, printer {PrintName}", targetHost, printName);
                return (false, $"Внутренняя ошибка: {ex.Message}");
            }
        }
    }
}