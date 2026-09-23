using System.Text.Json.Serialization;

namespace WebMetroSR.Models
{
    public class PrinterInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("portName")]
        public string? PortName { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("hostAddress")]
        public string? HostAddress { get; set; }
    }

    public class PrinterRawData
    {
        [JsonPropertyName("Name")]
        public string? Name { get; set; }

        [JsonPropertyName("PortName")]
        public string? PortName { get; set; }

        [JsonPropertyName("Type")]
        public string? Type { get; set; }

        [JsonPropertyName("HostAddress")]
        public string? HostAddress { get; set; }
    }

    public class PrinterInfoSimple
    {
        public string? Name { get; set; }
        public string? PortName { get; set; }
    }
}
