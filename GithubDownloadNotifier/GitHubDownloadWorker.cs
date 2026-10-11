using System.Net.Http.Headers;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Work;

namespace GithubDownloadNotifier;

[Android.Runtime.Preserve(AllMembers = true)]
public sealed class GitHubDownloadWorker(Context context, WorkerParameters workerParameters) : Worker(context, workerParameters)
{
    public const string UniqueWorkName = "github_download_monitor";
    public const string PreferencesName = "github_download_notifier";
    public const string KeyOwner = "repo_owner";
    public const string KeyRepo = "repo_name";
    public const string KeyIntervalMinutes = "check_interval_minutes";
    public const string KeyAllReleases = "all_releases";
    public const string KeyAlwaysNotify = "always_notify";

    private const string LastTotalPrefix = "last_total_";
    private const int UpdateNotificationId = 1002;
    private const string UpdateChannelId = "update_channel";

    public override Result DoWork()
    {
        try
        {
            return CheckDownloadsAsync().GetAwaiter().GetResult()
                ? Result.InvokeSuccess()
                : Result.InvokeRetry();
        }
        catch
        {
            return Result.InvokeRetry();
        }
    }

    private async Task<bool> CheckDownloadsAsync()
    {
        var preferences = ApplicationContext.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        var owner = preferences?.GetString(KeyOwner, string.Empty)?.Trim() ?? string.Empty;
        var repo = preferences?.GetString(KeyRepo, string.Empty)?.Trim() ?? string.Empty;
        var allReleases = preferences?.GetBoolean(KeyAllReleases, true) ?? true;
        var alwaysNotify = preferences?.GetBoolean(KeyAlwaysNotify, false) ?? false;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo) || preferences is null)
        {
            return true;
        }

        var currentTotal = await TryGetReleaseDownloadTotalAsync(owner, repo, allReleases);
        if (!currentTotal.HasValue)
        {
            return false;
        }

        ProcessDownloadTotal(preferences, owner, repo, allReleases, alwaysNotify, currentTotal.Value);
        return true;
    }

    private void ProcessDownloadTotal(
        ISharedPreferences preferences,
        string owner,
        string repo,
        bool allReleases,
        bool alwaysNotify,
        int currentTotal)
    {
        var key = BuildLastTotalKey(owner, repo, allReleases);
        var hasPrevious = preferences.Contains(key);
        var previousTotal = preferences.GetInt(key, currentTotal);
        var delta = currentTotal - previousTotal;

        if (alwaysNotify || (hasPrevious && delta > 0))
        {
            SendUpdateNotification(owner, repo, hasPrevious ? delta : null, currentTotal);
        }

        preferences.Edit()?.PutInt(key, currentTotal)?.Apply();
    }

    private static string BuildLastTotalKey(string owner, string repo, bool allReleases)
        => LastTotalPrefix + (allReleases ? "all_" : "latest_") + owner.ToLowerInvariant() + "_" + repo.ToLowerInvariant();

    private static async Task<int?> TryGetReleaseDownloadTotalAsync(string owner, string repo, bool allReleases)
    {
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GithubDownloadNotifier", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var releasePath = allReleases ? "releases?per_page=100" : "releases/latest";
        var requestUri = $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/{releasePath}";
        using var response = await httpClient.GetAsync(requestUri);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);

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

    private void SendUpdateNotification(string owner, string repo, int? delta, int currentTotal)
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
            .SetContentTitle($"{owner}/{repo}")
            .SetContentText(message)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetAutoCancel(true)
            .Build();

        var manager = (NotificationManager?)ApplicationContext.GetSystemService(Context.NotificationService);
        manager?.Notify(UpdateNotificationId, notification);
    }

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
