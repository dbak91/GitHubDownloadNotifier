using Android.Content;
using GithubDownloadNotifier.Services;

namespace GithubDownloadNotifier;

[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class BootCompletedReceiver : BroadcastReceiver
{
    private const string PreferencesName = "boot_persistence";
    private const string EnabledKey = "enabled";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent?.Action != Intent.ActionBootCompleted)
        {
            return;
        }

        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        var storageDirectory = context.FilesDir?.AbsolutePath;
        if (preferences?.GetBoolean(EnabledKey, false) != true || string.IsNullOrWhiteSpace(storageDirectory))
        {
            return;
        }

        var document = new WatcherStore(storageDirectory).Load();
        new DownloadWorkScheduler(context).Synchronize(document);
        new ReleaseWorkScheduler(context).Synchronize(document);
    }
}
