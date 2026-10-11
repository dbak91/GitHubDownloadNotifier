using GithubDownloadNotifier.Models;

namespace GithubDownloadNotifier.Services;

public sealed record ApiRequestBudgetResult(int PeakRequests, DateTimeOffset WindowStartUtc)
{
    public bool IsWithinBudget(int budget) => PeakRequests <= budget;
}

public static class ApiRequestBudgetCalculator
{
    public const int ScheduledRequestsPerHourBudget = 100;
    public const int MinimumRepositoryCountForProjection = 16;
    private static readonly TimeSpan ProjectionDuration = TimeSpan.FromDays(370);
    private static readonly TimeSpan WindowDuration = TimeSpan.FromHours(1);

    public static ApiRequestBudgetResult Calculate(
        WatcherConfigurationDocument document,
        DateTimeOffset nowUtc,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(timeZone);

        var projectionEnd = nowUtc.Add(ProjectionDuration);
        var occurrences = new List<DateTimeOffset>();
        foreach (var repository in document.Repositories)
        {
            if (repository.Download.Enabled)
            {
                AddOccurrences(occurrences, repository.Download.Schedule, nowUtc, projectionEnd, timeZone);
            }
            if (repository.Release.Enabled)
            {
                AddOccurrences(occurrences, repository.Release.Schedule, nowUtc, projectionEnd, timeZone);
            }
        }

        if (occurrences.Count == 0)
        {
            return new ApiRequestBudgetResult(0, nowUtc);
        }

        occurrences.Sort();
        var peak = 0;
        var peakStart = occurrences[0];
        var windowStartIndex = 0;
        for (var windowEndIndex = 0; windowEndIndex < occurrences.Count; windowEndIndex++)
        {
            while (occurrences[windowEndIndex] - occurrences[windowStartIndex] >= WindowDuration)
            {
                windowStartIndex++;
            }

            var count = windowEndIndex - windowStartIndex + 1;
            if (count > peak)
            {
                peak = count;
                peakStart = occurrences[windowStartIndex];
            }
        }

        return new ApiRequestBudgetResult(peak, peakStart);
    }

    private static void AddOccurrences(
        List<DateTimeOffset> occurrences,
        WatchSchedule source,
        DateTimeOffset nowUtc,
        DateTimeOffset projectionEnd,
        TimeZoneInfo timeZone)
    {
        var schedule = CopySchedule(source);
        var cursor = nowUtc;
        while (true)
        {
            var next = ScheduleCalculator.GetNextRun(cursor, schedule, timeZone);
            if (next > projectionEnd)
            {
                return;
            }

            occurrences.Add(next);
            schedule.LastScheduledUtc = next;
            cursor = next;
        }
    }

    private static WatchSchedule CopySchedule(WatchSchedule source) => new()
    {
        Unit = source.Unit,
        Amount = source.Amount,
        RunAtHour = source.RunAtHour,
        RunAtMinute = source.RunAtMinute,
        AnchorDayOfMonth = source.AnchorDayOfMonth,
        LastScheduledUtc = source.LastScheduledUtc
    };
}
