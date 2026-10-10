using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GithubDownloadNotifier;

[Activity(Label = "@string/app_name", MainLauncher = true)]
public class MainActivity : Activity
{
    private static readonly int[] IntervalValues = [5, 30, 60, 720, 1440];

    private const int NotificationPermissionRequestCode = 100;
    private const string KeyServiceEnabled = "service_enabled";
    private const string TestChannelId = "manual_test_channel";
    private const int TestNotificationId = 2001;
    private const string LastTotalPrefix = "last_total_";

    private EditText? _repoOwnerEditText;
    private EditText? _repoNameEditText;
    private Spinner? _intervalSpinner;
    private EditText? _customIntervalEditText;
    private Switch? _allReleasesSwitch;
    private Switch? _alwaysNotifySwitch;
    private Switch? _serviceSwitch;
    private TextView? _statusTextView;
    private bool _pendingStartMonitoring;
    private bool _pendingTestNotification;
    private bool _updatingServiceSwitch;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        _repoOwnerEditText = FindViewById<EditText>(Resource.Id.repoOwnerEditText);
        _repoNameEditText = FindViewById<EditText>(Resource.Id.repoNameEditText);
        _intervalSpinner = FindViewById<Spinner>(Resource.Id.intervalSpinner);
        _customIntervalEditText = FindViewById<EditText>(Resource.Id.customIntervalEditText);
        _allReleasesSwitch = FindViewById<Switch>(Resource.Id.allReleasesSwitch);
        _alwaysNotifySwitch = FindViewById<Switch>(Resource.Id.alwaysNotifySwitch);
        _serviceSwitch = FindViewById<Switch>(Resource.Id.serviceSwitch);
        _statusTextView = FindViewById<TextView>(Resource.Id.statusTextView);
        var saveButton = FindViewById<Button>(Resource.Id.saveButton);
        var testNotificationButton = FindViewById<Button>(Resource.Id.testNotificationButton);

        ConfigureIntervalSpinner();
        LoadSettings();

        if (_allReleasesSwitch is not null)
        {
            _allReleasesSwitch.CheckedChange += (_, args) => UpdateReleaseScopeText(args.IsChecked);
        }

        if (_alwaysNotifySwitch is not null)
        {
            _alwaysNotifySwitch.CheckedChange += (_, args) => UpdateNotificationModeText(args.IsChecked);
        }

        if (saveButton is not null)
        {
            saveButton.Click += (_, _) =>
            {
                if (TrySaveSettings())
                {
                    Toast.MakeText(this, Resource.String.settings_saved, ToastLength.Short)?.Show();
                    ApplySettingsAndEnsureMonitoring();
                }
            };
        }

        if (testNotificationButton is not null)
        {
            testNotificationButton.Click += async (_, _) =>
            {
                if (!TrySaveSettings())
                {
                    return;
                }

                if (NeedsNotificationPermission())
                {
                    _pendingTestNotification = true;
                    RequestPermissions([Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
                    return;
                }

                await SendTestNotificationAsync();
            };
        }

        if (_serviceSwitch is not null)
        {
            _serviceSwitch.CheckedChange += (_, args) =>
            {
                if (_updatingServiceSwitch)
                {
                    return;
                }

                if (args.IsChecked)
                {
                    StartMonitoringWithValidation();
                }
                else
                {
                    StopMonitoring();
                }
            };
        }

        UpdateStatusText(_serviceSwitch?.Checked == true);
    }

    private void LoadSettings()
    {
        var preferences = GetSharedPreferences(GitHubDownloadCheckService.PreferencesName, FileCreationMode.Private);
        var owner = preferences?.GetString(GitHubDownloadCheckService.KeyOwner, string.Empty) ?? string.Empty;
        var repo = preferences?.GetString(GitHubDownloadCheckService.KeyRepo, string.Empty) ?? string.Empty;
        var interval = preferences?.GetInt(GitHubDownloadCheckService.KeyIntervalMinutes, 60) ?? 60;
        var allReleases = preferences?.GetBoolean(GitHubDownloadCheckService.KeyAllReleases, true) ?? true;
        var alwaysNotify = preferences?.GetBoolean(GitHubDownloadCheckService.KeyAlwaysNotify, false) ?? false;
        var serviceEnabled = preferences?.GetBoolean(KeyServiceEnabled, false) ?? false;

        if (_repoOwnerEditText is not null)
        {
            _repoOwnerEditText.Text = owner;
        }

        if (_repoNameEditText is not null)
        {
            _repoNameEditText.Text = repo;
        }

        SelectInterval(interval);

        if (_allReleasesSwitch is not null)
        {
            _allReleasesSwitch.Checked = allReleases;
            UpdateReleaseScopeText(allReleases);
        }

        if (_alwaysNotifySwitch is not null)
        {
            _alwaysNotifySwitch.Checked = alwaysNotify;
            UpdateNotificationModeText(alwaysNotify);
        }

        if (_serviceSwitch is not null)
        {
            _serviceSwitch.Checked = serviceEnabled;
        }
    }

    private bool TrySaveSettings()
    {
        var owner = _repoOwnerEditText?.Text?.Trim() ?? string.Empty;
        var repo = _repoNameEditText?.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            Toast.MakeText(this, Resource.String.repo_required_message, ToastLength.Short)?.Show();
            return false;
        }

        if (!TryGetSelectedInterval(out var intervalMinutes))
        {
            Toast.MakeText(this, Resource.String.interval_invalid_message, ToastLength.Short)?.Show();
            return false;
        }

        var preferences = GetSharedPreferences(GitHubDownloadCheckService.PreferencesName, FileCreationMode.Private);
        preferences?.Edit()
            ?.PutString(GitHubDownloadCheckService.KeyOwner, owner)
            ?.PutString(GitHubDownloadCheckService.KeyRepo, repo)
            ?.PutInt(GitHubDownloadCheckService.KeyIntervalMinutes, intervalMinutes)
            ?.PutBoolean(GitHubDownloadCheckService.KeyAllReleases, _allReleasesSwitch?.Checked ?? true)
            ?.PutBoolean(GitHubDownloadCheckService.KeyAlwaysNotify, _alwaysNotifySwitch?.Checked ?? false)
            ?.Apply();

        return true;
    }

