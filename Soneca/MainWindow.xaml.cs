using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Media;
using System.Reflection;
using System.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;

namespace Soneca;
public partial class MainWindow : Window
{
    private readonly Schedule schedule;
    private readonly bool demo;
    private readonly bool skipIntro;
    private readonly CancellationTokenSource introCancellation = new();
    private DateTimeOffset? target;
    private bool clockMode;
    private bool updating;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public MainWindow(bool demo, bool skipIntro = false)
    {
        this.demo = demo; this.skipIntro = skipIntro; schedule = new Schedule(demo);
        InitializeComponent();
        CleanupPanel.Demo = demo;
        VersionLabel.Text = "v" + Updater.Current.ToString(3);
        if (demo) { Title = "Artho AI · demonstração"; UpdateLabel.Text = "Demonstração · operações simuladas"; }
        bool startWithWindows = !demo && StartupRegistration.ResolvePreference(StartupRegistration.Preference);
        StartupCheckBox.IsChecked = startWithWindows;
        if (!demo) StartupRegistration.SetEnabled(startWithWindows);
        LoadDashboard();
        try { target = schedule.Read(); } catch (Exception ex) { ErrorLabel.Text = "Não foi possível consultar o agendamento: " + ex.Message; ScheduleButton.IsEnabled = false; }
        timer.Tick += (_, _) => Render(); timer.Start(); Render();
        Loaded += async (_, _) => { if (!demo) { _ = RunIntro(); await System.Threading.Tasks.Task.Delay(1800); await CheckUpdate(false); } else IntroOverlay.Visibility = Visibility.Collapsed; };
        Closed += (_, _) => { timer.Stop(); introCancellation.Cancel(); CinematicAudio.Stop(); };
        Closing += (_, args) => { if (CleanupPanel.Busy || NamesPanel.Busy) { args.Cancel = true; UpdateLabel.Text = "Aguarde a operação terminar antes de fechar."; } };
    }
    private void LoadDashboard()
    {
        TodayLabel.Text = DateTime.Now.ToString("dddd, d 'de' MMMM", new System.Globalization.CultureInfo("pt-BR"));
        var local = LocalComputerAgent.Read();
        ComputerNameLabel.Text = local.Name;
        string memory = local.TotalMemoryGb > 0 ? $"{local.MemoryInUseGb:0.0} de {local.TotalMemoryGb:0.0} GB de memória em uso" : "Memória indisponível";
        ComputerDetailsLabel.Text = $"{local.WindowsVersion}  ·  {local.ProcessorCount} processadores lógicos  ·  {memory}  ·  Ativo há {local.Uptime.Days}d {local.Uptime.Hours}h";
        NetworkStatusLabel.Text = local.NetworkAvailable ? "Interface ativa" : "Sem interface ativa";
        IntroStatus.Text = local.NetworkAvailable ? "Computador pronto · rede disponível" : "Computador pronto · sem rede detectada";
    }
    private async Task RunIntro()
    {
        if (skipIntro) { IntroOverlay.Visibility = Visibility.Collapsed; return; }
        var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(5)) { RepeatBehavior = RepeatBehavior.Forever };
        RingRotation.BeginAnimation(RotateTransform.AngleProperty, spin);
        var pulse = new DoubleAnimation(0.08, 0.45, TimeSpan.FromSeconds(1.1)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        CoreGlow.BeginAnimation(UIElement.OpacityProperty, pulse);
        CinematicAudio.PlayEffects();
        try { await Task.Delay(TimeSpan.FromMilliseconds(2850), introCancellation.Token); } catch (TaskCanceledException) { return; }
        if (introCancellation.IsCancellationRequested) return;
        IntroStatus.Text = "Artho AI inicializado. Bem-vindo de volta.";
        CinematicAudio.Speak("Artho AI inicializado. Sistemas locais verificados. Bem-vindo de volta. Estou à sua disposição.");
        try { await Task.Delay(TimeSpan.FromMilliseconds(2150), introCancellation.Token); } catch (TaskCanceledException) { return; }
        FinishIntro();
    }
    private void FinishIntro()
    {
        introCancellation.Cancel();
        CinematicAudio.Stop();
        IntroOverlay.Visibility = Visibility.Collapsed;
    }
    private void SkipIntro_Click(object sender, RoutedEventArgs e) => FinishIntro();
    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || demo) return;
        try { StartupRegistration.SetEnabled(StartupCheckBox.IsChecked == true); UpdateLabel.Text = StartupCheckBox.IsChecked == true ? "Inicialização com Windows ativada." : "Inicialização com Windows desativada."; }
        catch (Exception ex) { UpdateLabel.Text = "Não foi possível alterar a inicialização: " + ex.Message; StartupCheckBox.IsChecked = StartupRegistration.IsEnabled(); }
    }
    private void Render()
    {
        bool active = target.HasValue && target > DateTimeOffset.Now;
        StatusLabel.Text = active ? "DESLIGAMENTO AGENDADO" : "NENHUM DESLIGAMENTO AGENDADO";
        if (active)
        {
            CountdownLabel.Text = TimeRules.Countdown(target!.Value, DateTimeOffset.Now);
            TargetLabel.Text = "Windows desliga " + target.Value.ToLocalTime().ToString("dd/MM 'às' HH:mm");
        }
        else
        {
            try { var preview = GetTarget(); CountdownLabel.Text = TimeRules.Countdown(preview, DateTimeOffset.Now); TargetLabel.Text = "Previsão: " + preview.ToLocalTime().ToString("dd/MM 'às' HH:mm"); }
            catch { CountdownLabel.Text = "--:--:--"; TargetLabel.Text = "Escolha um tempo ou horário válido."; }
            if (target.HasValue) { TargetLabel.Text = "O horário programado já passou."; target = null; }
        }
        CancelButton.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ScheduleButton.Content = active ? "Alterar agendamento" : "Agendar desligamento";
    }
    private DateTimeOffset GetTarget() => clockMode ? TimeRules.At(TimeInput.Text, DateTimeOffset.Now) : TimeRules.After(TimeInput.Text, DateTimeOffset.Now);
    private void Schedule_Click(object sender, RoutedEventArgs e)
    {
        try { var next = GetTarget(); schedule.Set(next); target = next; ErrorLabel.Text = ""; Render(); }
        catch (Exception ex) { ErrorLabel.Text = ex.Message; }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try { schedule.Cancel(); target = null; ErrorLabel.Text = "Agendamento cancelado."; Render(); }
        catch (Exception ex) { ErrorLabel.Text = "Não foi possível cancelar: " + ex.Message; }
    }
    private void SetMode(bool clock)
    {
        clockMode = clock; Presets.Visibility = clock ? Visibility.Collapsed : Visibility.Visible;
        InputLabel.Text = clock ? "Horário de hoje ou amanhã (HH:mm)" : "Tempo personalizado (minutos)";
        TimeInput.Text = clock ? DateTime.Now.AddHours(1).ToString("HH:mm") : "30";
        DelayMode.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(clock ? "#252530" : "#49405F"));
        ClockMode.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(clock ? "#49405F" : "#252530"));
        ErrorLabel.Text = ""; Render();
    }
    private void Delay_Click(object sender, RoutedEventArgs e) => SetMode(false);
    private void Clock_Click(object sender, RoutedEventArgs e) => SetMode(true);
    private void Preset_Click(object sender, RoutedEventArgs e) { TimeInput.Text = (string)((Button)sender).Tag; ErrorLabel.Text = ""; Render(); }
    private void Input_Changed(object sender, TextChangedEventArgs e) { if (IsLoaded) Render(); }
    private async void Update_Click(object sender, RoutedEventArgs e) => await CheckUpdate(true);
    private void Navigate(int page)
    {
        HomePanel.Visibility = page == -1 ? Visibility.Visible : Visibility.Collapsed;
        EnergyPanel.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        CleanupPanel.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        NamesPanel.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        HomeNav.Background = page == -1 ? new SolidColorBrush(Color.FromRgb(36,65,85)) : Brushes.Transparent;
        EnergyNav.Background = page == 0 ? new SolidColorBrush(Color.FromRgb(73,64,95)) : Brushes.Transparent;
        CleanupNav.Background = page == 1 ? new SolidColorBrush(Color.FromRgb(73,64,95)) : Brushes.Transparent;
        NamesNav.Background = page == 2 ? new SolidColorBrush(Color.FromRgb(73,64,95)) : Brushes.Transparent;
    }
    private void Home_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void Energy_Click(object sender, RoutedEventArgs e) => Navigate(0);
    private void Cleanup_Click(object sender, RoutedEventArgs e) => Navigate(1);
    private void Names_Click(object sender, RoutedEventArgs e) => Navigate(2);
    private async Task CheckUpdate(bool manual)
    {
        if (demo || updating) return;
        if (CleanupPanel.Busy || NamesPanel.Busy) { UpdateLabel.Text = "Aguarde a operação terminar antes de atualizar."; return; }
        if (target > DateTimeOffset.Now) { UpdateLabel.Text = "A atualização será verificada ao reabrir sem agendamento."; return; }
        updating = true; UpdateButton.IsEnabled = false;
        try
        {
            var update = await Updater.Check();
            if (update is null) { UpdateLabel.Text = "Você está na versão mais recente."; return; }
            if (CleanupPanel.Busy || NamesPanel.Busy) { UpdateLabel.Text = "Atualização adiada até a operação terminar."; return; }
            // Lock scheduling before downloading so no new timer can be lost during installation.
            ScheduleButton.IsEnabled = false;
            CleanupPanel.SetEnabled(false);
            NamesPanel.IsEnabled = false;
            UpdateLabel.Text = "Baixando atualização " + update.Version + "…";
            var installer = await Updater.Download(update);
            if (schedule.Read() > DateTimeOffset.Now) { UpdateLabel.Text = "Atualização adiada: existe um agendamento ativo."; return; }
            Updater.Install(installer); Application.Current.Shutdown();
        }
        catch (Exception ex) { UpdateLabel.Text = manual ? "Não foi possível atualizar: " + ex.Message : "Sem conexão para atualizar. Tentaremos ao reabrir."; }
        finally { updating = false; UpdateButton.IsEnabled = true; ScheduleButton.IsEnabled = true; CleanupPanel.SetEnabled(true); NamesPanel.IsEnabled = true; }
    }
}
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string PreferenceKey = @"Software\Artho\Soneca";
    private const string ValueName = "ArthoAI";
    internal static bool ResolvePreference(bool? preference) => preference ?? true;
    public static bool? Preference { get { using var key = Registry.CurrentUser.OpenSubKey(PreferenceKey); return key?.GetValue("StartupEnabled") is int value ? value != 0 : null; } }
    public static bool IsEnabled() { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue(ValueName) is string; }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled) key.SetValue(ValueName, "\"" + Environment.ProcessPath + "\"");
        else key.DeleteValue(ValueName, false);
        using var preference = Registry.CurrentUser.CreateSubKey(PreferenceKey, true);
        preference.SetValue("StartupEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
    }
    public static void RemoveAll()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true)) key?.DeleteValue(ValueName, false);
        using (var preference = Registry.CurrentUser.OpenSubKey(PreferenceKey, true)) preference?.DeleteValue("StartupEnabled", false);
    }
}

