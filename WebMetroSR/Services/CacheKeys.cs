namespace WebMetroSR.Services
{
    public static class CacheKeys
    {
        public const string LapsPrefix = "laps_";
        public const string SccmIpPrefix = "sccm_ip_";
        public const string SccmActivePrefix = "sccm_active_";
        public const string BasicInfoPrefix = "basic_";
        public const string HostInfoPrefix = "hostinfo_";
        public const string MetroIpPrefix = "metro_ip_";
        public const string MetroPortPrefix = "metro_port_";

        public static string LapsKey(string hostname) => $"{LapsPrefix}{hostname.ToUpper()}";
        public static string SccmIpKey(string hostname) => $"{SccmIpPrefix}{hostname.ToUpper()}";
        public static string SccmActiveKey(string hostname) => $"{SccmActivePrefix}{hostname.ToUpper()}";
        public static string BasicInfoKey(string hostname) => $"{BasicInfoPrefix}{hostname.ToUpper()}";
        public static string HostInfoKey(string hostname) => $"{HostInfoPrefix}{hostname.ToUpper()}";
    }
}
