using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using GithubDownloadNotifier.Models;
using GithubDownloadNotifier.Services;

namespace GithubDownloadNotifier;

[Activity(Label = "@string/repository_editor_title")]
public sealed class RepositoryEditorActivity : Activity
{
    public const string RepositoryIdExtra = "repository_id";

    private static readonly int[] IntervalValues = [15, 30, 60, 720];
    private const int NotificationPermissionRequestCode = 101;
    private const string PreferencesName = "github_download_notifier";
    private const string BatteryWarningShownKey = "battery_warning_shown";
    private const string TestChannelId = "manual_test_channel";
    private const int TestNotificationId = 2001;

    private EditText? _ownerEditText;
    private EditText? _nameEditText;
    private Switch? _downloadEnabledSwitch;
    private Spinner? _intervalSpinner;
    private LinearLayout? _customIntervalLayout;
    private EditText? _customIntervalEditText;
    private Spinner? _customIntervalUnitSpinner;
    private Button? _runAtTimeButton;
    private Switch? _allReleasesSwitch;
    private Switch? _alwaysNotifySwitch;
    private TextView? _releaseSummaryText;
    private WatcherStore? _store;
    private DownloadWorkScheduler? _scheduler;
    private RepositoryWatchConfiguration? _configuration;
    private bool _saveAfterPermission;
    private bool _testAfterPermission;
    private int _runAtHour = DateTime.Now.Hour;
    private int _runAtMinute = DateTime.Now.Minute;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_repository_editor);

        _ownerEditText = FindViewById<EditText>(Resource.Id.repoOwnerEditText);
        _nameEditText = FindViewById<EditText>(Resource.Id.repoNameEditText);
        _downloadEnabledSwitch = FindViewById<Switch>(Resource.Id.downloadEnabledSwitch);
        _intervalSpinner = FindViewById<Spinner>(Resource.Id.intervalSpinner);
        _customIntervalLayout = FindViewById<LinearLayout>(Resource.Id.customIntervalLayout);
        _customIntervalEditText = FindViewById<EditText>(Resource.Id.customIntervalEditText);
        _customIntervalUnitSpinner = FindViewById<Spinner>(Resource.Id.customIntervalUnitSpinner);
        _runAtTimeButton = FindViewById<Button>(Resource.Id.runAtTimeButton);
        _allReleasesSwitch = FindViewById<Switch>(Resource.Id.allReleasesSwitch);
        _alwaysNotifySwitch = FindViewById<Switch>(Resource.Id.alwaysNotifySwitch);
        _releaseSummaryText = FindViewById<TextView>(Resource.Id.releaseSummaryText);
        _store = new WatcherStore(FilesDir!.AbsolutePath);
        _scheduler = new DownloadWorkScheduler(this);

        ConfigureIntervalSpinner();
        ConfigureUnitSpinner();
        LoadConfiguration();

        _allReleasesSwitch!.CheckedChange += (_, args) => UpdateScopeText(args.IsChecked);
        _alwaysNotifySwitch!.CheckedChange += (_, args) => UpdateNotificationText(args.IsChecked);
        _runAtTimeButton!.Click += (_, _) => ShowTimePicker();
        FindViewById<Button>(Resource.Id.saveRepositoryButton)!.Click += (_, _) => BeginSave();
        FindViewById<Button>(Resource.Id.testDownloadButton)!.Click += async (_, _) => await BeginTestAsync();
    }

    private void ConfigureIntervalSpinner()
    {
        var labels = new[]
        {
            GetString(Resource.String.interval_15_minutes),
            GetString(Resource.String.interval_30_minutes),
            GetString(Resource.String.interval_1_hour),
            GetString(Resource.String.interval_12_hours),
            GetString(Resource.String.interval_1_day),
            GetString(Resource.String.interval_custom)
        };
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, labels);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _intervalSpinner!.Adapter = adapter;
        _intervalSpinner.ItemSelected += (_, _) => UpdateScheduleControls();
    }

    private void ConfigureUnitSpinner()
    {
        var labels = new[]
        {
            GetString(Resource.String.interval_unit_minutes),
            GetString(Resource.String.interval_unit_days)
        };
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, labels);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _customIntervalUnitSpinner!.Adapter = adapter;
        _customIntervalUnitSpinner.ItemSelected += (_, _) => UpdateScheduleControls();
    }

    private void LoadConfiguration()
    {
        var repositoryId = Intent?.GetStringExtra(RepositoryIdExtra);
        var document = _store!.Load();
        _configuration = document.Repositories.FirstOrDefault(item => item.Id == repositoryId)
            ?? new RepositoryWatchConfiguration();

        _ownerEditText!.Text = _configuration.Owner;
        _nameEditText!.Text = _configuration.Name;
        _downloadEnabledSwitch!.Checked = _configuration.Download.Enabled;
        _allReleasesSwitch!.Checked = _configuration.Download.Scope == DownloadScope.AllReleases;
        _alwaysNotifySwitch!.Checked = _configuration.Download.NotificationMode == NotificationMode.EveryCheck;
        SelectInterval(_configuration.Download.Schedule);
        UpdateScopeText(_allReleasesSwitch.Checked);
        UpdateNotificationText(_alwaysNotifySwitch.Checked);
        _releaseSummaryText!.Text = _configuration.Release.Enabled
            ? GetString(Resource.String.release_watcher_on)
            : GetString(Resource.String.release_watcher_off);
    }

    private void BeginSave()
    {
        if (!TryApplyForm())
        {
            return;
        }

        if (_configuration!.Download.Enabled && NeedsNotificationPermission())
        {
            _saveAfterPermission = true;
            RequestPermissions([Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
            return;
        }

        SaveAndFinish();
    }

    private void SaveAndFinish()
    {
        _store!.Update(document =>
        {
            var index = document.Repositories.FindIndex(item => item.Id == _configuration!.Id);
            if (index >= 0)
            {
                document.Repositories[index] = _configuration!;
            }
            else
            {
                document.Repositories.Add(_configuration!);
            }
        });
        if (_configuration!.Download.Enabled)
        {
            _scheduler!.Schedule(_configuration);
        }
        else
        {
            _scheduler!.Cancel(_configuration.Id);
        }

        Toast.MakeText(this, Resource.String.settings_saved, ToastLength.Short)?.Show();
        if (!_configuration.Download.Enabled || !ShowBatteryOptimizationWarningOnce())
        {
            Finish();
        }
    }

    private bool TryApplyForm()
    {
        var owner = _ownerEditText?.Text?.Trim() ?? string.Empty;
        var name = _nameEditText?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(name))
        {
            Toast.MakeText(this, Resource.String.repo_required_message, ToastLength.Short)?.Show();
            return false;
        }

        if (!TryGetSchedule(out var schedule))
        {
            Toast.MakeText(this, Resource.String.interval_invalid_message, ToastLength.Short)?.Show();
            return false;
        }

        if (SchedulesMatch(_configuration!.Download.Schedule, schedule))
        {
            schedule.LastScheduledUtc = _configuration.Download.Schedule.LastScheduledUtc;
        }

        _configuration.Owner = owner;
        _configuration.Name = name;
        _configuration.Download.Enabled = _downloadEnabledSwitch?.Checked == true;
        _configuration.Download.Scope = _allReleasesSwitch?.Checked == true
            ? DownloadScope.AllReleases
            : DownloadScope.LatestRelease;
        _configuration.Download.NotificationMode = _alwaysNotifySwitch?.Checked == true
            ? NotificationMode.EveryCheck
            : NotificationMode.ChangesOnly;
        _configuration.Download.Schedule = schedule;
        return true;
    }

    private async Task BeginTestAsync()
    {
        if (!TryApplyForm())
        {
            return;
        }

        if (NeedsNotificationPermission())
        {
            _testAfterPermission = true;
            RequestPermissions([Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
            return;
        }

        await SendTestNotificationAsync();
    }

    private async Task SendTestNotificationAsync()
    {
        Toast.MakeText(this, Resource.String.test_notification_busy, ToastLength.Short)?.Show();
        try
        {
            using var httpClient = new HttpClient();
            var apiClient = new GitHubApiClient(httpClient);
            var currentTotal = await apiClient.GetDownloadTotalAsync(
                _configuration!.Owner,
                _configuration.Name,
                _configuration.Download.Scope);
            var previous = _configuration.Download.LastDownloadTotal;
            var comparison = previous.HasValue ? currentTotal - previous.Value : (int?)null;
            _configuration.Download.LastDownloadTotal = currentTotal;
            _store!.Update(document =>
            {
                var saved = document.Repositories.FirstOrDefault(item => item.Id == _configuration.Id);
                if (saved is not null)
                {
                    saved.Download.LastDownloadTotal = currentTotal;
                }
            });

            EnsureTestChannel();
            var message = comparison switch
            {
                > 0 => $"{currentTotal} total downloads (+{comparison})",
                0 => $"{currentTotal} total downloads (no change)",
                < 0 => $"{currentTotal} total downloads ({comparison})",
                _ => $"{currentTotal} total downloads (first comparison)"
            };
            var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
                ? new Notification.Builder(this, TestChannelId)
                : new Notification.Builder(this);
            var notification = builder
                .SetContentTitle($"{_configuration.Owner}/{_configuration.Name}")
                .SetContentText(message)
                .SetSmallIcon(Resource.Mipmap.appicon)
                .SetAutoCancel(true)
                .Build();
            ((NotificationManager?)GetSystemService(NotificationService))?.Notify(TestNotificationId, notification);
        }
        catch
        {
            Toast.MakeText(this, Resource.String.test_notification_error, ToastLength.Short)?.Show();
        }
    }

    private void SelectInterval(WatchSchedule schedule)
    {
        if (schedule.Unit == ScheduleUnit.Days && schedule.Amount == 1)
        {
            _intervalSpinner!.SetSelection(IntervalValues.Length);
        }
        else if (schedule.Unit == ScheduleUnit.Minutes)
        {
            var presetIndex = Array.IndexOf(IntervalValues, schedule.Amount);
            _intervalSpinner!.SetSelection(presetIndex >= 0 ? presetIndex : IntervalValues.Length + 1);
        }
        else
        {
            _intervalSpinner!.SetSelection(IntervalValues.Length + 1);
        }

        _customIntervalEditText!.Text = schedule.Amount.ToString();
        _customIntervalUnitSpinner!.SetSelection(schedule.Unit == ScheduleUnit.Days ? 1 : 0);
        if (schedule.Unit == ScheduleUnit.Days)
        {
            _runAtHour = schedule.RunAtHour;
            _runAtMinute = schedule.RunAtMinute;
        }
        UpdateScheduleControls();
    }

    private bool TryGetSchedule(out WatchSchedule schedule)
    {
        var selected = _intervalSpinner?.SelectedItemPosition ?? -1;
        if (selected >= 0 && selected < IntervalValues.Length)
        {
            schedule = WatchSchedule.Minutes(IntervalValues[selected]);
            return true;
        }

        if (selected == IntervalValues.Length)
        {
            schedule = WatchSchedule.Days(1, new TimeSpan(_runAtHour, _runAtMinute, 0));
            return true;
        }

        var isDays = _customIntervalUnitSpinner?.SelectedItemPosition == 1;
        if (int.TryParse(_customIntervalEditText?.Text?.Trim(), out var amount) &&
            amount >= (isDays ? 1 : 15))
        {
            schedule = isDays
                ? WatchSchedule.Days(amount, new TimeSpan(_runAtHour, _runAtMinute, 0))
                : WatchSchedule.Minutes(amount);
            return true;
        }

        schedule = WatchSchedule.Minutes(15);
        return false;
    }

    private void UpdateScheduleControls()
    {
        var selected = _intervalSpinner?.SelectedItemPosition ?? -1;
        var custom = selected == IntervalValues.Length + 1;
        var days = selected == IntervalValues.Length ||
            (custom && _customIntervalUnitSpinner?.SelectedItemPosition == 1);
        _customIntervalLayout!.Visibility = custom ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone;
        _runAtTimeButton!.Visibility = days ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone;
        _runAtTimeButton.Text = GetString(
            Resource.String.run_at_time,
            DateTime.Today.AddHours(_runAtHour).AddMinutes(_runAtMinute).ToString("t"));
    }

    private void ShowTimePicker()
        => new TimePickerDialog(this, (_, args) =>
        {
            _runAtHour = args.HourOfDay;
            _runAtMinute = args.Minute;
            UpdateScheduleControls();
        }, _runAtHour, _runAtMinute, false).Show();

    private static bool SchedulesMatch(WatchSchedule left, WatchSchedule right)
        => left.Unit == right.Unit &&
           left.Amount == right.Amount &&
           left.RunAtHour == right.RunAtHour &&
           left.RunAtMinute == right.RunAtMinute;

    private void UpdateScopeText(bool allReleases)
        => _allReleasesSwitch!.Text = GetString(allReleases
            ? Resource.String.release_scope_all
            : Resource.String.release_scope_latest);

    private void UpdateNotificationText(bool alwaysNotify)
        => _alwaysNotifySwitch!.Text = GetString(alwaysNotify
            ? Resource.String.notify_always
            : Resource.String.notify_updates_only);

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
        if (!granted)
        {
            _saveAfterPermission = false;
            _testAfterPermission = false;
            Toast.MakeText(this, Resource.String.notification_permission_required, ToastLength.Short)?.Show();
            return;
        }

        if (_saveAfterPermission)
        {
            _saveAfterPermission = false;
            SaveAndFinish();
        }

        if (_testAfterPermission)
        {
            _testAfterPermission = false;
            _ = SendTestNotificationAsync();
        }
    }

    private bool ShowBatteryOptimizationWarningOnce()
    {
        var preferences = GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        if (preferences?.GetBoolean(BatteryWarningShownKey, false) == true)
        {
            return false;
        }

        preferences?.Edit()?.PutBoolean(BatteryWarningShownKey, true)?.Apply();
        new AlertDialog.Builder(this)
            .SetTitle(Resource.String.battery_warning_title)
            .SetMessage(Resource.String.battery_warning_message)
            .SetPositiveButton(Resource.String.open_battery_settings, (_, _) =>
            {
                try
                {
                    StartActivity(new Intent(Android.Provider.Settings.ActionIgnoreBatteryOptimizationSettings));
                }
                catch (ActivityNotFoundException)
                {
                    StartActivity(new Intent(Android.Provider.Settings.ActionSettings));
                }
                Finish();
            })
            .SetNegativeButton(Resource.String.continue_button, (_, _) => Finish())
            .Show();
        return true;
    }

    private void EnsureTestChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.CreateNotificationChannel(new NotificationChannel(
            TestChannelId,
            GetString(Resource.String.test_notification_channel_name),
            NotificationImportance.Default));
    }
}
