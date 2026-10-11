using GithubDownloadNotifier.Models;
using GithubDownloadNotifier.Services;

namespace GithubDownloadNotifier.Tests;

public sealed class WatcherLogFormatterTests
{
    [Fact]
    public void FormatsSelectedFieldsInConfiguredOrder()
    {
        LogField[] fields = [LogField.Repository, LogField.CurrentDownloadTotal, LogField.DownloadDelta];
        var entry = new WatcherLogEntry
        {
            Repository = "owner/repo",
            CurrentDownloadTotal = 42,
            DownloadDelta = 3
        };

        Assert.Equal("Repository,CurrentDownloadTotal,DownloadDelta", WatcherLogFormatter.FormatHeader(fields));
        Assert.Equal("owner/repo,42,3", WatcherLogFormatter.FormatEntry(fields, entry));
    }

    [Fact]
    public void EscapesCommasQuotesAndLineBreaks()
    {
        LogField[] fields = [LogField.ReleaseName, LogField.Error];
        var entry = new WatcherLogEntry
        {
            ReleaseName = "Version 1, stable",
            Error = "API said \"no\"\nTry later"
        };

        Assert.Equal("\"Version 1, stable\",\"API said \"\"no\"\"\nTry later\"", WatcherLogFormatter.FormatEntry(fields, entry));
    }
}