using System.Text.Json.Serialization;

namespace DSE.Updater.Models;

internal class UpdateManifestFile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("target")]
    public string Target { get; set; } = "";
}