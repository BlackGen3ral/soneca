using System.Windows;
using System.Threading;

namespace Soneca;
public partial class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? activateWindow;
    private RegisteredWaitHandle? activation;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 2 && e.Args[0] == "--clean-prefetch") { Environment.Exit(Cleaner.PrefetchHelper(e.Args[1])); return; }
        if (e.Args.Contains("--uninstall-cleanup"))
        {
            StartupRegistration.RemoveAll();
            try
            {
                PcRoutineSchedule.Remove();
                if (PcStore.Read().Mobile is not null) PcStore.Update(state => state.Mobile = null);
            }
            finally { Shutdown(); }
            return;
        }
        if (e.Args.Contains("--self-test")) { Environment.Exit(SelfTests.Run()); return; }
        if (e.Args is [WindowsSuspend.Argument])
        {
            Environment.Exit(WindowsSuspend.Run() ? 0 : 1);
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == "--run-pc-routines")
        {
            try
            {
                DateTime local = DateTime.Now;
                Task.Run(() => PcDeviceService.RunAsync(local)).GetAwaiter().GetResult();
                Environment.Exit(0);
            }
            catch { Environment.Exit(1); }
            return;
        }
        if (e.Args.Contains("--cancel-schedule"))
        {
            try { new Schedule().Cancel(); Schedule.ForSuspend().Cancel(); Environment.Exit(0); }
            catch { Environment.Exit(1); }
            return;
        }
        bool demo = e.Args.Contains("--demo");
        string name = demo ? "Local\\SonecaDemo" : "Local\\Soneca";
        activateWindow = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-Activate");
        instance = new Mutex(true, name, out bool first);
        if (!first) { activateWindow.Set(); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) => { MessageBox.Show(args.Exception.Message, "Artho AI"); args.Handled = true; };
        var window = new MainWindow(demo, e.Args.Contains("--skip-intro"));
        MainWindow = window;
        activation = ThreadPool.RegisterWaitForSingleObject(activateWindow, (_, _) =>
            Dispatcher.InvokeAsync(() => window.RestoreFromTray()), null, Timeout.Infinite, executeOnlyOnce: false);
        SessionEnding += (_, _) => window.AllowSessionEnd();
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        activation?.Unregister(null);
        activateWindow?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }
}
