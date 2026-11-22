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
                await androidControl.PairAsync(endpoint, code, token);
                AndroidPairAddress.Clear();
                AndroidPairHelp.IsExpanded = false;
                AndroidStatus.Text = "Pareado. Use o endereço da tela principal de Depuração sem fio para conectar.";
                AndroidConnectAddress.Focus();
            });
        }
        catch (ArgumentException ex) { AndroidStatus.Text = ex.Message; }
    }

    private async Task OpenAndroidAsync(string? wifiEndpoint)
    {
        await UseAndroidAsync(wifiEndpoint is null ? "Procurando o Android conectado por USB…" : "Conectando ao Android por Wi-Fi…",
            async token =>
            {
                await androidControl.MirrorAsync(wifiEndpoint,
                    () => AndroidStatus.Text = "Tela do Android aberta. Use o mouse e o teclado na janela de controle.", token);
                AndroidStatus.Text = "Controle do Android encerrado.";
            });
    }

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
        AndroidUsb.IsEnabled = AndroidWifi.IsEnabled = AndroidPair.IsEnabled = false;
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
            AndroidUsb.IsEnabled = AndroidWifi.IsEnabled = AndroidPair.IsEnabled = true;
            AndroidStop.IsEnabled = false;
        }
    }

    private void AndroidStop_Click(object sender, RoutedEventArgs e) => androidOperation?.Cancel();
}
