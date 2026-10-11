using System.Text.Json;
using GithubDownloadNotifier.Models;

namespace GithubDownloadNotifier.Services;

public sealed class WatcherStore
{
    private const string FileName = "watchers.json";
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public WatcherStore(string storageDirectory)
    {
        Directory.CreateDirectory(storageDirectory);
        _filePath = Path.Combine(storageDirectory, FileName);
    }

    public WatcherConfigurationDocument Load()
    {
        if (!File.Exists(_filePath))
        {
            return new WatcherConfigurationDocument();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize<WatcherConfigurationDocument>(json, _jsonOptions);
            return document is null || document.SchemaVersion != WatcherConfigurationDocument.CurrentSchemaVersion
                ? new WatcherConfigurationDocument()
                : document;
        }
        catch (JsonException)
        {
            return new WatcherConfigurationDocument();
        }
        catch (IOException)
        {
            return new WatcherConfigurationDocument();
        }
    }

    public void Save(WatcherConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.SchemaVersion = WatcherConfigurationDocument.CurrentSchemaVersion;
        var temporaryPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(document, _jsonOptions);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _filePath, true);
    }
}
