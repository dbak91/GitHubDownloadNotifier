# GitHub Download Notifier

GitHub Download Notifier is a .NET 10 Android app that monitors GitHub release-asset download counts and sends Android notifications.

## Behavior

1. Enter the GitHub repository owner and repository name.
2. Select how frequently GitHub should be checked.
3. Choose whether counts include all releases or only the latest release.
4. Choose whether every successful check produces a notification or notifications are only sent when downloads increase.
5. Enable background monitoring.

The first successful check establishes a comparison baseline. Later checks compare the current total with the stored total for the selected repository and release scope.

## Check intervals

Available intervals are:

- 5 minutes
- 30 minutes
- 1 hour
- 12 hours
- 1 day
- Custom interval in minutes

## Release scope

- **All Releases** sums the `download_count` values of release assets across up to the latest 100 GitHub releases.
- **Latest Only** sums release-asset downloads from the latest release.

Separate comparison baselines are maintained for each scope, so changing scope does not compare unrelated totals.

## Notification modes

- **Only show when downloads increase** sends a notification only when the current total is greater than the previous total.
- **Always show each check** reports every successful scheduled check, including no-change and first-check results.

The foreground service displays a low-priority persistent notification while monitoring is enabled. Failed checks do not produce download-update notifications.

## Test Now

**Test now** immediately queries GitHub and sends a notification containing:

- The current download total for the selected release scope.
- The difference from the previous stored total.

The test also updates the stored comparison baseline.

## Android permissions

The app uses:

- Internet access for GitHub API requests.
- Foreground-service permission for scheduled monitoring.
- Notification permission on Android 13 and newer.

Notification permission is requested when background monitoring is first enabled or when **Test now** is used, unless permission was already granted.

## Service lifetime

Monitoring uses an Android foreground service. It does not currently register a boot receiver, so monitoring is not automatically restarted after the device reboots.

## GitHub API considerations

The app currently uses unauthenticated GitHub API requests. GitHub rate limits therefore apply. Very large repositories with more than 100 releases are limited to the first API page when **All Releases** is selected.

## Build and publish

Build the project with the .NET 10 Android workload installed:

```powershell
dotnet build GithubDownloadNotifier.slnx
```

Publish APK files:

```powershell
dotnet publish GithubDownloadNotifier\GithubDownloadNotifier.csproj -c Release -f net10.0-android /p:AndroidPackageFormat=apk
```

Published APK files are written under:

`GithubDownloadNotifier\bin\Release\net10.0-android\publish`
