using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Soneca;

public partial class MainWindow
{
    private readonly DispatcherTimer devicesTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private PcAgent? pcAgent;
    private bool agentBusy;
    private bool devicesRefreshing;

    private async void Devices_Click(object sender, RoutedEventArgs e)
    {
        Navigate(5);
        if (!devicesTimer.IsEnabled)
        {
            devicesTimer.Tick += async (_, _) =>
            {
                if (DevicesPanel.IsVisible && WindowState != WindowState.Minimized) await RefreshDevicesAsync();
            };
            devicesTimer.Start();
        }
        var addresses = PcLan.Adapters();
        var oldAddress = (PcAgentNetwork.SelectedItem as LanAdapter)?.Address.ToString();
        PcAgentNetwork.ItemsSource = addresses;
        PcAgentNetwork.SelectedItem = addresses.FirstOrDefault(item => item.Address.ToString() == oldAddress) ?? addresses.FirstOrDefault();
        DevicePair.IsEnabled = !demo;
        UpdateAgentButtons();
        await RefreshDevicesAsync();
    }

    private async void DevicesRefresh_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync();

    private async Task RefreshDevicesAsync()
    {
        if (devicesRefreshing) return;
        devicesRefreshing = true;
        try
        {
            var cards = demo
                ? new[] { new PcCard(Guid.Empty, "PC de demonstração", "192.168.1.50", true, DateTimeOffset.Now,
                    new WeeklyRule("08:00", 126), new WeeklyRule("18:00", 62), null) }
                : await Task.Run(() => PcDeviceService.ListAsync());
            if (!IsLoaded) return;
            DeviceCards.Children.Clear();
            DeviceList.Visibility = Visibility.Visible;
            if (cards.Length == 0)
                DeviceCards.Children.Add(new TextBlock { Text = "Nenhum PC pareado.", Margin = new Thickness(13), Style = (Style)FindResource("BodyCopy") });
            for (int index = 0; index < cards.Length; index++)
                DeviceCards.Children.Add(DeviceRow(cards[index], index < cards.Length - 1));
        }
        catch (Exception ex) { ShowDeviceNotice(ex.Message); }
        finally { devicesRefreshing = false; }
    }

