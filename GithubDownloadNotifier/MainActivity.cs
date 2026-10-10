using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;

namespace GithubDownloadNotifier;

[Activity(Label = "@string/app_name", MainLauncher = true)]
public class MainActivity : Activity
{
    private const int NotificationPermissionRequestCode = 100;
    private const string KeyServiceEnabled = "service_enabled";

    private EditText? _repoOwnerEditText;
    private EditText? _repoNameEditText;
    private EditText? _intervalEditText;
    private Switch? _serviceSwitch;
    private TextView? _statusTextView;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        _repoOwnerEditText = FindViewById<EditText>(Resource.Id.repoOwnerEditText);
        _repoNameEditText = FindViewById<EditText>(Resource.Id.repoNameEditText);
        _intervalEditText = FindViewById<EditText>(Resource.Id.intervalEditText);
        _serviceSwitch = FindViewById<Switch>(Resource.Id.serviceSwitch);
        _statusTextView = FindViewById<TextView>(Resource.Id.statusTextView);
        var saveButton = FindViewById<Button>(Resource.Id.saveButton);

        LoadSettings();

        if (saveButton is not null)
        {
            saveButton.Click += (_, _) =>
            {
                if (TrySaveSettings())
                {
                    Toast.MakeText(this, Resource.String.settings_saved, ToastLength.Short)?.Show();
                }
            };
        }

        if (_serviceSwitch is not null)
        {
            _serviceSwitch.CheckedChange += (_, args) =>
            {
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
        var serviceEnabled = preferences?.GetBoolean(KeyServiceEnabled, false) ?? false;

        if (_repoOwnerEditText is not null)
        {
            _repoOwnerEditText.Text = owner;
        }

        if (_repoNameEditText is not null)
        {
            _repoNameEditText.Text = repo;
        }

        if (_intervalEditText is not null)
        {
            _intervalEditText.Text = interval.ToString();
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
        var intervalText = _intervalEditText?.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            Toast.MakeText(this, Resource.String.repo_required_message, ToastLength.Short)?.Show();
            return false;
        }

        if (!int.TryParse(intervalText, out var intervalMinutes) || intervalMinutes <= 0)
        {
            Toast.MakeText(this, Resource.String.interval_invalid_message, ToastLength.Short)?.Show();
            return false;
        }

        var preferences = GetSharedPreferences(GitHubDownloadCheckService.PreferencesName, FileCreationMode.Private);
        preferences?.Edit()
            ?.PutString(GitHubDownloadCheckService.KeyOwner, owner)
            ?.PutString(GitHubDownloadCheckService.KeyRepo, repo)
            ?.PutInt(GitHubDownloadCheckService.KeyIntervalMinutes, intervalMinutes)
            ?.Apply();

        return true;
    }

    private void StartMonitoringWithValidation()
    {
        if (!TrySaveSettings())
        {
            if (_serviceSwitch is not null)
            {
                _serviceSwitch.Checked = false;
            }

            return;
        }

        if (NeedsNotificationPermission())
        {
            RequestPermissions([Manifest.Permission.PostNotifications], NotificationPermissionRequestCode);
            return;
        }

        StartMonitoring();
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
        UpdateStatusText(true);
    }

    private void StopMonitoring()
    {
        var serviceIntent = new Intent(this, typeof(GitHubDownloadCheckService));
        StopService(serviceIntent);

        SaveServiceEnabled(false);
        UpdateStatusText(false);
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
            StartMonitoring();
            return;
        }

        if (_serviceSwitch is not null)
        {
            _serviceSwitch.Checked = false;
        }

        Toast.MakeText(this, Resource.String.notification_permission_required, ToastLength.Short)?.Show();
    }
}
