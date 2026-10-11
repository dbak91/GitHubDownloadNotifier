using Android.Content;
using AndroidX.Work;
using GithubDownloadNotifier.Models;
using Java.Util.Concurrent;

namespace GithubDownloadNotifier.Services;

public sealed class DownloadWorkScheduler(Context context)
{
    private readonly Context _context = context.ApplicationContext;

    public void Synchronize(WatcherConfigurationDocument document)
    {
        foreach (var repository in document.Repositories)
        {
            if (repository.Download.Enabled)
            {
                Schedule(repository);
            }
            else
            {
                Cancel(repository.Id);
            }
        }
    }

    public void Schedule(RepositoryWatchConfiguration repository)
    {
        var intervalMinutes = repository.Download.Schedule.Unit == ScheduleUnit.Minutes
            ? Math.Max(15, repository.Download.Schedule.Amount)
            : Math.Max(15, repository.Download.Schedule.Amount * 1440L);
        var inputData = new Data.Builder()
            .PutString(GitHubDownloadWorker.RepositoryIdInputKey, repository.Id)
            .Build();
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected)
            .Build();
        var request = PeriodicWorkRequest.Builder
            .From<GitHubDownloadWorker>(intervalMinutes, TimeUnit.Minutes)
            .SetInputData(inputData)
            .SetConstraints(constraints)
            .Build();

        WorkManager.GetInstance(_context).EnqueueUniquePeriodicWork(
            GetUniqueWorkName(repository.Id),
            ExistingPeriodicWorkPolicy.Update,
            request);
    }

    public void Cancel(string repositoryId)
        => WorkManager.GetInstance(_context).CancelUniqueWork(GetUniqueWorkName(repositoryId));

    private static string GetUniqueWorkName(string repositoryId)
        => $"github_download_{repositoryId}";
}