internal static class CinematicAudio
{
    private static MemoryStream? stream;
    private static SoundPlayer? player;
    private static object? voice;
    public static void PlayEffects()
    {
        try
        {
            const int rate = 22050, seconds = 5;
            int count = rate * seconds;
            stream = new MemoryStream(44 + count * 2);
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(count * 2);
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / rate, sample = 0;
                    (double at, double duration, double f1, double f2)[] cues = [(0.16, 0.95, 62, 93), (1.45, 0.75, 110, 164), (2.55, 1.30, 82, 123)];
                    foreach (var cue in cues) if (t >= cue.at && t < cue.at + cue.duration)
                    {
                        double x = t - cue.at, env = Math.Min(1, x * 9) * Math.Exp(-x * 2.7);
                        sample += env * (Math.Sin(2 * Math.PI * cue.f1 * x) * 0.42 + Math.Sin(2 * Math.PI * cue.f2 * x) * 0.12);
                    }
                    writer.Write((short)(Math.Clamp(sample, -0.9, 0.9) * short.MaxValue));
                }
            }
            stream.Position = 0; player = new SoundPlayer(stream); player.Play();
        }
        catch { Stop(); }
    }
    public static void Speak(string text)
    {
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice"); if (type is null) return;
            voice = Activator.CreateInstance(type); if (voice is null) return;
            var v = voice;
            object? tokens = v.GetType().InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, v, ["", ""]);
            if (tokens is not null)
            {
                object? preferred = null, portuguese = null;
                int count = Convert.ToInt32(tokens.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, tokens, null));
                for (int i = 0; i < count; i++)
                {
                    object? token = tokens.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, tokens, [i]);
                    if (token is null) continue;
                    string gender = Convert.ToString(token.GetType().InvokeMember("GetAttribute", BindingFlags.InvokeMethod, null, token, ["Gender"])) ?? "";
                    string language = Convert.ToString(token.GetType().InvokeMember("GetAttribute", BindingFlags.InvokeMethod, null, token, ["Language"])) ?? "";
                    if (gender.Equals("Male", StringComparison.OrdinalIgnoreCase) && (preferred is null || language.Contains("0416", StringComparison.OrdinalIgnoreCase))) preferred = token;
                    if (gender.Equals("Male", StringComparison.OrdinalIgnoreCase) && language.Contains("0416", StringComparison.OrdinalIgnoreCase)) { portuguese = token; break; }
                }
                var selected = portuguese ?? preferred;
                if (selected is not null) v.GetType().InvokeMember("Voice", BindingFlags.SetProperty, null, v, [selected]);
            }
            v.GetType().InvokeMember("Rate", BindingFlags.SetProperty, null, v, [-2]);
            v.GetType().InvokeMember("Volume", BindingFlags.SetProperty, null, v, [88]);
            v.GetType().InvokeMember("Speak", BindingFlags.InvokeMethod, null, v, [text, 1]);
        }
        catch { }
    }
    public static void Stop()
    {
        try { player?.Stop(); } catch { }
        try { if (voice is not null) voice.GetType().InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, ["", 3]); } catch { }
        player?.Dispose(); player = null; stream?.Dispose(); stream = null;
        if (voice is not null) { try { Marshal.FinalReleaseComObject(voice); } catch { } voice = null; }
    }
}
