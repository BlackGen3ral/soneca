using System.Windows;
using System.Threading;

namespace Soneca;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--clean-prefetch") { Environment.Exit(Cleaner.PrefetchHelper(e.Args[1])); return; }
        if (e.Args.Contains("--uninstall-cleanup")) { StartupRegistration.RemoveAll(); Shutdown(); return; }
        if (e.Args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }
        if (e.Args.Contains("--cancel-schedule"))
        {
            try { new Schedule().Cancel(); Environment.Exit(0); }
            catch { Environment.Exit(1); }
            return;
        }
        bool demo = e.Args.Contains("--demo");
        instance = new Mutex(true, demo ? "Local\\SonecaDemo" : "Local\\Soneca", out bool first);
        if (!first) { MessageBox.Show("O Artho AI já está aberto.", "Artho AI"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "Artho AI"); args.Handled = true; };
        MainWindow = new MainWindow(demo, e.Args.Contains("--skip-intro")); MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
