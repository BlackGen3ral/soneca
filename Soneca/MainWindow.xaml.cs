using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Soneca;
public partial class MainWindow : Window
{
    private readonly Schedule schedule;
    private readonly Schedule suspendSchedule;
    private readonly bool demo;
    private readonly bool skipIntro;
    private DateTimeOffset? target;
    private DateTimeOffset? suspendTarget;
    private bool clockMode;
    private bool updatingStartupOption;
    private readonly DispatcherTimer energyClock = new(DispatcherPriority.Background);
    private int selectedPage = -1;
    private static readonly CultureInfo BrazilianPortuguese = CultureInfo.GetCultureInfo("pt-BR");
    public MainWindow(bool demo, bool skipIntro = false)
    {
        this.demo = demo; this.skipIntro = skipIntro; schedule = new Schedule(demo);
        suspendSchedule = Schedule.ForSuspend(demo);
        InitializeComponent();
        AndroidSurface.Content = androidScreen;
        mobileReconnectTimer.Tick += async (_, _) => await RestoreMobileAsync();
        remoteDesktopTimer.Tick += (_, _) => RefreshRemoteDesktop();
        ConfigureRemoteDesktop();
        CleanupPanel.Demo = demo;
        VersionLabel.Text = "v" + Updater.Current.ToString(3);
        InstalledVersionLabel.Text = "Versão instalada  " + Updater.Current.ToString(3);
        if (demo) { Title = "Artho AI · demonstração"; UpdateLabel.Text = "Demonstração · operações simuladas"; }
        if (!demo)
        {
            try { StartupRegistration.ApplyPreference(); }
            catch (Exception ex) { StartupFeedback.Text = "Não foi possível registrar a inicialização: " + ex.Message; }
            RefreshStartupOption();
        }
        ConfigureDashboard();
        if (!demo) ConfigureTray();
        StateChanged += (_, _) => TrayWindowStateChanged();
        ConfigureReminders();
        try { target = schedule.Read(); } catch (Exception ex) { ErrorLabel.Text = "Não foi possível consultar o agendamento: " + ex.Message; ScheduleButton.IsEnabled = false; }
        try { suspendTarget = suspendSchedule.Read(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
        energyClock.Tick += (_, _) => UpdateEnergyClock();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) StopPageMotion();
            UpdateEnergyClock();
            UpdateDevicePolling();
        };
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized) StopPageMotion();
            UpdateEnergyClock();
            UpdateDevicePolling();
        };
        DashboardContent.IsEnabledChanged += (_, _) =>
        {
            UpdateEnergyClock();
            UpdateDevicePolling();
        };
        devicesTimer.Tick += async (_, _) => await RefreshDevicesAsync();
        internetPowerTimer.Tick += async (_, _) => await PollInternetPowerAsync();
        Render();
        Loaded += async (_, _) =>
        {
            UpdateEnergyClock();
            UpdateDevicePolling();
            if (demo) return;
            RestoreInternetPower();
            _ = RestorePcAgentAsync();
            _ = RestoreMobileAsync();
            if (!skipIntro && SystemParameters.ClientAreaAnimation) await RunIntro();
            await Task.Delay(1800);
            if (IsVisible && !updateBusy && availableUpdate is null) await CheckUpdateAsync();
        };
        Closed += async (_, _) =>
        {
            androidOperation?.Cancel();
            StopInternetPower();
            EndRemoteDesktop();
            energyClock.Stop(); devicesTimer.Stop(); FinishIntro();
            EndHouseSession();
            CloseUpdates();
            CloseReminders();
            CloseTray();
            await StopPcAgentAsync();
            await StopMobileAsync();
        };
        Closing += (_, args) => HandleClose(args);
    }

    private void UpdateEnergyClock()
    {
        if (!IsLoaded || !IsVisible || WindowState == WindowState.Minimized || !DashboardContent.IsEnabled)
        {
            energyClock.Stop();
            return;
        }
        if (selectedPage == 0)
        {
            Render();
            SetEnergyClockInterval(TimeSpan.FromSeconds(1));
        }
        else if (selectedPage == -1)
        {
            var remaining = target - DateTimeOffset.Now;
            RenderHomeEnergy(remaining > TimeSpan.Zero);
            RenderDayRoutines();
            SetEnergyClockInterval(TimeSpan.FromSeconds(15));
        }
        else energyClock.Stop();
    }

    private void SetEnergyClockInterval(TimeSpan delay)
    {
        if (energyClock.Interval != delay) energyClock.Interval = delay;
        if (!energyClock.IsEnabled) energyClock.Start();
    }

    private void RenderHomeEnergy(bool active) =>
        HomeEnergySubtitle.Text = active && target is { } shutdown
            ? $"Agendado para {shutdown.LocalDateTime:dd/MM 'às' HH:mm}"
            : "Definir horário ou contagem regressiva";

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) || SystemParameters.HighContrast) return;
        var handle = new WindowInteropHelper(this).Handle;
        const int DarkCaption = 20, CaptionColor = 35, CaptionTextColor = 36;
        var dark = 1;
        var background = 0x1e110a;
        var text = 0xfcf5ef;
        DwmSetWindowAttribute(handle, DarkCaption, in dark, sizeof(int));
        DwmSetWindowAttribute(handle, CaptionColor, in background, sizeof(int));
        DwmSetWindowAttribute(handle, CaptionTextColor, in text, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint handle, int attribute, in int value, int size);
    private void Startup_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || demo || updatingStartupOption) return;
        try
        {
            bool enabled = StartupCheckBox.IsChecked == true;
            StartupRegistration.SetEnabled(enabled);
            StartupFeedback.Text = enabled ? "Inicialização com Windows ativada." : "Inicialização com Windows desativada.";
        }
        catch (Exception ex)
        {
            StartupFeedback.Text = "Não foi possível alterar a inicialização: " + ex.Message;
            RefreshStartupOption();
        }
    }

    private void RefreshStartupOption()
    {
        if (demo) return;
        const string blocked = "O Windows desativou a inicialização do Artho AI. Marque a opção para reativar.";
        updatingStartupOption = true;
        try
        {
            StartupCheckBox.IsChecked = StartupRegistration.IsEnabled();
            if (StartupRegistration.ResolvePreference(StartupRegistration.Preference) && StartupRegistration.IsBlockedByWindows())
                StartupFeedback.Text = blocked;
            else if (StartupFeedback.Text == blocked) StartupFeedback.Text = "";
        }
        catch (Exception ex) { StartupFeedback.Text = "Não foi possível consultar a inicialização: " + ex.Message; }
        finally { updatingStartupOption = false; }
    }
    private void Render()
    {
        bool active = target.HasValue && target > DateTimeOffset.Now;
        StatusLabel.Text = active ? "Desligamento agendado" : "Nenhum desligamento agendado";
        EnergyTimeCaption.Text = active ? "Tempo restante" : "Prévia da contagem";
        RenderHomeEnergy(active);
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
        bool suspensionPending = suspendTarget > DateTimeOffset.Now;
        SuspendPlan.Visibility = suspensionPending ? Visibility.Visible : Visibility.Collapsed;
        if (suspensionPending) SuspendPlanTime.Text = suspendTarget!.Value.ToLocalTime().ToString("dd/MM 'às' HH:mm");
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
    private void CancelSuspend_Click(object sender, RoutedEventArgs e)
    {
        try { suspendSchedule.Cancel(); suspendTarget = null; ErrorLabel.Text = "Suspensão cancelada."; Render(); }
        catch (Exception ex) { ErrorLabel.Text = "Não foi possível cancelar a suspensão: " + ex.Message; }
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
    private void Navigate(int page)
    {
        if (selectedPage != page)
        {
            StopPageMotion();
            SetPageVisible(selectedPage, false);
            selectedPage = page;
            SetPageVisible(page, true);
            UpdateEnergyClock();
            UpdateDevicePolling();
            // Native phone and WebView surfaces have their own renderers.
            if (page != 7 && !(page == 4 && AndroidViewerPanel.IsVisible)) ShowPageMotion(PagePanel(page));
        }
        var nav = page switch
        {
            0 => EnergyNav,
            1 => CleanupNav,
            2 => NamesNav,
            3 => SettingsNav,
            4 => MobileNav,
            5 => DevicesNav,
            6 => UpdatesNav,
            7 => HouseNav,
            8 => PlanNav,
            _ => HomeNav
        };
        nav.IsChecked = true;
        nav.Focus();
        nav.BringIntoView();
    }

    private void SetPageVisible(int page, bool visible)
    {
        if (page == 4) ShowMobilePage(visible);
        else if (page == 7) ShowHouse(visible);
        else PagePanel(page).Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement PagePanel(int page) => page switch
    {
        0 => EnergyPanel,
        1 => CleanupPanel,
        2 => NamesPanel,
        3 => SettingsPanel,
        4 => MobilePanel,
        5 => DevicesPanel,
        6 => UpdatesPanel,
        7 => HousePage,
        8 => PlanPanel,
        _ => HomePanel
    };
    private void Home_Click(object sender, RoutedEventArgs e) => Navigate(-1);
    private void Energy_Click(object sender, RoutedEventArgs e) => Navigate(0);
    private void Cleanup_Click(object sender, RoutedEventArgs e) => Navigate(1);
    private void Names_Click(object sender, RoutedEventArgs e) => Navigate(2);
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        RefreshStartupOption();
        Navigate(3);
    }
}
internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string PreferenceKey = @"Software\Artho\Soneca";
    private const string ValueName = "ArthoAI";
    internal static bool ResolvePreference(bool? preference) => preference ?? true;
    public static bool? Preference { get { using var key = Registry.CurrentUser.OpenSubKey(PreferenceKey); return key?.GetValue("StartupEnabled") is int value ? value != 0 : null; } }
    internal static bool IsBlocked(byte[]? approval) => approval is [3 or 7, 0, 0, 0, ..];
    internal static bool IsBlockedByWindows()
    {
        using var key = Registry.CurrentUser.OpenSubKey(ApprovalKey);
        return IsBlocked(key?.GetValue(ValueName) as byte[]);
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string && !IsBlockedByWindows();
    }

    public static void ApplyPreference() => WriteRunEntry(ResolvePreference(Preference));

    private static void WriteRunEntry(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (enabled)
        {
            string command = "\"" + Environment.ProcessPath + "\"";
            if (!Equals(key.GetValue(ValueName), command)) key.SetValue(ValueName, command);
        }
        else key.DeleteValue(ValueName, false);
    }

    public static void SetEnabled(bool enabled)
    {
        WriteRunEntry(enabled);
        if (enabled)
        {
            using var approval = Registry.CurrentUser.OpenSubKey(ApprovalKey, true);
            // Take the Windows override into account only when the user explicitly enables startup.
            if (IsBlocked(approval?.GetValue(ValueName) as byte[])) approval!.DeleteValue(ValueName, false);
        }
        using var preference = Registry.CurrentUser.CreateSubKey(PreferenceKey, true);
        preference.SetValue("StartupEnabled", enabled ? 1 : 0, RegistryValueKind.DWord);
    }
    public static void RemoveAll()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true)) key?.DeleteValue(ValueName, false);
        using (var approval = Registry.CurrentUser.OpenSubKey(ApprovalKey, true)) approval?.DeleteValue(ValueName, false);
        using (var preference = Registry.CurrentUser.OpenSubKey(PreferenceKey, true)) preference?.DeleteValue("StartupEnabled", false);
    }
}
