using WebMetroSR.Models;

namespace WebMetroSR.Interfaces
{
    public interface IPrintersService
    {
        Task<List<string>> GetPrinterDriversAsync(string targetHost);
        Task<List<PrinterInfo>> GetPrintersAsync(string targetHost);
        Task<(bool success, string output)> ConnectPrinterAsync(string targetHost, string ipPrint, string driverFullName, string printName);
        Task<bool> IsPrinterByIpAsync(string ip);
    }
}