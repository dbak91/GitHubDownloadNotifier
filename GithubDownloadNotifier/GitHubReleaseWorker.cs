using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Work;
using GithubDownloadNotifier.Models;
using GithubDownloadNotifier.Services;

namespace GithubDownloadNotifier;

[Android.Runtime.Preserve(AllMembers = true)]
public sealed class GitHubReleaseWorker(Context context, WorkerParameters workerParameters) : Worker(context, workerParameters)
{
    public const string RepositoryIdInputKey = "repository_id";
    public const string ScheduledRunUtcInputKey = "scheduled_run_utc";
    private const string UpdateChannelId = "release_update_channel";

    public override Result DoWork()
    {
        var repositoryId = InputData.GetString(RepositoryIdInputKey);
        try
        {
            CheckReleaseAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(repositoryId))
            {
                RecordScheduledOccurrence(repositoryId);
                new ReleaseWorkScheduler(ApplicationContext).ScheduleNext(repositoryId);
            }
        }

        return Result.InvokeSuccess();
    }

    private async Task CheckReleaseAsync()
    {
        var repositoryId = InputData.GetString(RepositoryIdInputKey);
        var storageDirectory = ApplicationContext.FilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(repositoryId) || string.IsNullOrWhiteSpace(storageDirectory))
        {
            return;
        }

        var store = new WatcherStore(storageDirectory);
        var repository = store.Load().Repositories.FirstOrDefault(item => item.Id == repositoryId);
        if (repository is null || !repository.Release.Enabled)
        {
            return;
        }

        try
        {
            using var httpClient = new HttpClient();
            var release = await new GitHubApiClient(httpClient).GetLatestReleaseAsync(repository.Owner, repository.Name);
            var shouldNotify = false;
            store.Update(document =>
            {
                var latest = document.Repositories.FirstOrDefault(item => item.Id == repositoryId);
                if (latest is null || !latest.Release.Enabled)
                {
                    return;
                }

                shouldNotify = !string.Equals(latest.Release.CurrentTag, release.Tag, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(latest.Release.PendingTag, release.Tag, StringComparison.OrdinalIgnoreCase);
                if (shouldNotify)
                {
                    latest.Release.PendingTag = release.Tag;
                }

                latest.Release.LastRun = new WatcherRunStatus
                {
                    LastAttemptUtc = DateTimeOffset.UtcNow,
                    Succeeded = true,
                    Message = shouldNotify ? $"New release {release.Tag}" : $"Latest release {release.Tag}"
                };
            });

            if (shouldNotify)
            {
                SendReleaseNotification(repository, release);
            }
            await WriteLogAsync(repository, new WatcherLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Repository = $"{repository.Owner}/{repository.Name}",
                WatcherType = "Release",
                Status = "Success",
                BaselineTag = repository.Release.CurrentTag,
                DetectedTag = release.Tag,
                ReleaseName = release.Name,
                ReleasePublishedAt = release.PublishedAt,
                ReleaseIsPrerelease = release.IsPrerelease,
                ReleaseUrl = release.Url
            });
        }
        catch (Exception exception)
        {
            store.Update(document =>
            {
                var latest = document.Repositories.FirstOrDefault(item => item.Id == repositoryId);
                if (latest is not null)
                {
                    latest.Release.LastRun = new WatcherRunStatus
                    {
                        LastAttemptUtc = DateTimeOffset.UtcNow,
                        Succeeded = false,
                        Message = exception.Message
                    };
                }
            });
            await WriteLogAsync(repository, new WatcherLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Repository = $"{repository.Owner}/{repository.Name}",
                WatcherType = "Release",
                Status = "Error",
                Error = exception.Message,
                BaselineTag = repository.Release.CurrentTag
            });
        }
    }

    private async Task WriteLogAsync(RepositoryWatchConfiguration repository, WatcherLogEntry entry)
    {
        try
        {
            await new AndroidWatcherLogWriter(ApplicationContext).AppendAsync(repository.Release.Logging, entry);
        }
        catch
        {
        }
    }

    private void RecordScheduledOccurrence(string repositoryId)
    {
        var scheduledMilliseconds = InputData.GetLong(ScheduledRunUtcInputKey, 0);
        var scheduledUtc = scheduledMilliseconds > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(scheduledMilliseconds)
            : DateTimeOffset.UtcNow;
        var storageDirectory = ApplicationContext.FilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(storageDirectory))
        {
            return;
        }

        new WatcherStore(storageDirectory).Update(document =>
        {
            var repository = document.Repositories.FirstOrDefault(item => item.Id == repositoryId);
            if (repository is not null)
            {
                repository.Release.Schedule.LastScheduledUtc = scheduledUtc;
            }
        });
    }

    private void SendReleaseNotification(RepositoryWatchConfiguration repository, GitHubRelease release)
    {
        EnsureNotificationChannel();
        var message = string.IsNullOrWhiteSpace(release.Name) || release.Name == release.Tag
            ? $"New release: {release.Tag}"
            : $"New release: {release.Name} ({release.Tag})";
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(ApplicationContext, UpdateChannelId)
            : new Notification.Builder(ApplicationContext);
        var notification = builder
            .SetContentTitle($"{repository.Owner}/{repository.Name}")
            .SetContentText(message)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetAutoCancel(true)
            .Build();

        var manager = (NotificationManager?)ApplicationContext.GetSystemService(Context.NotificationService);
        manager?.Notify(GetNotificationId(repository.Id), notification);
    }

    private static int GetNotificationId(string repositoryId)
        => 103000 + Math.Abs(StringComparer.Ordinal.GetHashCode(repositoryId) % 100000);

    private void EnsureNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager?)ApplicationContext.GetSystemService(Context.NotificationService);
        var channel = new NotificationChannel(UpdateChannelId, "Release Updates", NotificationImportance.Default)
        {
            Description = "Notifications for new GitHub releases"
        };
        manager?.CreateNotificationChannel(channel);
    }
}
