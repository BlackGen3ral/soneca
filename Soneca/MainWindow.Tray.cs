using System.ComponentModel;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Soneca;

public partial class MainWindow
{
    private Forms.NotifyIcon? trayIcon;
    private Forms.ContextMenuStrip? trayMenu;
    private System.Drawing.Icon? trayDrawing;
    private WindowState windowBeforeTray = WindowState.Normal;
    private bool exitRequested;

    private void ConfigureTray()
    {
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ArthoAI.ico")).Stream;
        using var icon = new System.Drawing.Icon(stream);
        trayDrawing = (System.Drawing.Icon)icon.Clone();
        trayMenu = new Forms.ContextMenuStrip();
        var open = trayMenu.Items.Add("Abrir Artho AI");
        open.Click += (_, _) => Dispatcher.InvokeAsync(() => RestoreFromTray());
        trayMenu.Items.Add("Assinaturas", null, (_, _) => Dispatcher.InvokeAsync(() => RestoreFromTray(page: 8)));
        trayMenu.Items.Add(new Forms.ToolStripSeparator());
        trayMenu.Items.Add("Sair", null, (_, _) => Dispatcher.InvokeAsync(ExitFromTray));
        trayIcon = new Forms.NotifyIcon { Icon = trayDrawing, ContextMenuStrip = trayMenu, Text = "Artho AI" };
        trayIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) Dispatcher.InvokeAsync(() => RestoreFromTray());
        };
        trayIcon.BalloonTipClicked += (_, _) => Dispatcher.InvokeAsync(() => RestoreFromTray(page: 8));
        trayIcon.Visible = true;
    }

    private void TrayWindowStateChanged()
    {
        if (WindowState != WindowState.Minimized)
        {
            windowBeforeTray = WindowState;
            return;
        }
        if (!exitRequested && trayIcon is not null) MoveToTray();
    }

    private void HandleClose(CancelEventArgs args)
    {
        if (!exitRequested && !launchingInstaller && trayIcon is not null)
        {
            args.Cancel = true;
            MoveToTray();
            return;
        }
        if (launchingInstaller || (!CleanupPanel.Busy && !NamesPanel.Busy && downloadCancellation is null)) return;
        args.Cancel = true;
        exitRequested = false;
        RestoreFromTray();
        MessageBox.Show(this, "Aguarde a operação terminar ou cancele o download antes de fechar.", "Artho AI");
    }

    private void MoveToTray()
    {
        FinishIntro();
        Hide();
    }

    internal void RestoreFromTray(int? page = null)
    {
        if (exitRequested) return;
        if (!IsVisible || WindowState == WindowState.Minimized) WindowState = windowBeforeTray;
        if (!IsVisible) Show();
        if (page.HasValue)
        {
            FinishIntro();
            Navigate(page.Value);
        }
        Activate();
    }

    internal void AllowSessionEnd() => exitRequested = true;

    private void ExitFromTray()
    {
        exitRequested = true;
        Close();
    }

    private void CloseTray()
    {
        trayIcon?.Dispose();
        trayMenu?.Dispose();
        trayDrawing?.Dispose();
    }
}
