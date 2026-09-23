using System.Text.Json.Serialization;

namespace WebMetroSR.Models
{
    public class SoftwareInfo
    {
        [JsonPropertyName("DisplayName")]
        public string? Name { get; set; }

        [JsonPropertyName("Version")]
        public string? Version { get; set; }
    }
}
