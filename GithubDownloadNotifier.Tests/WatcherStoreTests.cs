using GithubDownloadNotifier.Models;
using GithubDownloadNotifier.Services;

namespace GithubDownloadNotifier.Tests;

public sealed class WatcherStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "GithubDownloadNotifierTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void SaveAndLoadRoundTripsRepositoryConfiguration()
    {
        var store = new WatcherStore(_directory);
        var document = new WatcherConfigurationDocument
        {
            Repositories =
            [
                new RepositoryWatchConfiguration
                {
                    Owner = "dbak91",
                    Name = "GitHubDownloadNotifier",
                    Download = new DownloadWatcherConfiguration
                    {
                        Enabled = true,
                        Scope = DownloadScope.AllReleases
                    }
                }
            ]
        };

        store.Save(document);
        var loaded = store.Load();

        var repository = Assert.Single(loaded.Repositories);
        Assert.Equal("dbak91", repository.Owner);
        Assert.Equal("GitHubDownloadNotifier", repository.Name);
        Assert.True(repository.Download.Enabled);
    }

    [Fact]
    public void LegacyMigrationCreatesOneDownloadWatcher()
    {
        var migrated = LegacySettingsMigrator.Migrate(new LegacyWatcherSettings(
            "dbak91",
            "GitHubDownloadNotifier",
            5,
            true,
            false,
            true));

        var repository = Assert.Single(migrated.Repositories);
        Assert.True(repository.Download.Enabled);
        Assert.Equal(15, repository.Download.Schedule.Amount);
        Assert.Equal(DownloadScope.AllReleases, repository.Download.Scope);
        Assert.False(repository.Release.Enabled);
    }

    [Fact]
    public void UpdateAtomicallyChangesExistingDocument()
    {
        var store = new WatcherStore(_directory);
        store.Save(new WatcherConfigurationDocument
        {
            Repositories = [new RepositoryWatchConfiguration { Owner = "before", Name = "repo" }]
        });

        store.Update(document => document.Repositories[0].Owner = "after");

        Assert.Equal("after", store.Load().Repositories[0].Owner);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
