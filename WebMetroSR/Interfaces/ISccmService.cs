namespace WebMetroSR.Interfaces
{
    public interface ISccmService
    {
        Task<bool> IsClientActiveAsync(string hostName);
        bool IsClientActive(string hostName);

        Task<Dictionary<string, bool>> GetMultipleClientsActivityAsync(List<string> hostnames);
    }
}
