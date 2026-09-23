namespace WebMetroSR.Interfaces
{
    public interface IMetroScopeService
    {
        string GetIpFromMetroScope(string hostname);
        string GetSwitchPortFromMetroScope(string ipAddress);
        void CleanOldTempFiles();
    }
}
