using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Soneca;

public partial class MainWindow
{
    private readonly RemoteDesktopSession remoteDesktop = new(new WindowsDesktopSurface());
    private readonly DispatcherTimer remoteDesktopTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private Window? desktopNotice;

    private void RemoteDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (demo || mobileSession?.HasLink != true) return;
        if (remoteDesktop.Status().Expires is not null)
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
        var (expires, viewing) = remoteDesktop.Status();
        bool linked = !demo && !mobileBusy && mobileSession is not null && mobileRemembered;
        RemoteDesktopButton.IsEnabled = linked;
        RemoteDesktopButton.Content = expires is not null ? "Encerrar acesso à tela" : "Liberar por 15 minutos";
        RemoteDesktopStatus.Text = viewing ? "Tela sendo compartilhada com o celular pareado."
            : expires is { } time ? $"Acesso liberado até {time.ToLocalTime():HH:mm}. No Android, abra Tela remota."
            : linked ? "Acesso à tela desativado." : "Pareie o celular para liberar o acesso à tela.";
        if (viewing && desktopNotice is null && IsLoaded) ShowDesktopNotice();
        if (!viewing) HideDesktopNotice();
        if (expires is null) remoteDesktopTimer.Stop();
    }

    private void EndRemoteDesktop()
    {
        remoteDesktop.Disable();
        remoteDesktopTimer.Stop();
        HideDesktopNotice();
        if (IsLoaded) RefreshRemoteDesktop();
    }

    private void ShowDesktopNotice()
    {
        var notice = new Window
        {
            Title = "Artho AI · Tela remota",
            Width = 342,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Top = SystemParameters.WorkArea.Top + 12,
            Left = SystemParameters.WorkArea.Right - 354,
            WindowStyle = WindowStyle.ToolWindow,
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("Canvas"),
            Topmost = true,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        var stop = new Button { Content = "Encerrar", HorizontalAlignment = HorizontalAlignment.Right };
        stop.Click += (_, _) => EndRemoteDesktop();
        var content = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(stop, Dock.Right);
        content.Children.Add(stop);
        content.Children.Add(new TextBlock
        {
            Text = "Compartilhando a tela", VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("InfoValue")
        });
        notice.Content = content;
        notice.Closed += (_, _) =>
        {
            if (desktopNotice != notice) return;
            desktopNotice = null;
            EndRemoteDesktop();
        };
        desktopNotice = notice;
        notice.Show();
    }

    private void HideDesktopNotice()
    {
        var notice = desktopNotice;
        desktopNotice = null;
        notice?.Close();
    }
}
