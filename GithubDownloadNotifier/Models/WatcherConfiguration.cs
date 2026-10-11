namespace GithubDownloadNotifier.Models;

public sealed class WatcherConfigurationDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public List<RepositoryWatchConfiguration> Repositories { get; set; } = [];
}

public sealed class RepositoryWatchConfiguration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Owner { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DownloadWatcherConfiguration Download { get; set; } = new();

    public ReleaseWatcherConfiguration Release { get; set; } = new();
}

public sealed class DownloadWatcherConfiguration
{
    public bool Enabled { get; set; }

    public DownloadScope Scope { get; set; } = DownloadScope.AllReleases;

    public NotificationMode NotificationMode { get; set; } = NotificationMode.ChangesOnly;

    public WatchSchedule Schedule { get; set; } = WatchSchedule.Minutes(60);

    public int? LastDownloadTotal { get; set; }

    public WatcherRunStatus LastRun { get; set; } = new();

    public WatcherLogConfiguration Logging { get; set; } = new();
}

public sealed class ReleaseWatcherConfiguration
{
    public bool Enabled { get; set; }

    public string CurrentTag { get; set; } = string.Empty;

    public string? PendingTag { get; set; }

    public WatchSchedule Schedule { get; set; } = WatchSchedule.Days(1, DateTime.Now.TimeOfDay);

    public WatcherRunStatus LastRun { get; set; } = new();

    public WatcherLogConfiguration Logging { get; set; } = new();
}

public sealed class WatchSchedule
{
    public ScheduleUnit Unit { get; set; } = ScheduleUnit.Minutes;

    public int Amount { get; set; } = 60;

    public int RunAtHour { get; set; }

    public int RunAtMinute { get; set; }

    public int AnchorDayOfMonth { get; set; } = DateTime.Now.Day;

    public DateTimeOffset? LastScheduledUtc { get; set; }

    public static WatchSchedule Minutes(int amount) => new()
    {
        Unit = ScheduleUnit.Minutes,
        Amount = amount
    };

    public static WatchSchedule Days(int amount, TimeSpan runAt) => new()
    {
        Unit = ScheduleUnit.Days,
        Amount = amount,
        RunAtHour = runAt.Hours,
        RunAtMinute = runAt.Minutes
    };

    public static WatchSchedule Months(int amount, TimeSpan runAt, int anchorDayOfMonth) => new()
    {
        Unit = ScheduleUnit.Months,
        Amount = amount,
        RunAtHour = runAt.Hours,
        RunAtMinute = runAt.Minutes,
        AnchorDayOfMonth = anchorDayOfMonth
    };
}

public sealed class WatcherLogConfiguration
{
    public bool Enabled { get; set; }

    public string DirectoryUri { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public List<LogField> Fields { get; set; } = [];
}

public sealed class WatcherRunStatus
{
    public DateTimeOffset? LastAttemptUtc { get; set; }

    public bool? Succeeded { get; set; }

    public string Message { get; set; } = string.Empty;
}

public enum DownloadScope
{
    LatestRelease,
    AllReleases
}

public enum NotificationMode
{
    ChangesOnly,
    EveryCheck
}

public enum ScheduleUnit
{
    Minutes,
    Days,
    Months
}

public enum LogField
{
    Timestamp,
    Repository,
    WatcherType,
    Status,
    Error,
    DownloadScope,
    PreviousDownloadTotal,
    CurrentDownloadTotal,
    DownloadDelta,
    BaselineTag,
    DetectedTag,
    ReleaseName,
    ReleasePublishedAt,
    ReleaseIsPrerelease,
    ReleaseUrl
}
