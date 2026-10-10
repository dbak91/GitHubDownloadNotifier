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

    private const string LastTotalPrefix = "last_total_";
    private const int ForegroundNotificationId = 1001;
    private const int UpdateNotificationId = 1002;
    private const string StatusChannelId = "status_channel";
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

            if (intervalMinutes <= 0)
            {
                intervalMinutes = 60;
            }

            if (!string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo))
            {
                var repoDisplay = $"{owner}/{repo}";
                UpdateForegroundStatus($"Checking {repoDisplay} every {intervalMinutes} minute(s)");

                var currentTotal = await TryGetLatestReleaseDownloadTotalAsync(owner, repo, cancellationToken);
                if (currentTotal.HasValue)
                {
                    ProcessDownloadTotal(preferences!, owner, repo, currentTotal.Value, intervalMinutes);
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

    private void ProcessDownloadTotal(ISharedPreferences preferences, string owner, string repo, int currentTotal, int intervalMinutes)
    {
        var key = BuildLastTotalKey(owner, repo);
        var hasPrevious = preferences.Contains(key);
        var previousTotal = preferences.GetInt(key, currentTotal);

        if (hasPrevious && currentTotal > previousTotal)
        {
            var delta = currentTotal - previousTotal;
            SendUpdateNotification(owner, repo, delta, currentTotal, intervalMinutes);
        }

        preferences.Edit()?.PutInt(key, currentTotal)?.Apply();
    }

    private static string BuildLastTotalKey(string owner, string repo)
        => LastTotalPrefix + owner.ToLowerInvariant() + "_" + repo.ToLowerInvariant();

    private async Task<int?> TryGetLatestReleaseDownloadTotalAsync(string owner, string repo, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GithubDownloadNotifier", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var requestUri = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/releases/latest";
        using var response = await httpClient.GetAsync(requestUri, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("assets", out var assetsElement) || assetsElement.ValueKind != JsonValueKind.Array)
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

        var statusChannel = new NotificationChannel(StatusChannelId, "Service Status", NotificationImportance.Low)
        {
            Description = "Foreground service status"
        };

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
            .Build();
    }

    private void UpdateForegroundStatus(string message)
    {
        var notification = BuildStatusNotification(message);
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.Notify(ForegroundNotificationId, notification);
    }

    private void SendUpdateNotification(string owner, string repo, int delta, int currentTotal, int intervalMinutes)
    {
        var message = $"{delta} new downloads in past {intervalMinutes} minute(s) ({currentTotal} total)";

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