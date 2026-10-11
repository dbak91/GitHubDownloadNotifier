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
    private const int DownloadLogFileRequestCode = 201;
    private const int ReleaseLogFileRequestCode = 202;
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
    private Switch? _downloadLoggingSwitch;
    private LinearLayout? _downloadLoggingOptions;
    private TextView? _downloadLogFileText;
    private Switch? _releaseEnabledSwitch;
    private EditText? _currentTagEditText;
    private Spinner? _releaseIntervalSpinner;
    private EditText? _customReleaseDaysEditText;
    private Button? _releaseRunAtTimeButton;
    private Switch? _releaseLoggingSwitch;
    private LinearLayout? _releaseLoggingOptions;
    private TextView? _releaseLogFileText;
    private WatcherStore? _store;
    private DownloadWorkScheduler? _scheduler;
    private ReleaseWorkScheduler? _releaseScheduler;
    private RepositoryWatchConfiguration? _configuration;
    private bool _saveAfterPermission;
    private bool _testAfterPermission;
    private int _runAtHour = DateTime.Now.Hour;
    private int _runAtMinute = DateTime.Now.Minute;
    private int _releaseRunAtHour = DateTime.Now.Hour;
    private int _releaseRunAtMinute = DateTime.Now.Minute;

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
        _downloadLoggingSwitch = FindViewById<Switch>(Resource.Id.downloadLoggingSwitch);
        _downloadLoggingOptions = FindViewById<LinearLayout>(Resource.Id.downloadLoggingOptions);
        _downloadLogFileText = FindViewById<TextView>(Resource.Id.downloadLogFileText);
        _releaseEnabledSwitch = FindViewById<Switch>(Resource.Id.releaseEnabledSwitch);
        _currentTagEditText = FindViewById<EditText>(Resource.Id.currentTagEditText);
        _releaseIntervalSpinner = FindViewById<Spinner>(Resource.Id.releaseIntervalSpinner);
        _customReleaseDaysEditText = FindViewById<EditText>(Resource.Id.customReleaseDaysEditText);
        _releaseRunAtTimeButton = FindViewById<Button>(Resource.Id.releaseRunAtTimeButton);
        _releaseLoggingSwitch = FindViewById<Switch>(Resource.Id.releaseLoggingSwitch);
        _releaseLoggingOptions = FindViewById<LinearLayout>(Resource.Id.releaseLoggingOptions);
        _releaseLogFileText = FindViewById<TextView>(Resource.Id.releaseLogFileText);
        _store = new WatcherStore(FilesDir!.AbsolutePath);
        _scheduler = new DownloadWorkScheduler(this);
        _releaseScheduler = new ReleaseWorkScheduler(this);

        ConfigureIntervalSpinner();
        ConfigureUnitSpinner();
        ConfigureReleaseIntervalSpinner();
        LoadConfiguration();

        _allReleasesSwitch!.CheckedChange += (_, args) => UpdateScopeText(args.IsChecked);
        _alwaysNotifySwitch!.CheckedChange += (_, args) => UpdateNotificationText(args.IsChecked);
        _runAtTimeButton!.Click += (_, _) => ShowTimePicker();
        _releaseRunAtTimeButton!.Click += (_, _) => ShowReleaseTimePicker();
        _downloadLoggingSwitch!.CheckedChange += (_, _) => UpdateLoggingControls(false);
        _releaseLoggingSwitch!.CheckedChange += (_, _) => UpdateLoggingControls(true);
        FindViewById<Button>(Resource.Id.downloadLogFileButton)!.Click += (_, _) => ChooseLogFile(false);
        FindViewById<Button>(Resource.Id.releaseLogFileButton)!.Click += (_, _) => ChooseLogFile(true);
        FindViewById<Button>(Resource.Id.downloadLogFieldsButton)!.Click += (_, _) => ChooseLogFields(false);
        FindViewById<Button>(Resource.Id.releaseLogFieldsButton)!.Click += (_, _) => ChooseLogFields(true);
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

    private void ConfigureReleaseIntervalSpinner()
    {
        var labels = new[]
        {
            GetString(Resource.String.release_interval_1_day),
            GetString(Resource.String.release_interval_3_days),
            GetString(Resource.String.release_interval_7_days),
            GetString(Resource.String.release_interval_1_month),
            GetString(Resource.String.release_interval_custom_days)
        };
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, labels);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _releaseIntervalSpinner!.Adapter = adapter;
        _releaseIntervalSpinner.ItemSelected += (_, _) => UpdateReleaseScheduleControls();
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
        _releaseEnabledSwitch!.Checked = _configuration.Release.Enabled;
        _currentTagEditText!.Text = _configuration.Release.PendingTag ?? _configuration.Release.CurrentTag;
        SelectReleaseInterval(_configuration.Release.Schedule);
        EnsureDefaultLogFields(_configuration.Download.Logging, false);
        EnsureDefaultLogFields(_configuration.Release.Logging, true);
        _downloadLoggingSwitch!.Checked = _configuration.Download.Logging.Enabled;
        _releaseLoggingSwitch!.Checked = _configuration.Release.Logging.Enabled;
        UpdateLoggingControls(false);
        UpdateLoggingControls(true);
    }

    private void BeginSave()
    {
        if (!TryApplyForm())
        {
            return;
        }

        if ((_configuration!.Download.Enabled || _configuration.Release.Enabled) && NeedsNotificationPermission())
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
        if (_configuration.Release.Enabled)
        {
            _releaseScheduler!.Schedule(_configuration);
        }
        else
        {
            _releaseScheduler!.Cancel(_configuration.Id);
        }

        Toast.MakeText(this, Resource.String.settings_saved, ToastLength.Short)?.Show();
        if ((!_configuration.Download.Enabled && !_configuration.Release.Enabled) || !ShowBatteryOptimizationWarningOnce())
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
        if (!ValidateLogging(_configuration!.Download.Logging, _downloadLoggingSwitch?.Checked == true) ||
            !ValidateLogging(_configuration.Release.Logging, _releaseLoggingSwitch?.Checked == true))
        {
            return false;
        }

        var releaseEnabled = _releaseEnabledSwitch?.Checked == true;
        var currentTag = _currentTagEditText?.Text?.Trim() ?? string.Empty;
        if (releaseEnabled && string.IsNullOrWhiteSpace(currentTag))
        {
            Toast.MakeText(this, Resource.String.release_tag_required_message, ToastLength.Short)?.Show();
            return false;
        }
        if (!TryGetReleaseSchedule(out var releaseSchedule))
        {
            Toast.MakeText(this, Resource.String.release_interval_invalid_message, ToastLength.Short)?.Show();
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
        if (SchedulesMatch(_configuration.Release.Schedule, releaseSchedule))
        {
            releaseSchedule.LastScheduledUtc = _configuration.Release.Schedule.LastScheduledUtc;
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
        _configuration.Download.Logging.Enabled = _downloadLoggingSwitch?.Checked == true;
        _configuration.Release.Enabled = releaseEnabled;
        if (!string.Equals(_configuration.Release.CurrentTag, currentTag, StringComparison.OrdinalIgnoreCase))
        {
            _configuration.Release.PendingTag = null;
        }
        _configuration.Release.CurrentTag = currentTag;
        _configuration.Release.Schedule = releaseSchedule;
        _configuration.Release.Logging.Enabled = _releaseLoggingSwitch?.Checked == true;
        return true;
    }

    private bool ValidateLogging(WatcherLogConfiguration logging, bool enabled)
    {
        if (!enabled)
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(logging.DirectoryUri))
        {
            Toast.MakeText(this, Resource.String.log_file_required_message, ToastLength.Short)?.Show();
            return false;
        }
        if (logging.Fields.Count == 0)
        {
            Toast.MakeText(this, Resource.String.log_fields_required_message, ToastLength.Short)?.Show();
            return false;
        }
        return true;
    }

    private void UpdateLoggingControls(bool release)
    {
        var logging = release ? _configuration?.Release.Logging : _configuration?.Download.Logging;
        var options = release ? _releaseLoggingOptions : _downloadLoggingOptions;
        var fileText = release ? _releaseLogFileText : _downloadLogFileText;
        var enabled = release ? _releaseLoggingSwitch?.Checked == true : _downloadLoggingSwitch?.Checked == true;
        options!.Visibility = enabled ? Android.Views.ViewStates.Visible : Android.Views.ViewStates.Gone;
        fileText!.Text = string.IsNullOrWhiteSpace(logging?.FileName)
            ? GetString(Resource.String.no_log_file_selected)
            : logging.FileName;
    }

    private void ChooseLogFile(bool release)
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("text/csv");
        var repository = $"{_ownerEditText?.Text?.Trim()}-{_nameEditText?.Text?.Trim()}".Trim('-');
        intent.PutExtra(Intent.ExtraTitle, $"{(string.IsNullOrWhiteSpace(repository) ? "github" : repository)}-{(release ? "releases" : "downloads")}.csv");
        StartActivityForResult(intent, release ? ReleaseLogFileRequestCode : DownloadLogFileRequestCode);
    }

    private void ChooseLogFields(bool release)
    {
        var logging = release ? _configuration!.Release.Logging : _configuration!.Download.Logging;
        var available = release
            ? new[] { LogField.Timestamp, LogField.Repository, LogField.Status, LogField.Error, LogField.BaselineTag, LogField.DetectedTag, LogField.ReleaseName, LogField.ReleasePublishedAt, LogField.ReleaseIsPrerelease, LogField.ReleaseUrl }
            : new[] { LogField.Timestamp, LogField.Repository, LogField.Status, LogField.Error, LogField.DownloadScope, LogField.PreviousDownloadTotal, LogField.CurrentDownloadTotal, LogField.DownloadDelta };
        var labels = available.Select(FormatLogField).ToArray();
        var selected = available.Select(logging.Fields.Contains).ToArray();
        new AlertDialog.Builder(this)
            .SetTitle(Resource.String.log_fields_title)
            .SetMultiChoiceItems(labels, selected, (_, args) => selected[args.Which] = args.IsChecked)
            .SetPositiveButton(Android.Resource.String.Ok, (_, _) =>
            {
                logging.Fields = available.Where((_, index) => selected[index]).ToList();
            })
            .SetNegativeButton(Android.Resource.String.Cancel, (_, _) => { })
            .Show();
    }

    private static string FormatLogField(LogField field)
        => string.Concat(field.ToString().Select((character, index) => index > 0 && char.IsUpper(character) ? $" {character}" : character.ToString()));

    private static void EnsureDefaultLogFields(WatcherLogConfiguration logging, bool release)
    {
        if (logging.Fields.Count > 0)
        {
            return;
        }
        logging.Fields = release
            ? [LogField.Timestamp, LogField.Repository, LogField.Status, LogField.BaselineTag, LogField.DetectedTag]
            : [LogField.Timestamp, LogField.Repository, LogField.Status, LogField.CurrentDownloadTotal, LogField.DownloadDelta];
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data is null ||
            requestCode is not (DownloadLogFileRequestCode or ReleaseLogFileRequestCode))
        {
            return;
        }

        ContentResolver?.TakePersistableUriPermission(data.Data, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        var release = requestCode == ReleaseLogFileRequestCode;
        var logging = release ? _configuration!.Release.Logging : _configuration!.Download.Logging;
        logging.DirectoryUri = data.Data.ToString() ?? string.Empty;
        logging.FileName = data.Data.LastPathSegment ?? "watcher-log.csv";
        UpdateLoggingControls(release);
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

    private void SelectReleaseInterval(WatchSchedule schedule)
    {
        var selected = schedule.Unit == ScheduleUnit.Months
            ? 3
            : schedule.Amount switch { 1 => 0, 3 => 1, 7 => 2, _ => 4 };
        _releaseIntervalSpinner!.SetSelection(selected);
        _customReleaseDaysEditText!.Text = schedule.Amount.ToString();
        _releaseRunAtHour = schedule.RunAtHour;
        _releaseRunAtMinute = schedule.RunAtMinute;
        UpdateReleaseScheduleControls();
    }

    private bool TryGetReleaseSchedule(out WatchSchedule schedule)
    {
        var runAt = new TimeSpan(_releaseRunAtHour, _releaseRunAtMinute, 0);
        switch (_releaseIntervalSpinner?.SelectedItemPosition)
        {
            case 0: schedule = WatchSchedule.Days(1, runAt); return true;
            case 1: schedule = WatchSchedule.Days(3, runAt); return true;
            case 2: schedule = WatchSchedule.Days(7, runAt); return true;
            case 3: schedule = WatchSchedule.Months(1, runAt, DateTime.Now.Day); return true;
            default:
                if (int.TryParse(_customReleaseDaysEditText?.Text?.Trim(), out var days) && days >= 1)
                {
                    schedule = WatchSchedule.Days(days, runAt);
                    return true;
                }
                schedule = WatchSchedule.Days(1, runAt);
                return false;
        }
    }

    private void UpdateReleaseScheduleControls()
    {
        _customReleaseDaysEditText!.Visibility = _releaseIntervalSpinner?.SelectedItemPosition == 4
            ? Android.Views.ViewStates.Visible
            : Android.Views.ViewStates.Gone;
        _releaseRunAtTimeButton!.Text = GetString(
            Resource.String.run_at_time,
            DateTime.Today.AddHours(_releaseRunAtHour).AddMinutes(_releaseRunAtMinute).ToString("t"));
    }

    private void ShowReleaseTimePicker()
        => new TimePickerDialog(this, (_, args) =>
        {
            _releaseRunAtHour = args.HourOfDay;
            _releaseRunAtMinute = args.Minute;
            UpdateReleaseScheduleControls();
        }, _releaseRunAtHour, _releaseRunAtMinute, false).Show();

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
