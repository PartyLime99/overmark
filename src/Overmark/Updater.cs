using System.Windows.Threading;
using Overmark.Settings;
using Overmark.Tray;
using Velopack;
using Velopack.Sources;

namespace Overmark;

/// <summary>
/// Checks GitHub Releases for a newer Overmark now and then, and downloads it quietly. Velopack installs it the
/// next time Overmark starts. Only an installed copy updates; a development build does nothing.
/// </summary>
internal sealed class Updater
{
    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private readonly Func<AppSettings> _settings;
    private readonly TrayIcon _tray;
    private readonly DispatcherTimer _timer;
    private string? _downloaded;

    public Updater(Func<AppSettings> settings, TrayIcon tray, Dispatcher dispatcher)
    {
        _settings = settings;
        _tray = tray;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = FirstCheck };
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = CheckEvery;
            await CheckAsync();
        };
        _timer.Start();
    }

    private async Task CheckAsync()
    {
        if (!_settings().CheckForUpdates || _downloaded is not null)
        {
            return;
        }

        try
        {
            var manager = new UpdateManager(new GithubSource(AppInfo.RepositoryUrl, null, false));
            if (!manager.IsInstalled)
            {
                return;
            }

            var update = await manager.CheckForUpdatesAsync();
            if (update is null)
            {
                return;
            }

            await manager.DownloadUpdatesAsync(update);
            _downloaded = update.TargetFullRelease.Version.ToString();
            Log.Info($"Downloaded Overmark {_downloaded}; it installs next time Overmark starts");
            _tray.Notify("Overmark update ready", $"Version {_downloaded} installs next time Overmark starts.");
        }
        catch (Exception ex)
        {
            // Offline, GitHub unreachable, or no releases yet: try again later.
            Log.Warn($"Update check failed: {ex.Message}");
        }
    }
}
