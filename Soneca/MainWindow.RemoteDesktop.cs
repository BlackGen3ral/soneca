using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Soneca;

public partial class MainWindow
{
    private readonly RemoteDesktopSession remoteDesktop = new(new WindowsDesktopSurface());
    private readonly DispatcherTimer remoteDesktopTimer = new() { Interval = TimeSpan.FromMilliseconds(750) };
    private bool allowRemoteDesktop;

    private void ConfigureRemoteDesktop()
    {
        allowRemoteDesktop = demo || RemoteDesktopPreference.Read();
        remoteDesktop.ViewerChanged += () => Dispatcher.BeginInvoke(new Action(RefreshRemoteDesktop));
        RefreshRemoteDesktop();
    }

    private void RemoteDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (demo) return;
        bool allowed = !allowRemoteDesktop;
        try
        {
            RemoteDesktopPreference.Save(allowed);
            allowRemoteDesktop = allowed;
            ApplyRemoteDesktopPermission();
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            RemoteDesktopStatus.Text = "Não foi possível salvar a permissão de tela remota.";
        }
    }

    private void ApplyRemoteDesktopPermission()
    {
        if (!demo && allowRemoteDesktop && mobileSession?.HasLink == true)
            remoteDesktop.Enable();
        else
            remoteDesktop.Disable();
        RefreshRemoteDesktop();
    }

    private void RefreshRemoteDesktop()
    {
        var (enabled, viewing) = remoteDesktop.Status();
        RemoteDesktopButton.IsEnabled = !demo;
        RemoteDesktopButton.Content = allowRemoteDesktop ? "Desativar acesso remoto" : "Permitir acesso remoto";
        RemoteDesktopStatus.Text = viewing ? "Tela sendo compartilhada com o celular pareado."
            : enabled && mobileSession?.HasLink == true ? "Acesso liberado para o celular pareado. A captura começa ao abrir Tela remota no Android."
            : allowRemoteDesktop ? "Acesso automático ativado. O celular poderá abrir a tela quando o pareamento estiver disponível."
            : "Acesso remoto desativado neste PC.";
        if (viewing && !remoteDesktopTimer.IsEnabled) remoteDesktopTimer.Start();
        if (!viewing) remoteDesktopTimer.Stop();
    }

    private void EndRemoteDesktop()
    {
        remoteDesktop.Disable();
        remoteDesktopTimer.Stop();
        if (IsLoaded) RefreshRemoteDesktop();
    }
}

internal static class RemoteDesktopPreference
{
    private const string Key = @"Software\Artho\Soneca";
    private const string Name = "RemoteDesktopAllowed";

    internal static bool Resolve(int? saved) => saved is not 0;

    internal static bool Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return Resolve(key?.GetValue(Name) as int?);
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    internal static void Save(bool allowed)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, true);
        key.SetValue(Name, allowed ? 1 : 0, RegistryValueKind.DWord);
    }
}
