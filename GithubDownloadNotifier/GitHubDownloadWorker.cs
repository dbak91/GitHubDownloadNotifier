using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Work;
using GithubDownloadNotifier.Models;
using GithubDownloadNotifier.Services;

namespace GithubDownloadNotifier;

[Android.Runtime.Preserve(AllMembers = true)]
public sealed class GitHubDownloadWorker(Context context, WorkerParameters workerParameters) : Worker(context, workerParameters)
{
    public const string RepositoryIdInputKey = "repository_id";
    public const string ScheduledRunUtcInputKey = "scheduled_run_utc";
    private const string UpdateChannelId = "update_channel";

    public override Result DoWork()
    {
        var repositoryId = InputData.GetString(RepositoryIdInputKey);
        try
        {
            _ = CheckDownloadsAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // The next configured occurrence is still scheduled below.
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(repositoryId))
            {
                RecordScheduledOccurrence(repositoryId);
                new DownloadWorkScheduler(ApplicationContext).ScheduleNext(repositoryId);
            }
        }

        return Result.InvokeSuccess();
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
                repository.Download.Schedule.LastScheduledUtc = scheduledUtc;
            }
        });
    }

    private async Task<bool> CheckDownloadsAsync()
    {
        var repositoryId = InputData.GetString(RepositoryIdInputKey);
        var storageDirectory = ApplicationContext.FilesDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(repositoryId) || string.IsNullOrWhiteSpace(storageDirectory))
        {
            return true;
        }

        var store = new WatcherStore(storageDirectory);
        var document = store.Load();
        var repository = document.Repositories.FirstOrDefault(item => item.Id == repositoryId);
        if (repository is null || !repository.Download.Enabled)
        {
            return true;
        }

        try
        {
            using var httpClient = new HttpClient();
            var apiClient = new GitHubApiClient(httpClient);
            var currentTotal = await apiClient.GetDownloadTotalAsync(
                repository.Owner,
                repository.Name,
                repository.Download.Scope);
            int? delta = null;
            int? previousTotal = null;
            var shouldNotify = false;
            store.Update(latestDocument =>
            {
                var latest = latestDocument.Repositories.FirstOrDefault(item => item.Id == repositoryId);
                if (latest is null)
                {
                    return;
                }

                previousTotal = latest.Download.LastDownloadTotal;
                delta = previousTotal.HasValue ? currentTotal - previousTotal.Value : null;
                shouldNotify = latest.Download.NotificationMode == NotificationMode.EveryCheck || delta > 0;
                latest.Download.LastDownloadTotal = currentTotal;
                latest.Download.LastRun = new WatcherRunStatus
                {
                    LastAttemptUtc = DateTimeOffset.UtcNow,
                    Succeeded = true,
                    Message = $"{currentTotal} total downloads"
                };
            });

            if (shouldNotify)
            {
                SendUpdateNotification(repository, delta, currentTotal);
            }

            await WriteLogAsync(repository, new WatcherLogEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Repository = $"{repository.Owner}/{repository.Name}",
                WatcherType = "Download",
                Status = "Success",
                DownloadScope = repository.Download.Scope.ToString(),
                PreviousDownloadTotal = previousTotal,
                CurrentDownloadTotal = currentTotal,
                DownloadDelta = delta
            });

            return true;
        }
        catch (Exception exception)
        {
            store.Update(latestDocument =>
            {
                var latest = latestDocument.Repositories.FirstOrDefault(item => item.Id == repositoryId);
                if (latest is not null)
                {
                    latest.Download.LastRun = new WatcherRunStatus
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
                WatcherType = "Download",
                Status = "Error",
                Error = exception.Message,
                DownloadScope = repository.Download.Scope.ToString(),
                PreviousDownloadTotal = repository.Download.LastDownloadTotal
            });
            return false;
        }
    }

    private async Task WriteLogAsync(RepositoryWatchConfiguration repository, WatcherLogEntry entry)
    {
        try
        {
            await new AndroidWatcherLogWriter(ApplicationContext).AppendAsync(repository.Download.Logging, entry);
        }
        catch
        {
        }
    }

    private void SendUpdateNotification(RepositoryWatchConfiguration repository, int? delta, int currentTotal)
    {
        EnsureUpdateNotificationChannel();

        var message = delta switch
        {
            > 0 => $"{delta} new downloads since the previous check ({currentTotal} total)",
            0 => $"No new downloads since the previous check ({currentTotal} total)",
            < 0 => $"Total changed by {delta} since the previous check ({currentTotal} total)",
            _ => $"{currentTotal} total downloads (first check)"
        };

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
        => 3000 + Math.Abs(StringComparer.Ordinal.GetHashCode(repositoryId) % 100000);

    private void EnsureUpdateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager?)ApplicationContext.GetSystemService(Context.NotificationService);
        var channel = new NotificationChannel(UpdateChannelId, "Download Updates", NotificationImportance.Default)
        {
            Description = "Notifications for new downloads"
        };
        manager?.CreateNotificationChannel(channel);
    }
}