    private Border DeviceRow(PcCard pc, bool hasFollowing)
    {
        var body = new StackPanel();
        var panel = new Border
        {
            Padding = new Thickness(15, 12, 15, 11), Child = body,
            BorderBrush = (System.Windows.Media.Brush)FindResource("Line"),
            BorderThickness = new Thickness(0, 0, 0, hasFollowing ? 1 : 0)
        };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var state = new TextBlock
        {
            Text = pc.Online ? "Online" : "Sem resposta",
            Style = (Style)FindResource("BodyCopy"),
            Foreground = (System.Windows.Media.Brush)FindResource(pc.Online ? "Accent" : "Muted"),
            Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center
        };
        DockPanel.SetDock(state, Dock.Right);
        heading.Children.Add(state);
        heading.Children.Add(new TextBlock { Text = pc.Name, Style = (Style)FindResource("SectionTitle") });
        body.Children.Add(heading);
        string last = pc.LastContact?.ToLocalTime().ToString("dd/MM/yyyy 'às' HH:mm:ss") ?? "ainda não respondeu";
        AddText($"{pc.Address}  ·  Último contato {last}");
        AddText($"Ligar {DescribeRule(pc.Wake)}");
        AddText($"Desligar {DescribeRule(pc.Shutdown)}");
        if (!string.IsNullOrEmpty(pc.LastRoutine)) AddText(pc.LastRoutine);

        var actions = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        body.Children.Add(actions);
        AddButton("Ligar agora", async () =>
        {
            if (demo) { ShowDeviceNotice("Demonstração. Nenhum sinal real foi enviado."); return; }
            await DeviceAction(async () =>
            {
                await PcDeviceService.WakeAsync(pc.Id);
                ShowDeviceNotice($"Sinal enviado para {pc.Name}. O painel muda para Online quando o PC responder.");
            });
        });
        AddButton("Editar horários", async () =>
        {
            var editor = new PcRoutineWindow(pc) { Owner = this };
            if (editor.ShowDialog() != true) return;
            if (demo) { ShowDeviceNotice("Horários conferidos no modo de demonstração."); return; }
            await DeviceAction(async () =>
            {
                await Task.Run(() => PcDeviceService.Save(pc.Id, editor.WakeRule, editor.ShutdownRule));
                ShowDeviceNotice($"Horários de {pc.Name} salvos.");
                await RefreshDevicesAsync();
            });
        });
        AddButton("Remover", async () =>
        {
            if (demo || MessageBox.Show($"Remover {pc.Name} e cancelar as rotinas dele neste PC?",
                "Artho AI", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            await DeviceAction(async () =>
            {
                await Task.Run(() => PcDeviceService.Remove(pc.Id));
                ShowDeviceNotice("Pareamento removido. Para revogar o acesso no PC de destino, desative o controle nele.");
                await RefreshDevicesAsync();
            });
        });
        return panel;

        void AddText(string text, string style = "BodyCopy") => body.Children.Add(new TextBlock
        {
            Text = text, Style = (Style)FindResource(style), Margin = new Thickness(0, 0, 0, 4)
        });
        void AddButton(string title, Func<Task> run)
        {
            var button = new Button { Content = title, Margin = new Thickness(0, 0, 8, 4) };
            button.Click += async (_, _) => { button.IsEnabled = false; try { await run(); } finally { button.IsEnabled = true; } };
            actions.Children.Add(button);
        }
    }

    private static string DescribeRule(WeeklyRule? rule)
    {
        if (rule is null) return "sem horário";
        string[] labels = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];
        return $"às {rule.Time} ({string.Join(", ", Enumerable.Range(0, 7).Where(day => rule.Includes((DayOfWeek)day)).Select(day => labels[day]))})";
    }

    private async void DevicesPair_Click(object sender, RoutedEventArgs e)
    {
        if (demo) return;
        DevicePair.IsEnabled = false;
        string code = DeviceInviteInput.Text;
        ShowDeviceNotice("Validando o pareamento…");
        await DeviceAction(async () =>
        {
            var result = await Task.Run(() => PcDeviceService.PairAsync(code));
            DeviceInviteInput.Clear();
            ShowDeviceNotice($"{result.Name} foi pareado.");
            await RefreshDevicesAsync();
        });
        DevicePair.IsEnabled = true;
    }

    private void ShowDeviceNotice(string text)
    {
        DeviceNotice.Text = text;
        DeviceNotice.Visibility = Visibility.Visible;
    }

    private async Task DeviceAction(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { ShowDeviceNotice(ex.Message); }
    }

    private async Task RestorePcAgentAsync()
    {
        if (demo) return;
        agentBusy = true;
        try
        {
            var settings = await Task.Run(() => PcStore.Read().Agent);
            if (settings?.Enabled != true) return;
            await StartPcAgentAsync(settings);
            PcAgentNotice.Text = "Controle ativo na rede local. Gere um código para autorizar outro PC.";
        }
        catch (Exception ex) { PcAgentNotice.Text = $"O controle não iniciou. {ex.Message}"; }
        finally { agentBusy = false; UpdateAgentButtons(); }
    }

    private async Task StartPcAgentAsync(PcAgentSettings settings)
    {
        var started = await Task.Run(() => PcAgent.StartAsync(settings));
        if (!IsLoaded) { await started.DisposeAsync(); return; }
        pcAgent = started;
    }

    private async void PcAgentCode_Click(object sender, RoutedEventArgs e)
    {
        if (demo || agentBusy || PcAgentNetwork.SelectedItem is not LanAdapter adapter) return;
        if (pcAgent is null && MessageBox.Show(
            "O código autoriza outro PC da rede a consultar este computador e solicitar o desligamento. Permitir controle?",
            "Artho AI", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        agentBusy = true;
        UpdateAgentButtons();
        try
        {
            if (pcAgent is null)
            {
                var previous = await Task.Run(() => PcStore.Read().Agent);
                var configured = await Task.Run(() => PcAgent.Configure(adapter, previous));
                await StartPcAgentAsync(configured);
                if (pcAgent is null) return;
                await Task.Run(() => PcStore.Update(state => state.Agent = configured));
            }
            Clipboard.SetText(pcAgent.NewInvite());
            PcAgentNotice.Text = "Código copiado. Envie apenas ao PC de controle. Válido por 3 minutos e por um pareamento.";
        }
        catch (Exception ex) { PcAgentNotice.Text = $"Não foi possível gerar o código. {ex.Message}"; }
        finally { agentBusy = false; UpdateAgentButtons(); }
    }

    private async void PcAgentOff_Click(object sender, RoutedEventArgs e)
    {
        if (demo || agentBusy) return;
        agentBusy = true;
        UpdateAgentButtons();
        try
        {
            if (pcAgent is not null) await Task.Run(pcAgent.Revoke);
            else await Task.Run(() => PcStore.Update(state =>
                state.Agent = state.Agent is null ? null : state.Agent with { Enabled = false, ControllerToken = null }));
            await StopPcAgentAsync();
            PcAgentNotice.Text = "Controle desativado. A autorização anterior foi revogada.";
        }
        catch (Exception ex) { PcAgentNotice.Text = $"Não foi possível desativar. {ex.Message}"; }
        finally { agentBusy = false; UpdateAgentButtons(); }
    }

    private async Task StopPcAgentAsync()
    {
        var active = pcAgent;
        pcAgent = null;
        if (active is not null) await active.DisposeAsync();
    }

    private void UpdateAgentButtons()
    {
        PcAgentCode.IsEnabled = !demo && !agentBusy && PcAgentNetwork.SelectedItem is not null;
        PcAgentOff.IsEnabled = !demo && !agentBusy;
        PcAgentNetwork.IsEnabled = !agentBusy && pcAgent is null;
    }
}
