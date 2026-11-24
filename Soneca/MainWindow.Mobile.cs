using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;

namespace Soneca;

public partial class MainWindow
{
    private readonly DispatcherTimer mobileTimer = new();
    private readonly DispatcherTimer mobileReconnectTimer = new() { Interval = TimeSpan.FromSeconds(12) };
    private MobilePairingServer? mobileSession;
    private bool mobileBusy;
    private bool mobileRemembered;

    private void Mobile_Click(object sender, RoutedEventArgs e)
    {
        Navigate(4);
        RefreshMobileAddresses();
    }

    private void RefreshMobileAddresses()
    {
        if (mobileSession is null && !mobileBusy && !mobileRemembered)
        {
            var previous = MobileNetwork.SelectedItem as IPAddress;
            var addresses = MobilePairingServer.Addresses();
            MobileNetwork.ItemsSource = addresses;
            MobileNetwork.SelectedItem = previous is not null && addresses.Contains(previous) ? previous : addresses.FirstOrDefault();
            if (addresses.Length == 0) MobileStatus.Text = "Conecte este PC à rede local.";
        }
        UpdateMobileButtons();
    }

    private async Task RestoreMobileAsync()
    {
        if (mobileBusy || mobileSession is not null) return;
        mobileBusy = true;
        UpdateMobileButtons();
        try
        {
            var profile = await Task.Run(() => PcStore.Read().Mobile);
            mobileRemembered = profile is not null;
            if (profile is null) return;
            mobileReconnectTimer.Stop();
            MobileStatus.Text = "Reabrindo o pareamento salvo…";
            MobileAllowDevices.IsChecked = profile.AllowDevices;
            MobileAllowPower.IsChecked = profile.AllowPower;
            if (!IPAddress.TryParse(profile.Address, out var address)) throw new IOException("Endereço salvo inválido.");
            MobileNetwork.ItemsSource = new[] { address };
            MobileNetwork.SelectedItem = address;
            await StartMobileAsync(address, profile);
            if (mobileSession is not null)
                MobileStatus.Text = "Pareamento salvo. O celular pode se conectar na rede local.";
        }
        catch (Exception)
        {
            MobileStatus.Text = "O pareamento salvo está indisponível. Confira a rede do PC e toque em Reconectar. Se o endereço mudou, gere outro QR Code.";
            if (mobileRemembered && IsLoaded) mobileReconnectTimer.Start();
        }
        finally
        {
            mobileBusy = false;
            if (MobilePanel.IsVisible) RefreshMobileAddresses();
            else UpdateMobileButtons();
        }
    }

