using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Soneca;

internal sealed class PcRoutineWindow : Window
{
    public WeeklyRule? WakeRule { get; private set; }
    public WeeklyRule? ShutdownRule { get; private set; }

    public PcRoutineWindow(PcCard pc)
    {
        Title = $"Horários · {pc.Name}";
        Width = 565;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = (Brush)FindResource("Canvas");
        var body = new StackPanel { Margin = new Thickness(23) };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = pc.Name, Style = (Style)FindResource("PageTitle") });
        body.Children.Add(new TextBlock
        {
            Text = "Os horários repetem nos dias marcados. Para cancelar uma rotina, desmarque a opção dela.",
            Style = (Style)FindResource("BodyCopy"), Margin = new Thickness(0, 4, 0, 12)
        });
        var wake = Editor("Ligar por Wake-on-LAN", "08:00", pc.Wake, body);
        var shutdown = Editor("Desligar", "18:00", pc.Shutdown, body);
        var error = new TextBlock { Foreground = (Brush)FindResource("Warning"), TextWrapping = TextWrapping.Wrap };
        body.Children.Add(error);
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        body.Children.Add(buttons);
        var save = new Button { Content = "Salvar horários", IsDefault = true, Style = (Style)FindResource("Primary") };
        buttons.Children.Add(save);
        buttons.Children.Add(new Button { Content = "Cancelar", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) });
        save.Click += (_, _) =>
        {
            try
            {
                WakeRule = wake();
                ShutdownRule = shutdown();
                if (WakeRule is not null && ShutdownRule is not null &&
                    WakeRule.Time == ShutdownRule.Time && (WakeRule.Days & ShutdownRule.Days) != 0)
                    throw new ArgumentException("Escolha horários diferentes para ligar e desligar nos mesmos dias.");
                DialogResult = true;
            }
            catch (ArgumentException ex) { error.Text = ex.Message; }
        };
    }

    private Func<WeeklyRule?> Editor(string title, string defaultTime, WeeklyRule? rule, StackPanel host)
    {
        var row = new StackPanel();
        host.Children.Add(new Border { Child = row, Style = (Style)FindResource("Panel"), Margin = new Thickness(0, 0, 0, 10) });
        var enable = new CheckBox { Content = title, IsChecked = rule is not null };
        row.Children.Add(enable);
        var inputs = new StackPanel { IsEnabled = rule is not null, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(inputs);
        enable.Checked += (_, _) => inputs.IsEnabled = true;
        enable.Unchecked += (_, _) => inputs.IsEnabled = false;
        var time = new TextBox
        {
            Text = rule?.Time ?? defaultTime, MaxLength = 5, Width = 108, HorizontalAlignment = HorizontalAlignment.Left
        };
        System.Windows.Automation.AutomationProperties.SetName(time, $"Horário para {title.ToLowerInvariant()}, formato HH:mm");
        inputs.Children.Add(time);
        var days = new WrapPanel { Margin = new Thickness(0, 9, 0, 0) };
        inputs.Children.Add(days);
        string[] labels = ["Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb"];
        var checks = new CheckBox[7];
        for (int day = 0; day < checks.Length; day++)
        {
            var check = new CheckBox
            {
                Content = labels[day], IsChecked = ((rule?.Days ?? 126) & (1 << day)) != 0, Margin = new Thickness(0, 0, 9, 5)
            };
            checks[day] = check;
            days.Children.Add(check);
        }
        return () =>
        {
            if (enable.IsChecked != true) return null;
            if (string.IsNullOrWhiteSpace(time.Text)) throw new ArgumentException($"Preencha o horário para {title.ToLowerInvariant()}.");
            int mask = 0;
            for (int day = 0; day < checks.Length; day++)
                if (checks[day].IsChecked == true) mask |= 1 << day;
            return WeeklyRule.Create(time.Text.Trim(), mask);
        };
    }
}
