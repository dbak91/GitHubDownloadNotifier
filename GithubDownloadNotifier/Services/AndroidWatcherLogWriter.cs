using System.Text;
using Android.Content;
using Android.Net;
using GithubDownloadNotifier.Models;

namespace GithubDownloadNotifier.Services;

public sealed class AndroidWatcherLogWriter(Context context)
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private readonly Context _context = context.ApplicationContext;

    public async Task AppendAsync(WatcherLogConfiguration configuration, WatcherLogEntry entry)
    {
        if (!configuration.Enabled || string.IsNullOrWhiteSpace(configuration.DirectoryUri) || configuration.Fields.Count == 0)
        {
            return;
        }

        var uri = Android.Net.Uri.Parse(configuration.DirectoryUri)
            ?? throw new InvalidOperationException("The selected log file is unavailable.");
        await WriteLock.WaitAsync();
        try
        {
            var descriptor = _context.ContentResolver?.OpenAssetFileDescriptor(uri, "wa")
                ?? throw new IOException("The selected log file cannot be opened.");
            using (descriptor)
            await using (var stream = descriptor.CreateOutputStream())
            {
                if (stream is null)
                {
                    throw new IOException("The selected log file cannot be written.");
                }

                var includeHeader = descriptor.Length == 0;
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                if (includeHeader)
                {
                    await writer.WriteLineAsync(WatcherLogFormatter.FormatHeader(configuration.Fields));
                }
                await writer.WriteLineAsync(WatcherLogFormatter.FormatEntry(configuration.Fields, entry));
            }
        }
        finally
        {
            WriteLock.Release();
        }
    }
}