    private void UpdateNotificationModeText(bool alwaysNotify)
    {
        if (_alwaysNotifySwitch is not null)
        {
            _alwaysNotifySwitch.Text = GetString(alwaysNotify
                ? Resource.String.notify_always
                : Resource.String.notify_updates_only);
        }
    }

    private void ConfigureIntervalSpinner()
    {
        if (_intervalSpinner is null)
        {
            return;
        }

        var labels = new[]
        {
            GetString(Resource.String.interval_5_minutes),
            GetString(Resource.String.interval_30_minutes),
            GetString(Resource.String.interval_1_hour),
            GetString(Resource.String.interval_12_hours),
            GetString(Resource.String.interval_1_day),
            GetString(Resource.String.interval_custom)
        };

        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, labels);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _intervalSpinner.Adapter = adapter;
        _intervalSpinner.ItemSelected += (_, _) => UpdateCustomIntervalVisibility();
    }

    private void SelectInterval(int intervalMinutes)
    {
        if (_intervalSpinner is null)
        {
            return;
        }

        var presetIndex = Array.IndexOf(IntervalValues, intervalMinutes);
        var selectedIndex = presetIndex >= 0 ? presetIndex : IntervalValues.Length;
        _intervalSpinner.SetSelection(selectedIndex);

        if (_customIntervalEditText is not null)
        {
            _customIntervalEditText.Text = intervalMinutes.ToString();
        }

        UpdateCustomIntervalVisibility();
    }

    private void UpdateCustomIntervalVisibility()
    {
        if (_customIntervalEditText is not null)
        {
            _customIntervalEditText.Visibility = _intervalSpinner?.SelectedItemPosition == IntervalValues.Length
                ? Android.Views.ViewStates.Visible
                : Android.Views.ViewStates.Gone;
        }
    }

    private bool TryGetSelectedInterval(out int intervalMinutes)
    {
        var selectedPosition = _intervalSpinner?.SelectedItemPosition ?? -1;
        if (selectedPosition >= 0 && selectedPosition < IntervalValues.Length)
        {
            intervalMinutes = IntervalValues[selectedPosition];
            return true;
        }

        return int.TryParse(_customIntervalEditText?.Text?.Trim(), out intervalMinutes) && intervalMinutes > 0;
    }

    private void UpdateReleaseScopeText(bool allReleases)
    {
        if (_allReleasesSwitch is not null)
        {
            _allReleasesSwitch.Text = GetString(allReleases
                ? Resource.String.release_scope_all
                : Resource.String.release_scope_latest);
        }
    }

    private void StartMonitoringWithValidation()
    {
        if (!TrySaveSettings())
        {
            SetMonitoringUi(false);
            return;
        }

        if (NeedsNotificationPermission())
        {
            _pendingStartMonitoring = true;
            RequestPermissions([Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
            return;
        }

        StartMonitoring();
    }

    private void ApplySettingsAndEnsureMonitoring()
    {
        StopService(new Intent(this, typeof(GitHubDownloadCheckService)));
        StartMonitoringWithValidation();
    }

    private void StartMonitoring()
    {
        var serviceIntent = new Intent(this, typeof(GitHubDownloadCheckService));

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            StartForegroundService(serviceIntent);
        }
        else
        {
            StartService(serviceIntent);
        }

        SaveServiceEnabled(true);
        SetMonitoringUi(true);
    }

    private void StopMonitoring()
    {
        var serviceIntent = new Intent(this, typeof(GitHubDownloadCheckService));
        StopService(serviceIntent);

        SaveServiceEnabled(false);
        SetMonitoringUi(false);
    }

    private void SaveServiceEnabled(bool enabled)
    {
        var preferences = GetSharedPreferences(GitHubDownloadCheckService.PreferencesName, FileCreationMode.Private);
        preferences?.Edit()?.PutBoolean(KeyServiceEnabled, enabled)?.Apply();
    }

    private void UpdateStatusText(bool serviceEnabled)
    {
        if (_statusTextView is null)
        {
            return;
        }

        _statusTextView.Text = serviceEnabled
            ? GetString(Resource.String.service_running)
            : GetString(Resource.String.service_stopped);
    }

    private void SetMonitoringUi(bool enabled)
    {
        if (_serviceSwitch is not null)
        {
            _updatingServiceSwitch = true;
            _serviceSwitch.Checked = enabled;
            _updatingServiceSwitch = false;
        }

        UpdateStatusText(enabled);
    }

    private bool NeedsNotificationPermission()
        => Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu &&
           CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted;

    public override void OnRequestPermissionsResult(int requestCode, string[]? permissions, Permission[]? grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != NotificationPermissionRequestCode)
        {
            return;
        }

        var granted = grantResults is { Length: > 0 } && grantResults[0] == Permission.Granted;
        if (granted)
        {
            if (_pendingStartMonitoring)
            {
                StartMonitoring();
            }

            if (_pendingTestNotification)
            {
                _ = SendTestNotificationAsync();
            }

            _pendingStartMonitoring = false;
            _pendingTestNotification = false;
            return;
        }

        if (_pendingStartMonitoring)
        {
            SaveServiceEnabled(false);
            SetMonitoringUi(false);
        }

        _pendingStartMonitoring = false;
        _pendingTestNotification = false;

        Toast.MakeText(this, Resource.String.notification_permission_required, ToastLength.Short)?.Show();
    }

    private async Task SendTestNotificationAsync()
    {
        var preferences = GetSharedPreferences(GitHubDownloadCheckService.PreferencesName, FileCreationMode.Private);
        var owner = preferences?.GetString(GitHubDownloadCheckService.KeyOwner, string.Empty)?.Trim() ?? string.Empty;
        var repo = preferences?.GetString(GitHubDownloadCheckService.KeyRepo, string.Empty)?.Trim() ?? string.Empty;
        var allReleases = preferences?.GetBoolean(GitHubDownloadCheckService.KeyAllReleases, true) ?? true;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            Toast.MakeText(this, Resource.String.repo_required_message, ToastLength.Short)?.Show();
            return;
        }

        Toast.MakeText(this, Resource.String.test_notification_busy, ToastLength.Short)?.Show();

        var currentTotal = await TryGetReleaseDownloadTotalAsync(owner, repo, allReleases);
        if (!currentTotal.HasValue)
        {
            Toast.MakeText(this, Resource.String.test_notification_error, ToastLength.Short)?.Show();
            return;
        }

        EnsureTestNotificationChannel();

        var launchIntent = new Intent(this, typeof(MainActivity));
        launchIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var pendingIntent = PendingIntent.GetActivity(
            this,
            101,
            launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var lastTotalKey = BuildLastTotalKey(owner, repo, allReleases);
        var hasPrevious = preferences?.Contains(lastTotalKey) == true;
        var previousTotal = preferences?.GetInt(lastTotalKey, currentTotal.Value) ?? currentTotal.Value;
        var delta = currentTotal.Value - previousTotal;

        var comparisonText = hasPrevious
            ? delta switch
            {
                > 0 => $"(+{delta} since last check)",
                < 0 => $"({Math.Abs(delta)} lower than last check)",
                _ => "(no change since last check)"
            }
            : "(first comparison)";

        var content = $"{owner}/{repo}: {currentTotal.Value} total downloads {comparisonText}";

        preferences?.Edit()?.PutInt(lastTotalKey, currentTotal.Value)?.Apply();

        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(this, TestChannelId)
            : new Notification.Builder(this);

        var notification = builder
            .SetContentTitle(GetString(Resource.String.test_notification_title))
            .SetContentText(content)
            .SetSmallIcon(Resource.Mipmap.appicon)
            .SetContentIntent(pendingIntent)
            .SetAutoCancel(true)
            .Build();

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.Notify(TestNotificationId, notification);
    }

    private static string BuildLastTotalKey(string owner, string repo, bool allReleases)
        => LastTotalPrefix + (allReleases ? "all_" : "latest_") + owner.ToLowerInvariant() + "_" + repo.ToLowerInvariant();

    private async Task<int?> TryGetReleaseDownloadTotalAsync(string owner, string repo, bool allReleases)
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

    private void EnsureTestNotificationChannel()
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

        var channel = new NotificationChannel(
            TestChannelId,
            GetString(Resource.String.test_notification_channel_name),
            NotificationImportance.Default)
        {
            Description = GetString(Resource.String.test_notification_channel_description)
        };

        manager.CreateNotificationChannel(channel);
    }
}
