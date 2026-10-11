using GithubDownloadNotifier.Models;
using GithubDownloadNotifier.Services;

namespace GithubDownloadNotifier.Tests;

public sealed class ScheduleCalculatorTests
{
    [Fact]
    public void MinutesClampToWorkManagerMinimum()
    {
        var now = new DateTimeOffset(2026, 10, 11, 10, 0, 0, TimeSpan.Zero);

        var next = ScheduleCalculator.GetNextRun(now, WatchSchedule.Minutes(5), TimeZoneInfo.Utc);

        Assert.Equal(now.AddMinutes(15), next);
    }

    [Fact]
    public void DaysUseConfiguredLocalTime()
    {
        var now = new DateTimeOffset(2026, 10, 11, 10, 30, 0, TimeSpan.Zero);
        var schedule = WatchSchedule.Days(3, new TimeSpan(8, 15, 0));

        var next = ScheduleCalculator.GetNextRun(now, schedule, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 10, 14, 8, 15, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void DaysContinueFromLastScheduledOccurrence()
    {
        var now = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
        var schedule = WatchSchedule.Days(3, new TimeSpan(8, 15, 0));
        schedule.LastScheduledUtc = new DateTimeOffset(2026, 10, 11, 8, 15, 0, TimeSpan.Zero);

        var next = ScheduleCalculator.GetNextRun(now, schedule, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 10, 17, 8, 15, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void MonthsClampAnchorToLastDay()
    {
        var now = new DateTimeOffset(2026, 1, 31, 9, 0, 0, TimeSpan.Zero);
        var schedule = WatchSchedule.Months(1, new TimeSpan(9, 0, 0), 31);

        var next = ScheduleCalculator.GetNextRun(now, schedule, TimeZoneInfo.Utc);

        Assert.Equal(new DateTimeOffset(2026, 2, 28, 9, 0, 0, TimeSpan.Zero), next);
    }
}
