namespace WebMetroSR.Models
{
    public class ContainerHostInfo
    {
        public string? Hostname { get; set; }
        public bool IsActive { get; set; }
        public bool IsOnline { get; set; }
        public string? Ip { get; set; }
        public string? HddTotal { get; set; }
        public string? HddFree { get; set; }
        public string? Ram { get; set; }
        public string? OsInfo { get; set; }

        public string? Error { get; set; }
    }

    public class HostInfo
    {
        public string? Name { get; set; }
        public string? DistinguishedName { get; set; }
    }
}
