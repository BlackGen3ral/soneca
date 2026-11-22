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
    private Task? androidCancellation;
    private readonly AndroidScreenHost androidScreen = new();
    private AndroidViewWindow? androidView;
    private bool showingAndroid;
    private bool androidActionBusy;
    private string? androidViewError;
    private bool openAndroidManual;
    private double androidAspect = 9d / 20;

    private void MobileDirection_Click(object sender, RoutedEventArgs e)
    {
        bool controlAndroid = AndroidMode.IsChecked == true;
        AndroidModePanel.Visibility = controlAndroid ? Visibility.Visible : Visibility.Collapsed;
        PcMobileModePanel.Visibility = controlAndroid ? Visibility.Collapsed : Visibility.Visible;
    }

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
            EnterAndroidViewer();
            await MirrorPairedAndroidAsync(device, token);
        });
    }

    private async void AndroidRemembered_Click(object sender, RoutedEventArgs e)
    {
        await UseAndroidAsync("Localizando o Android pareado…", async token =>
        {
            EnterAndroidViewer();
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
                EnterAndroidViewer();
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
                EnterAndroidViewer();
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
        await androidControl.MirrorAsync(endpoint, AndroidScreenOpened, token, onFrameSize: AndroidFrameSizeChanged);
        AndroidStatus.Text = "Controle do Android encerrado.";
    }

    private async Task MirrorPairedAndroidAsync(string device, CancellationToken token)
    {
        await androidControl.MirrorWifiAsync(device,
            () =>
            {
                if (token.IsCancellationRequested) return;
                ClearAndroidScreen();
                AndroidStatus.Text = "Aguardando o Android na rede. A tela vai abrir quando a Depuração por Wi-Fi voltar a responder.";
            },
            endpoint => AndroidConnectAddress.Text = endpoint,
            AndroidScreenOpened, token, AndroidFrameSizeChanged);
        AndroidStatus.Text = "Controle do Android encerrado.";
    }

    private void EnterAndroidViewer()
    {
        showingAndroid = true;
        AndroidMode.IsChecked = true;
        MobileDirection_Click(this, new RoutedEventArgs());
        AndroidResume.Visibility = Visibility.Collapsed;
        ShowMobilePage(MobileNav.IsChecked == true);
        if (androidView is not null) ShowAndroidConnectedStatus();
    }

    private void AndroidBack_Click(object sender, RoutedEventArgs e)
    {
        if (androidOperation is null)
        {
            ExitAndroidViewer();
            return;
        }
        showingAndroid = false;
        AndroidResume.Visibility = Visibility.Visible;
        AndroidRemembered.Visibility = AndroidPairQr.Visibility = Visibility.Collapsed;
        ShowMobilePage(MobileNav.IsChecked == true);
        if (androidView is not null) ShowAndroidConnectedStatus();
        AndroidResume.Focus();
    }

    private void AndroidResume_Click(object sender, RoutedEventArgs e)
    {
        if (androidOperation?.IsCancellationRequested == false) EnterAndroidViewer();
    }

    private void ShowMobilePage(bool visible)
    {
        MobilePanel.Visibility = visible && !showingAndroid ? Visibility.Visible : Visibility.Collapsed;
        AndroidViewerPanel.Visibility = visible && showingAndroid ? Visibility.Visible : Visibility.Collapsed;
        try { AttachVisibleAndroidScreen(); }
        catch (IOException ex)
        {
            androidViewError = ex.Message;
            CancelAndroidOperation();
        }
    }

    private void AndroidScreenOpened(AndroidViewWindow view)
    {
        androidOperation?.Token.ThrowIfCancellationRequested();
        androidView = view;
        AttachVisibleAndroidScreen();
        ShowAndroidConnectedStatus();
    }

    private void ShowAndroidConnectedStatus() =>
        AndroidStatus.Text = showingAndroid
            ? "Android conectado. Clique na tela abaixo para usar o mouse e o teclado."
            : "Android conectado. Clique em Mostrar tela para retomar o controle.";

    private void AttachVisibleAndroidScreen()
    {
        if (AndroidViewerPanel.Visibility != Visibility.Visible || androidView is not { } view || androidScreen.IsAttached(view))
            return;
        AndroidPhoneFrame.Visibility = Visibility.Visible;
        FitAndroidPhone();
        AndroidViewerPanel.UpdateLayout();
        androidScreen.Attach(view);
        AndroidWaitingPanel.Visibility = Visibility.Collapsed;
        UpdateAndroidPowerButtons();
    }

    private void AndroidStage_SizeChanged(object sender, SizeChangedEventArgs e) => FitAndroidPhone();

    private void AndroidFrameSizeChanged(int width, int height)
    {
        androidAspect = Math.Clamp((double)width / height, 0.3, 3.5);
        FitAndroidPhone();
    }

    private void FitAndroidPhone()
    {
        if (AndroidPhoneFrame is null || AndroidStage is null ||
            AndroidStage.ActualWidth < 45 || AndroidStage.ActualHeight < 65) return;
        double width = AndroidStage.ActualWidth - 44;
        double height = Math.Min(AndroidStage.ActualHeight - 56, width / androidAspect);
        AndroidPhoneFrame.Width = Math.Floor(height * androidAspect) + 20;
        AndroidPhoneFrame.Height = Math.Floor(height) + 32;
    }

    private void ClearAndroidScreen()
    {
        androidScreen.Clear();
        androidView = null;
        UpdateAndroidPowerButtons();
        AndroidPhoneFrame.Visibility = Visibility.Collapsed;
        AndroidWaitingPanel.Visibility = Visibility.Visible;
    }

    private void ExitAndroidViewer()
    {
        ClearAndroidScreen();
        showingAndroid = false;
        AndroidResume.Visibility = Visibility.Collapsed;
        AndroidRemembered.Visibility = AndroidPairQr.Visibility = Visibility.Visible;
        if (IsLoaded) ShowMobilePage(MobileNav.IsChecked == true);
    }

    private void CancelAndroidOperation()
    {
        if (androidOperation is { } operation) androidCancellation ??= operation.CancelAsync();
    }

    private void UpdateAndroidPowerButtons() =>
        AndroidWake.IsEnabled = AndroidSleep.IsEnabled = AndroidReboot.IsEnabled =
            !androidActionBusy && androidView?.Serial is not null;

    private async void AndroidWake_Click(object sender, RoutedEventArgs e) =>
        await RunAndroidActionAsync(androidControl.WakeScreenAsync, "Comando para acender a tela enviado ao Android.");

    private async void AndroidSleep_Click(object sender, RoutedEventArgs e) =>
        await RunAndroidActionAsync(androidControl.SleepScreenAsync, "Comando para desligar a tela enviado ao Android.");

    private async void AndroidReboot_Click(object sender, RoutedEventArgs e)
    {
        var device = androidView;
        if (device?.Serial is null || androidActionBusy) return;
        var result = MessageBox.Show(this, "O celular será reiniciado e a tela remota será interrompida. Quer continuar?",
            "Reiniciar celular", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result == MessageBoxResult.Yes && androidView == device)
            await RunAndroidActionAsync(androidControl.RebootAsync, "Reinício solicitado. Aguarde o Android voltar.");
    }

    private async Task RunAndroidActionAsync(Func<string, CancellationToken, Task> action, string completed)
    {
        if (androidActionBusy || androidView is not { Serial: { } serial } active || androidOperation is null) return;
        androidActionBusy = true;
        UpdateAndroidPowerButtons();
        try
        {
            await action(serial, androidOperation.Token);
            if (androidView == active) AndroidStatus.Text = completed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or Win32Exception)
        {
            if (androidView == active)
                AndroidStatus.Text = ex is IOException ? ex.Message : "O Windows não conseguiu enviar o comando ao Android.";
        }
        finally
        {
            androidActionBusy = false;
            UpdateAndroidPowerButtons();
        }
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
        androidCancellation = null;
        androidViewError = null;
        openAndroidManual = false;
        AndroidUsb.IsEnabled = AndroidWifi.IsEnabled = AndroidPair.IsEnabled = AndroidPairQr.IsEnabled = AndroidRemembered.IsEnabled = false;
        AndroidStop.IsEnabled = true;
        AndroidStop.Visibility = Visibility.Visible;
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
        { AndroidStatus.Text = androidViewError ?? "Controle do Android encerrado."; }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        { AndroidStatus.Text = "O download do scrcpy não terminou. Confira a internet e tente novamente."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        { AndroidStatus.Text = ex is IOException ? ex.Message : "O Windows não conseguiu iniciar o scrcpy."; }
        finally
        {
            ExitAndroidViewer();
            if (androidCancellation is { } stopping) await stopping;
            androidCancellation = null;
            androidOperation = null;
            AndroidUsb.IsEnabled = AndroidWifi.IsEnabled = AndroidPair.IsEnabled = AndroidPairQr.IsEnabled = AndroidRemembered.IsEnabled = true;
            AndroidStop.IsEnabled = false;
            AndroidStop.Visibility = Visibility.Collapsed;
            if (openAndroidManual && IsLoaded)
            {
                AndroidWifiOptions.IsExpanded = AndroidManual.IsExpanded = true;
                MobilePanel.UpdateLayout();
                AndroidConnectAddress.BringIntoView();
                AndroidConnectAddress.Focus();
                AndroidStatus.Text = "Use o endereço e a porta que aparecem na tela de Depuração por Wi-Fi do Android.";
            }
        }
    }

    private void AndroidManualFromViewer_Click(object sender, RoutedEventArgs e)
    {
        openAndroidManual = true;
        StopAndroidControl();
    }

    private void AndroidStop_Click(object sender, RoutedEventArgs e) => StopAndroidControl();

    private void StopAndroidControl()
    {
        ExitAndroidViewer();
        HideAndroidQr();
        AndroidStop.IsEnabled = false;
        if (androidOperation is null)
        {
            AndroidStatus.Text = "Controle do Android encerrado.";
            return;
        }
        AndroidStatus.Text = "Encerrando o controle do Android…";
        CancelAndroidOperation();
    }
}
