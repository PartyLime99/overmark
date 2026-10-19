using System.Windows;
using System.Windows.Threading;
using Overmark.Settings;

namespace Overmark;

public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private OvermarkHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception on a background thread", args.ExceptionObject as Exception);

        // Resolve a relative file path here, where it was typed, before it's passed on.
        string[] args = CommandLine.EditPath(e.Args) is { } editPath ? ["--edit", editPath] : [];

        // Started at sign-in: Overmark waits quietly in the notification area. Started any other way (its shortcut,
        // or "Open with"), it brings up the editor.
        var quiet = e.Args.Contains(Platform.StartWithWindows.StartupArgument, StringComparer.OrdinalIgnoreCase);

        _singleInstance = SingleInstance.TryClaim();
        if (_singleInstance is null)
        {
            if (!quiet)
            {
                SingleInstance.SendToRunningInstance(args);
            }

            Shutdown();
            return;
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        Log.Info($"Overmark {AppInfo.Version} starting");
        var host = new OvermarkHost(new SettingsStore(AppInfo.SettingsFile), Dispatcher, Shutdown, quiet);
        _host = host;
        _singleInstance.CommandReceived += received => Dispatcher.InvokeAsync(() => host.HandleCommand(received));
        if (!quiet)
        {
            Dispatcher.InvokeAsync(() => host.HandleCommand(args), DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        _singleInstance?.Dispose();
        if (_host is not null) Log.Info("Overmark stopped");
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled exception", e.Exception);
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nDetails are in the log:\n{Log.FilePath}",
            "Overmark", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
