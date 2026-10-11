using System.Text.Json;
using GithubDownloadNotifier.Models;

namespace GithubDownloadNotifier.Services;

public sealed class WatcherStore
{
    private const string FileName = "watchers.json";
    private static readonly object SyncRoot = new();
    private readonly string _filePath;

    public WatcherStore(string storageDirectory)
    {
        Directory.CreateDirectory(storageDirectory);
        _filePath = Path.Combine(storageDirectory, FileName);
    }

    public WatcherConfigurationDocument Load()
    {
        lock (SyncRoot)
        {
            return LoadCore();
        }
    }

    public void Save(WatcherConfigurationDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (SyncRoot)
        {
            SaveCore(document);
        }
    }

    public void Update(Action<WatcherConfigurationDocument> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (SyncRoot)
        {
            var document = LoadCore();
            update(document);
            SaveCore(document);
        }
    }

    private WatcherConfigurationDocument LoadCore()
    {
        if (!File.Exists(_filePath))
        {
            return new WatcherConfigurationDocument();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var document = JsonSerializer.Deserialize(json, WatcherJsonContext.Default.WatcherConfigurationDocument);
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

    private void SaveCore(WatcherConfigurationDocument document)
    {
        document.SchemaVersion = WatcherConfigurationDocument.CurrentSchemaVersion;
        var temporaryPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(document, WatcherJsonContext.Default.WatcherConfigurationDocument);
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _filePath, true);
    }
}
