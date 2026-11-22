using System.Windows;
using System.Windows.Threading;

namespace Soneca;

public partial class MainWindow
{
    private readonly RemoteDesktopSession remoteDesktop = new(new WindowsDesktopSurface());
    private readonly DispatcherTimer remoteDesktopTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };

    private void RemoteDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (demo || mobileSession?.HasLink != true) return;
        if (remoteDesktop.Status().Enabled)
        {
            EndRemoteDesktop();
            return;
        }
        if (!remoteDesktop.Enable())
        {
            RemoteDesktopStatus.Text = "Desbloqueie a sessão do Windows antes de liberar.";
            return;
        }
        remoteDesktopTimer.Start();
        RefreshRemoteDesktop();
    }

    private void RefreshRemoteDesktop()
    {
        var (enabled, viewing) = remoteDesktop.Status();
        bool linked = !demo && !mobileBusy && mobileSession is not null && mobileRemembered;
        RemoteDesktopButton.IsEnabled = linked;
        RemoteDesktopButton.Content = enabled ? "Encerrar acesso à tela" : "Liberar acesso à tela";
        RemoteDesktopStatus.Text = viewing ? "Tela sendo compartilhada com o celular pareado."
            : enabled ? "Acesso liberado. No Android, abra Tela remota."
            : linked ? "Acesso à tela desativado." : "Pareie o celular para liberar o acesso à tela.";
        if (!enabled) remoteDesktopTimer.Stop();
    }

    private void EndRemoteDesktop()
    {
        remoteDesktop.Disable();
        remoteDesktopTimer.Stop();
        if (IsLoaded) RefreshRemoteDesktop();
    }
}
