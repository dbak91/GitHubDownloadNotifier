using GithubDownloadNotifier.Models;

namespace GithubDownloadNotifier.Services;

public sealed record LegacyWatcherSettings(
    string Owner,
    string Repository,
    int IntervalMinutes,
    bool AllReleases,
    bool AlwaysNotify,
    bool MonitoringEnabled);

public static class LegacySettingsMigrator
{
    public static WatcherConfigurationDocument Migrate(LegacyWatcherSettings settings)
    {
        var document = new WatcherConfigurationDocument();
        if (string.IsNullOrWhiteSpace(settings.Owner) || string.IsNullOrWhiteSpace(settings.Repository))
        {
            return document;
        }

        document.Repositories.Add(new RepositoryWatchConfiguration
        {
            Owner = settings.Owner.Trim(),
            Name = settings.Repository.Trim(),
            Download = new DownloadWatcherConfiguration
            {
                Enabled = settings.MonitoringEnabled,
                Scope = settings.AllReleases ? DownloadScope.AllReleases : DownloadScope.LatestRelease,
                NotificationMode = settings.AlwaysNotify ? NotificationMode.EveryCheck : NotificationMode.ChangesOnly,
                Schedule = WatchSchedule.Minutes(Math.Max(15, settings.IntervalMinutes))
            }
        });

        return document;
    }
}
