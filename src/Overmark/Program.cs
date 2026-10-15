using Overmark.Platform;
using Velopack;

namespace Overmark;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        // This must come first. When the installer runs Overmark to install, update or uninstall it, Velopack
        // does that job here and exits; it also installs a downloaded update before the app starts.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => StartWithWindows.Set(false))
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
