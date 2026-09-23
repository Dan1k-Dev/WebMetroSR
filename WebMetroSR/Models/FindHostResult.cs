namespace WebMetroSR.Models
{
    public class FindHostResult
    {
        public bool Success { get; set; }
        public string? ContainerDn { get; set; }
        public string? HostName { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
