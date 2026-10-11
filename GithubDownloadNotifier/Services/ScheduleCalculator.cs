using GithubDownloadNotifier.Models;

namespace GithubDownloadNotifier.Services;

public static class ScheduleCalculator
{
    public static DateTimeOffset GetNextRun(DateTimeOffset nowUtc, WatchSchedule schedule, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(timeZone);

        if (schedule.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schedule), "Schedule amount must be positive.");
        }

        if (schedule.Unit == ScheduleUnit.Minutes)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(15, schedule.Amount));
            var next = schedule.LastScheduledUtc?.Add(interval) ?? nowUtc.Add(interval);
            while (next <= nowUtc)
            {
                next = next.Add(interval);
            }
            return next;
        }

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, timeZone);
        var localBase = schedule.LastScheduledUtc.HasValue
            ? TimeZoneInfo.ConvertTime(schedule.LastScheduledUtc.Value, timeZone)
            : localNow;

        DateTime nextLocal;
        do
        {
            nextLocal = schedule.Unit switch
            {
                ScheduleUnit.Days => NextDaily(localBase.DateTime, schedule),
                ScheduleUnit.Months => NextMonthly(localBase.DateTime, schedule),
                _ => throw new ArgumentOutOfRangeException(nameof(schedule), "Unsupported schedule unit.")
            };

            localBase = new DateTimeOffset(nextLocal, timeZone.GetUtcOffset(nextLocal));
        }
        while (localBase <= localNow);

        return TimeZoneInfo.ConvertTime(localBase, TimeZoneInfo.Utc);
    }

    private static DateTime NextDaily(DateTime localBase, WatchSchedule schedule)
    {
        var date = localBase.Date.AddDays(schedule.Amount);
        return date.AddHours(ClampHour(schedule.RunAtHour)).AddMinutes(ClampMinute(schedule.RunAtMinute));
    }

    private static DateTime NextMonthly(DateTime localBase, WatchSchedule schedule)
    {
        var targetMonth = new DateTime(localBase.Year, localBase.Month, 1).AddMonths(schedule.Amount);
        var day = Math.Clamp(schedule.AnchorDayOfMonth, 1, DateTime.DaysInMonth(targetMonth.Year, targetMonth.Month));
        return new DateTime(targetMonth.Year, targetMonth.Month, day, ClampHour(schedule.RunAtHour), ClampMinute(schedule.RunAtMinute), 0);
    }

    private static int ClampHour(int hour) => Math.Clamp(hour, 0, 23);

    private static int ClampMinute(int minute) => Math.Clamp(minute, 0, 59);
}
