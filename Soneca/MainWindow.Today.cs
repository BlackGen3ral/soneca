using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Soneca;

public partial class MainWindow
{
    private void RenderDayPlans(DateOnly today)
    {
        if (remindersUnavailable)
        {
            DayPlans.Text = "Não foi possível carregar os lembretes.";
            return;
        }
        var upcoming = reminders
            .Where(item => item.DaysLeft(today) is int days && days <= item.LeadDays)
            .OrderBy(item => item.Renewal).ToArray();
        if (upcoming.Length == 0)
        {
            DayPlans.Text = "Nenhuma renovação próxima.";
            return;
        }

        var lines = upcoming.Take(3).Select(item => $"{item.Name ?? item.Email} · {item.Countdown(today)}").ToList();
        if (upcoming.Length > 3) lines.Add($"Mais {upcoming.Length - 3} em Assinaturas");
        DayPlans.Text = string.Join(Environment.NewLine, lines);
    }

    private void RenderDayRoutines()
    {
        var lines = new List<string>();
        var now = DateTimeOffset.Now;
        if (target is { } shutdown && shutdown > now)
            lines.Add($"Desligamento em {shutdown.LocalDateTime:dd/MM 'às' HH:mm}");
        if (suspendTarget is { } suspension && suspension > now)
            lines.Add($"Suspensão em {suspension.LocalDateTime:dd/MM 'às' HH:mm}");

        DayDevices.Visibility = Visibility.Collapsed;
        if (!demo)
        {
            try
            {
                var state = PcStore.Read();
                if (state.LocalShutdown is { } weekly)
                    lines.Add($"Desligamento semanal {DescribeRule(weekly)}");
                var failures = state.Computers.Where(pc =>
                    pc.LastRoutine?.EndsWith("Falha ao enviar sinal para ligar", StringComparison.Ordinal) == true ||
                    pc.LastRoutine?.EndsWith("PC não aceitou o desligamento", StringComparison.Ordinal) == true).ToArray();
                lines.AddRange(failures.Take(2).Select(pc => $"{pc.Name} · {pc.LastRoutine}"));
                if (failures.Length > 2) lines.Add($"Mais {failures.Length - 2} em Computadores");
                if (failures.Length > 0) DayDevices.Visibility = Visibility.Visible;
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException or
                System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
            {
                lines.Add("Não foi possível consultar as rotinas dos computadores.");
            }
        }
        DayRoutines.Text = lines.Count == 0 ? "Nenhuma rotina pendente." : string.Join(Environment.NewLine, lines);
    }

    private void DayDevices_Click(object sender, RoutedEventArgs e) => Navigate(5);

    private void ShortcutSearch_Click(object sender, RoutedEventArgs e) => ShowShortcutSearch();

    private void ShowShortcutSearch()
    {
        var search = new TextBox { Margin = new Thickness(0, 8, 0, 10), MaxLength = 100 };
        System.Windows.Automation.AutomationProperties.SetName(search, "Buscar atalho");
        var results = new ListBox
        {
            Background = (System.Windows.Media.Brush)FindResource("Field"),
            Foreground = (System.Windows.Media.Brush)FindResource("Ink"),
            BorderBrush = (System.Windows.Media.Brush)FindResource("Line"),
            DisplayMemberPath = nameof(PersonalShortcut.Label)
        };
        var empty = new TextBlock { Text = "Nenhum atalho encontrado.", Style = (Style)FindResource("BodyCopy") };
        var open = new Button { Content = "Abrir", IsDefault = true, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Digite o que quer abrir", Style = (Style)FindResource("SectionTitle") });
        panel.Children.Add(search);
        panel.Children.Add(results);
        panel.Children.Add(empty);
        panel.Children.Add(open);

        var dialog = new Window
        {
            Owner = this, Title = "Atalhos pessoais", Width = 400, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Content = panel, Background = (System.Windows.Media.Brush)FindResource("Field"),
            Foreground = (System.Windows.Media.Brush)FindResource("Ink")
        };
        PersonalShortcut[] shortcuts =
        [
            new("Abrir maquete", "minha casa 3d editor", () => House_Click(this, new RoutedEventArgs())),
            new("Ver assinaturas", "contas chatgpt renovação lembretes", () => Plans_Click(this, new RoutedEventArgs())),
            new("Programar desligamento", "energia agendar desligar pc", () => { Navigate(0); TimeInput.Focus(); TimeInput.SelectAll(); })
        ];

        PersonalShortcut? chosen = null;
        void OpenSelected()
        {
            if (results.SelectedItem is not PersonalShortcut selection) return;
            chosen = selection;
            dialog.DialogResult = true;
        }
        void Filter()
        {
            var terms = search.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var matches = shortcuts.Where(item => terms.All(word => BrazilianPortuguese.CompareInfo.IndexOf(
                $"{item.Label} {item.Keywords}", word, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)).ToArray();
            results.ItemsSource = matches;
            results.SelectedIndex = matches.Length == 0 ? -1 : 0;
            results.Visibility = matches.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            open.IsEnabled = matches.Length > 0;
        }

        search.TextChanged += (_, _) => Filter();
        open.Click += (_, _) => OpenSelected();
        results.MouseDoubleClick += (_, _) => OpenSelected();
        dialog.Loaded += (_, _) => search.Focus();
        dialog.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) { args.Handled = true; dialog.DialogResult = false; }
            else if (args.Key == Key.Enter && (search.IsKeyboardFocused || results.IsKeyboardFocusWithin))
            { args.Handled = true; OpenSelected(); }
            else if (search.IsKeyboardFocused && (args.Key is Key.Up or Key.Down) && results.Items.Count > 0)
            {
                args.Handled = true;
                results.SelectedIndex = Math.Clamp(results.SelectedIndex + (args.Key == Key.Down ? 1 : -1),
                    0, results.Items.Count - 1);
            }
        };
        Filter();
        if (dialog.ShowDialog() == true) chosen?.Run();
    }

    private sealed record PersonalShortcut(string Label, string Keywords, Action Run);
}
