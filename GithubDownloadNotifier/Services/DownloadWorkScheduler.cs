using Android.Content;
using AndroidX.Work;
using GithubDownloadNotifier.Models;
using Java.Util.Concurrent;

namespace GithubDownloadNotifier.Services;

public sealed class DownloadWorkScheduler(Context context)
{
    private readonly Context _context = context.ApplicationContext;
    private readonly WatcherStore _store = new(context.FilesDir!.AbsolutePath);

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
        => ScheduleInternal(repository, ExistingWorkPolicy.Replace);

    public void ScheduleNext(string repositoryId)
    {
        var repository = _store.Load().Repositories.FirstOrDefault(item => item.Id == repositoryId);
        if (repository is not null && repository.Download.Enabled)
        {
            ScheduleInternal(repository, ExistingWorkPolicy.AppendOrReplace);
        }
    }

    public void Cancel(string repositoryId)
        => WorkManager.GetInstance(_context).CancelUniqueWork(GetUniqueWorkName(repositoryId));

    private void ScheduleInternal(RepositoryWatchConfiguration repository, ExistingWorkPolicy policy)
    {
        var now = DateTimeOffset.UtcNow;
        var nextRun = ScheduleCalculator.GetNextRun(now, repository.Download.Schedule, TimeZoneInfo.Local);
        var delayMilliseconds = Math.Max(0, (long)(nextRun - now).TotalMilliseconds);
        var inputData = new Data.Builder()
            .PutString(GitHubDownloadWorker.RepositoryIdInputKey, repository.Id)
            .PutLong(GitHubDownloadWorker.ScheduledRunUtcInputKey, nextRun.ToUnixTimeMilliseconds())
            .Build();
        var constraints = new Constraints.Builder()
            .SetRequiredNetworkType(NetworkType.Connected)
            .Build();
        var builder = OneTimeWorkRequest.Builder.From<GitHubDownloadWorker>();
        builder.SetInitialDelay(delayMilliseconds, TimeUnit.Milliseconds);
        builder.SetInputData(inputData);
        builder.SetConstraints(constraints);
        var request = builder.Build();

        WorkManager.GetInstance(_context).EnqueueUniqueWork(
            GetUniqueWorkName(repository.Id),
            policy,
            request);
    }

    private static string GetUniqueWorkName(string repositoryId)
        => $"github_download_{repositoryId}";
}