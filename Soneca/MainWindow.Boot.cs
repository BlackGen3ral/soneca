using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Soneca;

public partial class MainWindow
{
    private WebView2? bootView;
    private TaskCompletionSource? bootFinished;
    private DispatcherTimer? bootTimeout;
    private SoundPlayer? bootSound;
    private Stream? bootSoundStream;
    private bool bootStarted;
    private bool returnToReplay;

    private Task RunIntro(bool replay = false)
    {
        if (bootFinished is not null) return bootFinished.Task;

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bootFinished = completion;
        bootStarted = false;
        returnToReplay = replay;
        DashboardContent.IsEnabled = false;
        IntroPlaceholder.Visibility = Visibility.Visible;
        IntroOverlay.Visibility = Visibility.Visible;
        SkipIntroButton.Focus();
        bootTimeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        bootTimeout.Tick += (_, _) => FinishIntro();
        bootTimeout.Start();
        _ = OpenBootPage(completion);
        return completion.Task;
    }

    private async Task OpenBootPage(TaskCompletionSource completion)
    {
        try
        {
            var profile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Soneca", demo ? "BootPreview" : "Boot");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (bootFinished != completion) return;

            var view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(2, 5, 13) };
            bootView = view;
            IntroBrowser.Content = view;
            await view.EnsureCoreWebView2Async(environment);
            if (bootFinished != completion) return;

            var browser = view.CoreWebView2;
            browser.Settings.AreHostObjectsAllowed = false;
            browser.Settings.AreDevToolsEnabled = false;
            browser.Settings.AreDefaultContextMenusEnabled = false;
            browser.Settings.AreBrowserAcceleratorKeysEnabled = false;
            browser.Settings.IsZoomControlEnabled = false;
            // NavigateToString internally starts a data: navigation and reports about:blank to the document.
            bool openingEmbeddedPage = true;
            browser.NavigationStarting += (_, args) =>
            {
                bool allowed = openingEmbeddedPage &&
                    args.Uri.StartsWith("data:text/html;charset=utf-8;base64,", StringComparison.Ordinal);
                if (allowed) openingEmbeddedPage = false;
                args.Cancel = !allowed;
            };
            browser.NewWindowRequested += (_, args) => args.Handled = true;
            browser.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            browser.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess) QueueFinishIntro(view);
            };
            browser.ProcessFailed += (_, _) => QueueFinishIntro(view);
            browser.WebMessageReceived += (_, args) =>
            {
                if (bootView != view || args.Source != "about:blank") return;
                switch (args.WebMessageAsJson)
                {
                    case "\"ready\"":
                        browser.PostWebMessageAsJson(SerializeBootSnapshot());
                        break;
                    case "\"started\"":
                        if (bootTimeout is null || bootStarted) return;
                        bootStarted = true;
                        bootTimeout.Stop();
                        bootTimeout.Interval = TimeSpan.FromSeconds(6);
                        bootTimeout.Start();
                        IntroPlaceholder.Visibility = Visibility.Collapsed;
                        PlayBootEffects();
                        break;
                    case "\"complete\"":
                    case "\"skip\"":
                        QueueFinishIntro(view);
                        break;
                }
            };

            using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("Artho.Boot.Intro.html")
                ?? throw new InvalidOperationException("A abertura não está disponível.");
            using var reader = new StreamReader(stream);
            browser.NavigateToString(reader.ReadToEnd());
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Abertura do Artho AI: " + ex.Message);
            if (bootFinished == completion) FinishIntro();
        }
    }

    private static string SerializeBootSnapshot()
    {
        try
        {
            var snapshot = LocalComputerAgent.Read();
            var culture = CultureInfo.GetCultureInfo("pt-BR");
            return JsonSerializer.Serialize(new
            {
                processor = $"{snapshot.ProcessorCount} processadores lógicos",
                memory = snapshot.TotalMemoryGb > 0
                    ? string.Format(culture, "{0:0.#} de {1:0.#} GB", snapshot.MemoryInUseGb, snapshot.TotalMemoryGb)
                    : "Leitura indisponível",
                network = snapshot.NetworkAvailable ? "Interface ativa" : "Sem interface ativa"
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Leituras da abertura: " + ex.Message);
            return "null";
        }
    }

    private void PlayBootEffects()
    {
        if (bootSound is not null) return;
        try
        {
            bootSoundStream = typeof(MainWindow).Assembly.GetManifestResourceStream("Artho.Boot.Intro.wav");
            if (bootSoundStream is null) return;
            bootSound = new SoundPlayer(bootSoundStream);
            bootSound.Play();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Efeitos da abertura: " + ex.Message);
        }
    }

    private void QueueFinishIntro(WebView2 view)
    {
        // Dispose only after the WebView event handler returns.
        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            if (bootView == view) FinishIntro();
        }));
    }

    private void FinishIntro()
    {
        var completion = bootFinished;
        if (completion is null) return;

        bootFinished = null;
        bootTimeout?.Stop();
        bootTimeout = null;
        IntroOverlay.Visibility = Visibility.Collapsed;
        DashboardContent.IsEnabled = true;

        var view = bootView;
        bootView = null;
        IntroBrowser.Content = null;
        try
        {
            bootSound?.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Encerramento dos efeitos: " + ex.Message);
        }
        finally
        {
            bootSound?.Dispose();
            bootSoundStream?.Dispose();
            bootSound = null;
            bootSoundStream = null;
        }
        try
        {
            view?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Encerramento da abertura: " + ex.Message);
        }
        if (IsVisible) (returnToReplay ? ReplayIntroButton : HomeNav).Focus();
        completion.TrySetResult();
    }

    private async void ReplayIntro_Click(object sender, RoutedEventArgs e) => await RunIntro(replay: true);

    private void SkipIntro_Click(object sender, RoutedEventArgs e) => FinishIntro();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && bootFinished is not null)
        {
            e.Handled = true;
            _ = Dispatcher.BeginInvoke(new Action(FinishIntro));
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
