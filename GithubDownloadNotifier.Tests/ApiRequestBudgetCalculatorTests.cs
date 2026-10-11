using GithubDownloadNotifier.Models;
using GithubDownloadNotifier.Services;

namespace GithubDownloadNotifier.Tests;

public sealed class ApiRequestBudgetCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TwentySixAlignedFifteenMinuteWatchersExceedBudget()
    {
        var document = CreateMinuteWatchers(26, 15);

        var result = ApiRequestBudgetCalculator.Calculate(document, Now, TimeZoneInfo.Utc);

        Assert.Equal(104, result.PeakRequests);
        Assert.False(result.IsWithinBudget(ApiRequestBudgetCalculator.ScheduledRequestsPerHourBudget));
    }

    [Fact]
    public void TwentyFiveAlignedFifteenMinuteWatchersFitBudget()
    {
        var document = CreateMinuteWatchers(25, 15);

        var result = ApiRequestBudgetCalculator.Calculate(document, Now, TimeZoneInfo.Utc);

        Assert.Equal(100, result.PeakRequests);
        Assert.True(result.IsWithinBudget(ApiRequestBudgetCalculator.ScheduledRequestsPerHourBudget));
    }

    [Fact]
    public void StaggeredHourlySchedulesCanShareOneRollingHour()
    {
        var document = new WatcherConfigurationDocument();
        for (var index = 0; index < 30; index++)
        {
            document.Repositories.Add(new RepositoryWatchConfiguration
            {
                Id = index.ToString(),
                Download = new DownloadWatcherConfiguration
                {
                    Enabled = true,
                    Schedule = new WatchSchedule
                    {
                        Unit = ScheduleUnit.Minutes,
                        Amount = 60,
                        LastScheduledUtc = Now.AddMinutes(index * 2)
                    }
                }
            });
        }

        var result = ApiRequestBudgetCalculator.Calculate(document, Now, TimeZoneInfo.Utc);

        Assert.Equal(30, result.PeakRequests);
    }

    [Fact]
    public void DailyWatchersAtSameRunTimeAreCountedTogether()
    {
        var document = new WatcherConfigurationDocument();
        for (var index = 0; index < 49; index++)
        {
            document.Repositories.Add(new RepositoryWatchConfiguration
            {
                Id = index.ToString(),
                Release = new ReleaseWatcherConfiguration
                {
                    Enabled = true,
                    CurrentTag = "v1",
                    Schedule = WatchSchedule.Days(1, new TimeSpan(12, 0, 0))
                }
            });
        }

        var result = ApiRequestBudgetCalculator.Calculate(document, Now, TimeZoneInfo.Utc);

        Assert.Equal(49, result.PeakRequests);
    }

    private static WatcherConfigurationDocument CreateMinuteWatchers(int count, int minutes)
    {
        var document = new WatcherConfigurationDocument();
        for (var index = 0; index < count; index++)
        {
            document.Repositories.Add(new RepositoryWatchConfiguration
            {
                Id = index.ToString(),
                Download = new DownloadWatcherConfiguration
                {
                    Enabled = true,
                    Schedule = WatchSchedule.Minutes(minutes)
                }
            });
        }
        return document;
    }
}