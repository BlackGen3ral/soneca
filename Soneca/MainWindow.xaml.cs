using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Soneca;
public partial class MainWindow : Window
{
    private readonly Schedule schedule;
    private readonly bool demo;
    private DateTimeOffset? target;
    private bool clockMode;
    private bool updating;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    public MainWindow(bool demo)
    {
        this.demo = demo; schedule = new Schedule(demo);
        InitializeComponent();
        CleanupPanel.Demo = demo;
        VersionLabel.Text = "v" + Updater.Current.ToString(3);
        if (demo) { Title = "Artho Control · demonstração"; UpdateLabel.Text = "Demonstração · operações simuladas"; }
        try { target = schedule.Read(); } catch (Exception ex) { ErrorLabel.Text = "Não foi possível consultar o agendamento: " + ex.Message; ScheduleButton.IsEnabled = false; }
        timer.Tick += (_, _) => Render(); timer.Start(); Render();
        Loaded += async (_, _) => { if (!demo) { await System.Threading.Tasks.Task.Delay(1800); await CheckUpdate(false); } };
        Closed += (_, _) => timer.Stop();
        Closing += (_, args) => { if (CleanupPanel.Busy || NamesPanel.Busy) { args.Cancel = true; UpdateLabel.Text = "Aguarde a operação terminar antes de fechar."; } };
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
        EnergyPanel.Visibility = page == 0 ? Visibility.Visible : Visibility.Collapsed;
        CleanupPanel.Visibility = page == 1 ? Visibility.Visible : Visibility.Collapsed;
        NamesPanel.Visibility = page == 2 ? Visibility.Visible : Visibility.Collapsed;
        EnergyNav.Background = page == 0 ? new SolidColorBrush(Color.FromRgb(73,64,95)) : Brushes.Transparent;
        CleanupNav.Background = page == 1 ? new SolidColorBrush(Color.FromRgb(73,64,95)) : Brushes.Transparent;
        NamesNav.Background = page == 2 ? new SolidColorBrush(Color.FromRgb(73,64,95)) : Brushes.Transparent;
    }
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
