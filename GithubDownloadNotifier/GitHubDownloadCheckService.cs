using System.Net.Http.Headers;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.OS;

namespace GithubDownloadNotifier;

[Service(Exported = false, ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
public class GitHubDownloadCheckService : Service
{
    public const string PreferencesName = "github_download_notifier";
    public const string KeyOwner = "repo_owner";
    public const string KeyRepo = "repo_name";
    public const string KeyIntervalMinutes = "check_interval_minutes";
    public const string KeyAllReleases = "all_releases";
    public const string KeyAlwaysNotify = "always_notify";

    private const string LastTotalPrefix = "last_total_";
    private const int ForegroundNotificationId = 1001;
    private const int UpdateNotificationId = 1002;
    private const string StatusChannelId = "status_channel_silent_v2";
    private const string UpdateChannelId = "update_channel";

    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _workerTask;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureNotificationChannels();
        StartForeground(ForegroundNotificationId, BuildStatusNotification("Monitoring GitHub downloads"));

        if (_workerTask is null || _workerTask.IsCompleted)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _workerTask = Task.Run(() => MonitorLoopAsync(_cancellationTokenSource.Token));
        }

        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        base.OnDestroy();
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
            var owner = preferences?.GetString(KeyOwner, string.Empty)?.Trim() ?? string.Empty;
            var repo = preferences?.GetString(KeyRepo, string.Empty)?.Trim() ?? string.Empty;
            var intervalMinutes = preferences?.GetInt(KeyIntervalMinutes, 60) ?? 60;
            var allReleases = preferences?.GetBoolean(KeyAllReleases, true) ?? true;
            var alwaysNotify = preferences?.GetBoolean(KeyAlwaysNotify, false) ?? false;

            if (intervalMinutes <= 0)
            {
                intervalMinutes = 60;
            }

            if (!string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo))
            {
                var repoDisplay = $"{owner}/{repo}";
                UpdateForegroundStatus($"Checking {repoDisplay} every {intervalMinutes} minute(s)");

                var currentTotal = await TryGetReleaseDownloadTotalAsync(owner, repo, allReleases, cancellationToken);
                if (currentTotal.HasValue)
                {
                    ProcessDownloadTotal(preferences!, owner, repo, allReleases, alwaysNotify, currentTotal.Value, intervalMinutes);
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private void ProcessDownloadTotal(ISharedPreferences preferences, string owner, string repo, bool allReleases, bool alwaysNotify, int currentTotal, int intervalMinutes)
    {
        var key = BuildLastTotalKey(owner, repo, allReleases);
        var hasPrevious = preferences.Contains(key);
        var previousTotal = preferences.GetInt(key, currentTotal);
        var delta = currentTotal - previousTotal;

        if (alwaysNotify || (hasPrevious && delta > 0))
        {
            SendUpdateNotification(owner, repo, hasPrevious ? delta : null, currentTotal, intervalMinutes);
        }

        preferences.Edit()?.PutInt(key, currentTotal)?.Apply();
    }

    private static string BuildLastTotalKey(string owner, string repo, bool allReleases)
        => LastTotalPrefix + (allReleases ? "all_" : "latest_") + owner.ToLowerInvariant() + "_" + repo.ToLowerInvariant();

    private async Task<int?> TryGetReleaseDownloadTotalAsync(string owner, string repo, bool allReleases, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GithubDownloadNotifier", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var releasePath = allReleases ? "releases?per_page=100" : "releases/latest";
        var requestUri = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/{releasePath}";
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return SumReleaseAssetDownloads(document.RootElement);
        }

        var total = 0;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            total += SumReleaseAssetDownloads(release);
        }

        return total;
    }

    private static int SumReleaseAssetDownloads(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        var total = 0;
        foreach (var asset in assetsElement.EnumerateArray())
        {
            if (asset.TryGetProperty("download_count", out var downloadCountElement) &&
                downloadCountElement.TryGetInt32(out var downloadCount))
            {
                total += downloadCount;
            }
        }

        return total;
    }

    private void EnsureNotificationChannels()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null)
        {
            return;
        }

        var statusChannel = new NotificationChannel(StatusChannelId, "Background monitoring", NotificationImportance.Min)
        {
            Description = "Required Android foreground service status"
        };
        statusChannel.SetSound(null, null);
        statusChannel.EnableVibration(false);
        statusChannel.EnableLights(false);
        statusChannel.SetShowBadge(false);
        statusChannel.LockscreenVisibility = NotificationVisibility.Secret;

        var updateChannel = new NotificationChannel(UpdateChannelId, "Download Updates", NotificationImportance.Default)
        {
            Description = "Notifications for new downloads"
        };

        manager.CreateNotificationChannel(statusChannel);
        manager.CreateNotificationChannel(updateChannel);
    }

    private Notification BuildStatusNotification(string message)
    {
        var launchIntent = new Intent(this, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendingIntent = PendingIntent.GetActivity(
            this,
            0,
            launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, StatusChannelId)
            : new Notification.Builder(this);

        return builder
            .SetContentTitle(GetString(Resource.String.monitoring_notification_title))
            .SetContentText(message)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .SetCategory(Notification.CategoryService)
            .SetShowWhen(false)
            .SetOnlyAlertOnce(true)
            .SetPriority((int)NotificationPriority.Min)
            .Build();
    }

    private void UpdateForegroundStatus(string message)
    {
        var notification = BuildStatusNotification(message);
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.Notify(ForegroundNotificationId, notification);
    }

    private void SendUpdateNotification(string owner, string repo, int? delta, int currentTotal, int intervalMinutes)
    {
        var message = delta switch
        {
            > 0 => $"{delta} new downloads in past {intervalMinutes} minute(s) ({currentTotal} total)",
            0 => $"No new downloads in past {intervalMinutes} minute(s) ({currentTotal} total)",
            < 0 => $"Total changed by {delta} since last check ({currentTotal} total)",
            _ => $"{currentTotal} total downloads (first check)"
        };

        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, UpdateChannelId)
            : new Notification.Builder(this);

        var notification = builder
            .SetContentTitle($"{owner}/{repo}")
            .SetContentText(message)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetAutoCancel(true)
            .Build();

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.Notify(UpdateNotificationId, notification);
    }
}