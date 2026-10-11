using System.Globalization;
using GithubDownloadNotifier.Models;

namespace GithubDownloadNotifier.Services;

public sealed record WatcherLogEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public string Repository { get; init; } = string.Empty;
    public string WatcherType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public string DownloadScope { get; init; } = string.Empty;
    public int? PreviousDownloadTotal { get; init; }
    public int? CurrentDownloadTotal { get; init; }
    public int? DownloadDelta { get; init; }
    public string BaselineTag { get; init; } = string.Empty;
    public string DetectedTag { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public DateTimeOffset? ReleasePublishedAt { get; init; }
    public bool? ReleaseIsPrerelease { get; init; }
    public string ReleaseUrl { get; init; } = string.Empty;
}

public static class WatcherLogFormatter
{
    public static string FormatHeader(IReadOnlyList<LogField> fields)
        => string.Join(',', fields.Select(field => Escape(GetHeader(field))));

    public static string FormatEntry(IReadOnlyList<LogField> fields, WatcherLogEntry entry)
        => string.Join(',', fields.Select(field => Escape(GetValue(field, entry))));

    private static string GetHeader(LogField field) => field switch
    {
        LogField.Timestamp => "Timestamp",
        LogField.Repository => "Repository",
        LogField.WatcherType => "WatcherType",
        LogField.Status => "Status",
        LogField.Error => "Error",
        LogField.DownloadScope => "DownloadScope",
        LogField.PreviousDownloadTotal => "PreviousDownloadTotal",
        LogField.CurrentDownloadTotal => "CurrentDownloadTotal",
        LogField.DownloadDelta => "DownloadDelta",
        LogField.BaselineTag => "BaselineTag",
        LogField.DetectedTag => "DetectedTag",
        LogField.ReleaseName => "ReleaseName",
        LogField.ReleasePublishedAt => "ReleasePublishedAt",
        LogField.ReleaseIsPrerelease => "ReleaseIsPrerelease",
        LogField.ReleaseUrl => "ReleaseUrl",
        _ => field.ToString()
    };

    private static string GetValue(LogField field, WatcherLogEntry entry) => field switch
    {
        LogField.Timestamp => entry.Timestamp.ToString("O", CultureInfo.InvariantCulture),
        LogField.Repository => entry.Repository,
        LogField.WatcherType => entry.WatcherType,
        LogField.Status => entry.Status,
        LogField.Error => entry.Error,
        LogField.DownloadScope => entry.DownloadScope,
        LogField.PreviousDownloadTotal => FormatNumber(entry.PreviousDownloadTotal),
        LogField.CurrentDownloadTotal => FormatNumber(entry.CurrentDownloadTotal),
        LogField.DownloadDelta => FormatNumber(entry.DownloadDelta),
        LogField.BaselineTag => entry.BaselineTag,
        LogField.DetectedTag => entry.DetectedTag,
        LogField.ReleaseName => entry.ReleaseName,
        LogField.ReleasePublishedAt => entry.ReleasePublishedAt?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        LogField.ReleaseIsPrerelease => entry.ReleaseIsPrerelease?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        LogField.ReleaseUrl => entry.ReleaseUrl,
        _ => string.Empty
    };

    private static string FormatNumber(int? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
