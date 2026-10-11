using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.Work;
using GithubDownloadNotifier.Models;
using GithubDownloadNotifier.Services;

namespace GithubDownloadNotifier;

[Activity(Label = "@string/app_name", MainLauncher = true)]
public sealed class MainActivity : Activity
{
    private const string LegacyPreferencesName = "github_download_notifier";
    private const string MigrationCompleteKey = "multi_repo_migration_complete";
    private LinearLayout? _repositoryContainer;
    private TextView? _emptyText;
    private WatcherStore? _store;
    private DownloadWorkScheduler? _scheduler;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        _repositoryContainer = FindViewById<LinearLayout>(Resource.Id.repositoryContainer);
        _emptyText = FindViewById<TextView>(Resource.Id.emptyTextView);
        _store = new WatcherStore(FilesDir!.AbsolutePath);
        _scheduler = new DownloadWorkScheduler(this);
        WorkManager.GetInstance(this).CancelUniqueWork("github_download_monitor");

        var addButton = FindViewById<Button>(Resource.Id.addRepositoryButton);
        if (addButton is not null)
        {
            addButton.Click += (_, _) => OpenEditor(null);
        }

        MigrateLegacyConfigurationOnce();
    }

    protected override void OnResume()
    {
        base.OnResume();
        RenderRepositories();
    }

    private void MigrateLegacyConfigurationOnce()
    {
        var preferences = GetSharedPreferences(LegacyPreferencesName, FileCreationMode.Private);
        if (preferences?.GetBoolean(MigrationCompleteKey, false) == true || _store is null)
        {
            return;
        }

        var existing = _store.Load();
        if (existing.Repositories.Count == 0)
        {
            var migrated = LegacySettingsMigrator.Migrate(new LegacyWatcherSettings(
                preferences?.GetString("repo_owner", string.Empty) ?? string.Empty,
                preferences?.GetString("repo_name", string.Empty) ?? string.Empty,
                preferences?.GetInt("check_interval_minutes", 60) ?? 60,
                preferences?.GetBoolean("all_releases", true) ?? true,
                preferences?.GetBoolean("always_notify", false) ?? false,
                preferences?.GetBoolean("service_enabled", false) ?? false));

            if (migrated.Repositories.Count > 0)
            {
                _store.Save(migrated);
                _scheduler?.Synchronize(migrated);
            }
        }

        preferences?.Edit()?.PutBoolean(MigrationCompleteKey, true)?.Apply();
    }

    private void RenderRepositories()
    {
        if (_store is null || _repositoryContainer is null)
        {
            return;
        }

        var document = _store.Load();
        _repositoryContainer.RemoveAllViews();
        if (_emptyText is not null)
        {
            _emptyText.Visibility = document.Repositories.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
        }

        foreach (var repository in document.Repositories.OrderBy(item => item.Owner).ThenBy(item => item.Name))
        {
            var view = LayoutInflater.Inflate(Resource.Layout.repository_list_item, _repositoryContainer, false);
            view.FindViewById<TextView>(Resource.Id.repositoryNameText)!.Text = $"{repository.Owner}/{repository.Name}";
            view.FindViewById<TextView>(Resource.Id.repositorySummaryText)!.Text = BuildSummary(repository);
            view.FindViewById<Button>(Resource.Id.editRepositoryButton)!.Click += (_, _) => OpenEditor(repository.Id);
            view.FindViewById<Button>(Resource.Id.deleteRepositoryButton)!.Click += (_, _) => ConfirmDelete(repository);
            _repositoryContainer.AddView(view);
        }
    }

    private static string BuildSummary(RepositoryWatchConfiguration repository)
    {
        var download = repository.Download.Enabled
            ? $"Downloads: on, {FormatSchedule(repository.Download.Schedule)}"
            : "Downloads: off";
        var release = repository.Release.Enabled ? "Releases: on" : "Releases: off";
        return $"{download}  •  {release}";
    }

    private static string FormatSchedule(WatchSchedule schedule)
        => schedule.Unit switch
        {
            ScheduleUnit.Days => $"every {schedule.Amount} {(schedule.Amount == 1 ? "day" : "days")} at " +
                DateTime.Today.AddHours(schedule.RunAtHour).AddMinutes(schedule.RunAtMinute).ToString("t"),
            _ => $"every {schedule.Amount} min"
        };

    private void OpenEditor(string? repositoryId)
    {
        var intent = new Intent(this, typeof(RepositoryEditorActivity));
        if (!string.IsNullOrWhiteSpace(repositoryId))
        {
            intent.PutExtra(RepositoryEditorActivity.RepositoryIdExtra, repositoryId);
        }

        StartActivity(intent);
    }

    private void ConfirmDelete(RepositoryWatchConfiguration repository)
    {
        new AlertDialog.Builder(this)
            .SetTitle(Resource.String.delete_repository_title)
            .SetMessage(GetString(Resource.String.delete_repository_message, $"{repository.Owner}/{repository.Name}"))
            .SetPositiveButton(Resource.String.delete_button, (_, _) => DeleteRepository(repository.Id))
            .SetNegativeButton(Android.Resource.String.Cancel, (_, _) => { })
            .Show();
    }

    private void DeleteRepository(string repositoryId)
    {
        if (_store is null)
        {
            return;
        }

        _store.Update(document => document.Repositories.RemoveAll(item => item.Id == repositoryId));
        _scheduler?.Cancel(repositoryId);
        RenderRepositories();
    }
}
