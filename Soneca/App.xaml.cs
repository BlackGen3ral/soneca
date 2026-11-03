using System.Windows;
using System.Threading;

namespace Soneca;
public partial class App : Application
{
    private Mutex? instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }
        bool demo = e.Args.Contains("--demo");
        instance = new Mutex(true, demo ? "Local\\SonecaDemo" : "Local\\Soneca", out bool first);
        if (!first) { MessageBox.Show("O Soneca já está aberto.", "Soneca"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "Soneca"); args.Handled = true; };
        MainWindow = new MainWindow(demo); MainWindow.Show();
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
