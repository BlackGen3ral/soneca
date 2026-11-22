using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace Soneca;

public partial class MainWindow
{
    private readonly AndroidControl androidControl = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArthoAI", "AndroidControl"));
    private CancellationTokenSource? androidOperation;

    private async void AndroidUsb_Click(object sender, RoutedEventArgs e) => await OpenAndroidAsync(null);

    private async void AndroidQr_Click(object sender, RoutedEventArgs e)
    {
        await UseAndroidAsync("Preparando o QR Code…", async token =>
        {
            var invite = AndroidPairingQr.Create();
            AndroidQrImage.Source = BuildQrImage(invite.Payload);
            AndroidQrPanel.Visibility = Visibility.Visible;
            AndroidQrPanel.BringIntoView();
            AndroidStatus.Text = "Aguardando a leitura do QR Code pelo Android…";
            string device;
            try
            {
                device = await androidControl.PairQrAsync(invite, () =>
                {
                    HideAndroidQr();
                    AndroidStatus.Text = "QR Code lido. Concluindo o pareamento…";
                }, token);
            }
            finally { HideAndroidQr(); }
            androidControl.Remember(device);
            await MirrorPairedAndroidAsync(device, token);
        });
    }

    private async void AndroidRemembered_Click(object sender, RoutedEventArgs e)
    {
        await UseAndroidAsync("Localizando o Android pareado…", async token =>
        {
            var device = androidControl.RememberedDevice();
            while (device is null)
            {
                AndroidStatus.Text = "Aguardando um Android pareado na rede. Ative a Depuração por Wi-Fi no celular.";
                try { device = await androidControl.FindExistingDeviceAsync(null, token); }
                catch (AndroidConnectionException) { }
                if (device is null) await Task.Delay(1800, token);
            }
            androidControl.Remember(device);
            await MirrorPairedAndroidAsync(device, token);
        });
    }

    private void HideAndroidQr()
    {
        AndroidQrImage.Source = null;
        AndroidQrPanel.Visibility = Visibility.Collapsed;
    }

    private async void AndroidWifi_Click(object sender, RoutedEventArgs e)
    {
        try { await OpenAndroidAsync(AndroidControl.LocalEndpoint(AndroidConnectAddress.Text)); }
        catch (ArgumentException ex) { AndroidStatus.Text = ex.Message; }
    }

    private async void AndroidPair_Click(object sender, RoutedEventArgs e)
    {
        string code = AndroidPairCode.Password.Trim();
        try
        {
            if (!AndroidControl.ValidCode(code)) throw new ArgumentException("Digite os seis números exibidos no Android.");
            string endpoint = AndroidControl.LocalEndpoint(AndroidPairAddress.Text);
            AndroidPairCode.Clear();
            await UseAndroidAsync("Pareando com o Android…", async token =>
            {
                var device = await androidControl.PairAsync(endpoint, code, token);
                androidControl.Remember(device);
                AndroidPairAddress.Clear();
                AndroidPairHelp.IsExpanded = false;
                await MirrorPairedAndroidAsync(device, token);
            });
        }
        catch (ArgumentException ex) { AndroidStatus.Text = ex.Message; }
    }

    private async Task OpenAndroidAsync(string? wifiEndpoint)
    {
        await UseAndroidAsync(wifiEndpoint is null ? "Procurando o Android conectado por USB…" : "Conectando ao Android por Wi-Fi…",
            async token =>
            {
                var device = wifiEndpoint is null ? null : await androidControl.FindExistingDeviceAsync(wifiEndpoint, token);
                if (device is not null)
                {
                    androidControl.Remember(device);
                    await MirrorPairedAndroidAsync(device, token);
                }
                else await MirrorAndroidAsync(wifiEndpoint, token);
            });
    }

    private async Task MirrorAndroidAsync(string? endpoint, CancellationToken token)
    {
        await androidControl.MirrorAsync(endpoint, AndroidScreenOpened, token);
        AndroidStatus.Text = "Controle do Android encerrado.";
    }

    private async Task MirrorPairedAndroidAsync(string device, CancellationToken token)
    {
        await androidControl.MirrorWifiAsync(device,
            () => AndroidStatus.Text = "Aguardando o Android na rede. A tela vai abrir quando a Depuração por Wi-Fi voltar a responder.",
            endpoint => AndroidConnectAddress.Text = endpoint,
            AndroidScreenOpened, token);
        AndroidStatus.Text = "Controle do Android encerrado.";
    }

    private void AndroidScreenOpened() =>
        AndroidStatus.Text = "Tela do Android aberta. Use o mouse e o teclado na janela de controle.";

    private async Task UseAndroidAsync(string message, Func<CancellationToken, Task> operation)
    {
        if (demo)
        {
            AndroidStatus.Text = "O controle do Android está disponível quando o Artho AI é aberto normalmente.";
            return;
        }
        if (androidOperation is not null) return;
        using var cancellation = new CancellationTokenSource();
        androidOperation = cancellation;
        AndroidUsb.IsEnabled = AndroidWifi.IsEnabled = AndroidPair.IsEnabled = AndroidPairQr.IsEnabled = AndroidRemembered.IsEnabled = false;
        AndroidStop.IsEnabled = true;
        AndroidStatus.Text = message;
        try
        {
            await androidControl.PrepareAsync(
                () => AndroidStatus.Text = "Baixando o scrcpy oficial para usar no Artho AI…", cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            AndroidStatus.Text = message;
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { AndroidStatus.Text = "Controle do Android encerrado."; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { AndroidStatus.Text = "O download do scrcpy não terminou. Confira a internet e tente novamente."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        { AndroidStatus.Text = ex is IOException ? ex.Message : "O Windows não conseguiu iniciar o scrcpy."; }
        finally
        {
            androidOperation = null;
            AndroidUsb.IsEnabled = AndroidWifi.IsEnabled = AndroidPair.IsEnabled = AndroidPairQr.IsEnabled = AndroidRemembered.IsEnabled = true;
            AndroidStop.IsEnabled = false;
        }
    }

    private void AndroidStop_Click(object sender, RoutedEventArgs e) => androidOperation?.Cancel();
}
