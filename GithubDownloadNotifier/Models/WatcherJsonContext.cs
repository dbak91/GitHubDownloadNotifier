using System.Text.Json.Serialization;

namespace GithubDownloadNotifier.Models;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(WatcherConfigurationDocument))]
internal partial class WatcherJsonContext : JsonSerializerContext;
