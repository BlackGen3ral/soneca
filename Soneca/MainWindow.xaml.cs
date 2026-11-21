using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Globalization;

namespace Soneca;
public partial class MainWindow : Window
{
    private readonly Schedule schedule;
    private readonly bool demo;
    private readonly bool skipIntro;
    private DateTimeOffset? target;
    private bool clockMode;
    private bool updating;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer dashboardTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private static readonly CultureInfo BrazilianPortuguese = CultureInfo.GetCultureInfo("pt-BR");
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
        dashboardTimer.Tick += (_, _) =>
        {
            if (HomePanel.IsVisible && DashboardContent.IsEnabled && WindowState != WindowState.Minimized)
                LoadDashboard();
        };
        dashboardTimer.Start();
        Loaded += async (_, _) =>
        {
            if (demo) return;
            if (!skipIntro) await RunIntro();
            await Task.Delay(1800);
            if (IsVisible) await CheckUpdate(false);
        };
        Closed += async (_, _) => { timer.Stop(); dashboardTimer.Stop(); FinishIntro(); await StopMobileAsync(); };
        Closing += (_, args) => { if (CleanupPanel.Busy || NamesPanel.Busy) { args.Cancel = true; UpdateLabel.Text = "Aguarde a operação terminar antes de fechar."; } };
    }
    private void LoadDashboard()
    {
        TodayLabel.Text = DateTime.Now.ToString("dddd, d 'de' MMMM", BrazilianPortuguese);
        var local = LocalComputerAgent.Read();
        ComputerNameLabel.Text = local.Name;
        ComputerDetailsLabel.Text = "Windows · build " + Environment.OSVersion.Version.Build;
        bool hasMemory = local.TotalMemoryGb > 0;
        MemoryValue.Text = hasMemory ? local.MemoryInUseGb.ToString("0.0", BrazilianPortuguese) + " GB" : "Indisponível";
        MemoryTotal.Text = hasMemory ? "de " + local.TotalMemoryGb.ToString("0.0", BrazilianPortuguese) + " GB" : "";
        MemoryGauge.Visibility = hasMemory ? Visibility.Visible : Visibility.Collapsed;
        MemoryGauge.Value = hasMemory ? 100 * local.MemoryInUseGb / local.TotalMemoryGb : 0;
        ProcessorValue.Text = local.ProcessorCount.ToString(BrazilianPortuguese);
        UptimeValue.Text = local.Uptime.Days > 0
            ? $"{local.Uptime.Days} d {local.Uptime.Hours} h"
            : $"{local.Uptime.Hours} h {local.Uptime.Minutes} min";
        NetworkStatusLabel.Text = local.NetworkAvailable ? "Interface ativa" : "Sem interface ativa";
    }
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
        SettingsPanel.Visibility = page == 3 ? Visibility.Visible : Visibility.Collapsed;
        MobilePanel.Visibility = page == 4 ? Visibility.Visible : Visibility.Collapsed;
        var nav = page switch
        {
            0 => EnergyNav,
            1 => CleanupNav,
            2 => NamesNav,
            3 => SettingsNav,
            4 => MobileNav,
            _ => HomeNav
        };
        nav.IsChecked = true;
        nav.Focus();
        if (page == -1) LoadDashboard();
    }
    private void Home_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void Energy_Click(object sender, RoutedEventArgs e) => Navigate(0);
    private void Cleanup_Click(object sender, RoutedEventArgs e) => Navigate(1);
    private void Names_Click(object sender, RoutedEventArgs e) => Navigate(2);
    private void Settings_Click(object sender, RoutedEventArgs e) => Navigate(3);
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
