using System.Text.Json.Serialization;

namespace DSE.Updater.Models;

internal class UpdateManifest
{
    [JsonPropertyName("product")]
    public string Product { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";

    [JsonPropertyName("files")]
    public List<UpdateManifestFile> Files { get; set; } = new();
}