    private async void MobileStart_Click(object sender, RoutedEventArgs e)
    {
        if (mobileBusy || mobileSession is not null) return;
        if (mobileRemembered) { await RestoreMobileAsync(); return; }
        if (MobileNetwork.SelectedItem is not IPAddress address) return;
        mobileBusy = true;
        UpdateMobileButtons();
        try
        {
            if (demo)
            {
                ShowQr("Artho AI · Demonstração");
                MobileStatus.Text = "QR Code de demonstração.";
                return;
            }
            MobileStatus.Text = "Preparando o pareamento…";
            await StartMobileAsync(address, null);
            if (mobileSession is not { Payload: { } invitation } session) return;
            ShowQr(invitation);
            MobileStatus.Text = "Escaneie o QR Code no Artho AI para Android. Ele vale por 3 minutos e só pode ser usado uma vez.";
            mobileTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, (session.Expires - DateTimeOffset.UtcNow).TotalSeconds));
            mobileTimer.Tick += MobileExpired;
            mobileTimer.Start();
        }
        catch (Exception)
        {
            await StopMobileAsync();
            MobileStatus.Text = "Não foi possível abrir a conexão. Confira a rede local e feche a ponte antiga do Artho AI, se estiver aberta.";
        }
        finally
        {
            mobileBusy = false;
            UpdateMobileButtons();
        }
    }

    private async Task StartMobileAsync(IPAddress address, MobileProfile? profile)
    {
        bool allowDevices = MobileAllowDevices.IsChecked == true;
        bool allowPower = MobileAllowPower.IsChecked == true;
        if (allowRemoteDesktop) remoteDesktop.Enable();
        MobilePairingServer session;
        try
        {
            session = await Task.Run(() => MobilePairingServer.StartAsync(address,
                () => Dispatcher.BeginInvoke(new Action(MobilePaired)),
                allowDevices, allowPower, () => Dispatcher.BeginInvoke(new Action(RefreshSchedule)),
                restored: profile,
                revoked: () => Dispatcher.BeginInvoke(new Action(MobileRevoked)),
                remoteDesktop: remoteDesktop));
        }
        catch
        {
            remoteDesktop.Disable();
            throw;
        }
        if (!IsLoaded) { await session.DisposeAsync(); return; }
        mobileSession = session;
        ApplyRemoteDesktopPermission();
    }

    private void MobilePaired()
    {
        if (mobileSession is null) return;
        mobileRemembered = true;
        mobileTimer.Stop();
        mobileTimer.Tick -= MobileExpired;
        HideQr();
        MobileStatus.Text = "Celular pareado. A conexão será retomada quando o Artho AI voltar a abrir nesta rede.";
        ApplyRemoteDesktopPermission();
        RestoreInternetPower();
        UpdateMobileButtons();
    }

    private async void MobileRevoked()
    {
        mobileRemembered = false;
        RestoreInternetPower();
        try { await StopMobileAsync(); }
        finally
        {
            RefreshMobileAddresses();
            MobileStatus.Text = "Celular desvinculado.";
        }
    }

    private void RefreshSchedule()
    {
        try { target = schedule.Read(); suspendTarget = suspendSchedule.Read(); UpdateEnergyClock(); }
        catch (Exception ex) { ErrorLabel.Text = ex.Message; }
    }

    private void ShowQr(string payload)
    {
        MobileQr.Source = BuildQrImage(payload);
        MobileQr.Visibility = Visibility.Visible;
    }

    private static BitmapImage BuildQrImage(string payload)
    {
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var stream = new MemoryStream(new PngByteQRCode(data).GetGraphic(8));
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        return bitmap;
    }

    private void HideQr()
    {
        MobileQr.Source = null;
        MobileQr.Visibility = Visibility.Collapsed;
    }

    private async void MobileExpired(object? sender, EventArgs e)
    {
        if (mobileSession?.HasLink == true) { MobilePaired(); return; }
        await StopMobileAsync();
        MobileStatus.Text = "O QR Code expirou. Gere outro para conectar.";
    }

    private async void MobileStop_Click(object sender, RoutedEventArgs e)
    {
        if (mobileBusy) return;
        mobileBusy = true;
        UpdateMobileButtons();
        try
        {
            if (mobileSession is { } active) await Task.Run(active.Forget);
            else if (mobileRemembered && !demo) await Task.Run(() => PcStore.Update(state => state.Mobile = null));
            mobileRemembered = false;
            RestoreInternetPower();
            await StopMobileAsync();
            MobileStatus.Text = "Celular desvinculado.";
        }
        catch (Exception) { MobileStatus.Text = "Não foi possível desvincular o celular. Tente novamente."; }
        finally
        {
            mobileBusy = false;
            RefreshMobileAddresses();
        }
    }

    private async Task StopMobileAsync()
    {
        mobileTimer.Stop();
        mobileReconnectTimer.Stop();
        mobileTimer.Tick -= MobileExpired;
        HideQr();
        var session = mobileSession;
        mobileSession = null;
        EndRemoteDesktop();
        if (session is not null) await session.DisposeAsync();
        UpdateMobileButtons();
    }

    private void UpdateMobileButtons()
    {
        bool busy = mobileBusy || mobileSession is not null || mobileRemembered;
        MobileNetwork.IsEnabled = MobileAllowDevices.IsEnabled = MobileAllowPower.IsEnabled = !busy;
        MobileStart.Content = mobileRemembered ? "Reconectar" : "Gerar QR Code";
        MobileStart.IsEnabled = !mobileBusy && mobileSession is null &&
            MobileQr.Visibility != Visibility.Visible && (mobileRemembered || MobileNetwork.SelectedItem is not null);
        MobileStop.Content = mobileRemembered ? "Desvincular celular" : "Cancelar QR Code";
        MobileStop.Visibility = mobileRemembered || mobileSession is not null || MobileQr.Visibility == Visibility.Visible
            ? Visibility.Visible : Visibility.Collapsed;
        MobileStop.IsEnabled = !mobileBusy;
        RefreshRemoteDesktop();
    }
}
