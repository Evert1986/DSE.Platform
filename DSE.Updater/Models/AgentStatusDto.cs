using System.Text.Json.Serialization;

namespace DSE.Updater.Models;

internal class AgentStatusDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";
}