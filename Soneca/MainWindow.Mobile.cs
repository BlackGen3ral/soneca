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
    private MobilePairingServer? mobileSession;

    private void Mobile_Click(object sender, RoutedEventArgs e)
    {
        Navigate(4);
        if (mobileSession is not null || !MobileNetwork.IsEnabled) return;
        var previous = MobileNetwork.SelectedItem as IPAddress;
        var addresses = MobilePairingServer.Addresses();
        MobileNetwork.ItemsSource = addresses;
        MobileNetwork.SelectedItem = previous is not null && addresses.Contains(previous) ? previous : addresses.FirstOrDefault();
        MobileStart.IsEnabled = addresses.Length > 0;
        if (addresses.Length == 0) MobileStatus.Text = "Conecte este PC à rede local e reabra a aba Celular.";
    }

    private async void MobileStart_Click(object sender, RoutedEventArgs e)
    {
        if (mobileSession is not null || MobileNetwork.SelectedItem is not IPAddress address) return;
        MobileStart.IsEnabled = false;
        MobileNetwork.IsEnabled = false;
        bool allowDevices = MobileAllowDevices.IsChecked == true;
        bool allowPower = MobileAllowPower.IsChecked == true;
        MobileAllowDevices.IsEnabled = false;
        MobileAllowPower.IsEnabled = false;
        if (demo)
        {
            using var sample = QRCodeGenerator.GenerateQrCode("Artho AI · Demonstração", QRCodeGenerator.ECCLevel.M);
            ShowQr(new PngByteQRCode(sample).GetGraphic(8));
            MobileStatus.Text = "Demonstração do QR Code. A conexão é iniciada no aplicativo instalado.";
            return;
        }

        MobileStatus.Text = "Preparando a conexão…";
        try
        {
            var session = await Task.Run(() => MobilePairingServer.StartAsync(address, () =>
                Dispatcher.BeginInvoke(() =>
                {
                    if (mobileSession is null) return;
                    mobileTimer.Stop();
                    HideQr();
                    MobileStatus.Text = "Celular pareado. A conexão local está disponível enquanto o Artho AI estiver aberto.";
                }), allowDevices, allowPower, () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { target = schedule.Read(); Render(); }
                    catch (Exception ex) { ErrorLabel.Text = ex.Message; }
                }))));
            if (!IsLoaded)
            {
                await session.DisposeAsync();
                return;
            }
            mobileSession = session;
            using var data = QRCodeGenerator.GenerateQrCode(session.Payload, QRCodeGenerator.ECCLevel.M);
            ShowQr(new PngByteQRCode(data).GetGraphic(8));
            MobileStatus.Text = "No Android, toque em Conectar ao PC e escaneie este QR Code. Ele expira em 3 minutos.";
            mobileTimer.Interval = session.Expires - DateTimeOffset.UtcNow;
            mobileTimer.Tick += MobileExpired;
            mobileTimer.Start();
        }
        catch (Exception)
        {
            await StopMobileAsync();
            MobileStatus.Text = "Não foi possível abrir a conexão. Confira o endereço e feche a ponte antiga do Artho AI, se estiver aberta.";
        }
    }

    private void ShowQr(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        MobileQr.Source = bitmap;
        MobileQr.Visibility = Visibility.Visible;
        MobileStop.Visibility = Visibility.Visible;
    }

    private void HideQr()
    {
        MobileQr.Source = null;
        MobileQr.Visibility = Visibility.Collapsed;
    }

    private async void MobileExpired(object? sender, EventArgs e)
    {
        await StopMobileAsync();
        MobileStatus.Text = "O QR Code expirou. Gere outro para conectar.";
    }

    private async void MobileStop_Click(object sender, RoutedEventArgs e)
    {
        await StopMobileAsync();
        MobileStatus.Text = "Conexão encerrada.";
    }

    private async Task StopMobileAsync()
    {
        mobileTimer.Stop();
        mobileTimer.Tick -= MobileExpired;
        HideQr();
        MobileStop.Visibility = Visibility.Collapsed;
        var session = mobileSession;
        mobileSession = null;
        try
        {
            if (session is not null) await session.DisposeAsync();
        }
        finally
        {
            MobileNetwork.IsEnabled = true;
            MobileAllowDevices.IsEnabled = true;
            MobileAllowPower.IsEnabled = true;
            MobileStart.IsEnabled = MobileNetwork.SelectedItem is not null;
        }
    }
}
