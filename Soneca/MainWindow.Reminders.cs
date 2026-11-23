using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Soneca;

public partial class MainWindow
{
    private readonly DispatcherTimer reminderTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private List<PlanReminder> reminders = [];
    private Guid? editingReminder;
    private DateOnly renderedOn;

    private void ConfigureReminders()
    {
        try
        {
            reminders = demo
                ? [PlanReminder.Create("conta@exemplo.com", DateOnly.FromDateTime(DateTime.Today.AddDays(5)), 7, null)]
                : ReminderStore.Load();
        }
        catch (Exception error)
        {
            PlanFeedback.Text = "Não foi possível carregar os lembretes. " + error.Message;
            PlanSave.IsEnabled = false;
            return;
        }
        RenderReminders();
        if (demo) return;
        reminderTimer.Tick += (_, _) => CheckReminders();
        reminderTimer.Start();
        Loaded += (_, _) => CheckReminders();
        Activated += (_, _) => CheckReminders();
    }

    private void Plans_Click(object sender, RoutedEventArgs e)
    {
        Navigate(8);
        RenderReminders();
    }

    private void SavePlan_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var existing = reminders.Find(item => item.Id == editingReminder);
            DateOnly? renewal = null;
            string dateText = PlanDate.Text.Trim();
            if (dateText.Length != 0)
            {
                if (!DateOnly.TryParseExact(dateText, "dd/MM/yyyy", BrazilianPortuguese, DateTimeStyles.None, out var date))
                    throw new ArgumentException("Digite a data no formato dia/mês/ano ou deixe em branco.");
                renewal = date;
            }
            int lead = existing?.LeadDays ?? 7;
            if (!string.IsNullOrWhiteSpace(PlanLead.Text) && !int.TryParse(PlanLead.Text, out lead))
                throw new ArgumentException("Digite a antecedência em dias.");
            var row = PlanReminder.Create(PlanEmail.Text, renewal, lead, existing);
            if (reminders.Any(item => item.Id != row.Id && string.Equals(item.Email, row.Email, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Esse e-mail já está na lista. Use Editar para mudar a data.");
            SaveReminders(reminders.Where(item => item.Id != row.Id).Append(row).ToList());
            ClearPlanForm();
            RenderReminders();
            PlanFeedback.Text = "Conta salva.";
            CheckReminders();
        }
        catch (Exception error) { PlanFeedback.Text = error.Message; }
    }

    private void NewPlan_Click(object sender, RoutedEventArgs e) => ClearPlanForm();

    private void ClearPlanForm()
    {
        editingReminder = null;
        PlanEmail.Clear();
        PlanDate.Clear();
        PlanLead.Text = "7";
        PlanFormTitle.Text = "Adicionar conta";
        PlanEmail.Focus();
    }

    private void SaveReminders(List<PlanReminder> entries)
    {
        if (!demo) ReminderStore.Save(entries);
        reminders = entries;
    }

    private void RenderReminders()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        renderedOn = today;
        PlanRows.Children.Clear();
        PlanEmpty.Visibility = reminders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        int upcoming = reminders.Count(item => item.DaysLeft(today) is int days && days >= 0 && days <= item.LeadDays);
        PlanNav.Content = upcoming == 0 ? "Assinaturas" : $"Assinaturas ({upcoming})";
        foreach (var item in reminders.OrderBy(entry => entry.Renewal is null).ThenBy(entry => entry.Renewal).ThenBy(entry => entry.Email))
        {
            var body = new StackPanel();
            PlanRows.Children.Add(new Border { Style = (Style)FindResource("Panel"), Child = body,
                Padding = new Thickness(16, 12, 16, 10), Margin = new Thickness(0, 0, 0, 8) });
            body.Children.Add(new TextBlock { Text = item.Email, Style = (Style)FindResource("SectionTitle"), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = item.Countdown(today), FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 4, 0, 2),
                Foreground = (System.Windows.Media.Brush)FindResource(item.DaysLeft(today) <= item.LeadDays ? "Accent" : "Ink") });
            if (item.Renewal is DateOnly renewal)
                body.Children.Add(new TextBlock { Text = $"{renewal:dd/MM/yyyy}  ·  Antecedência de {item.LeadDays} dia(s)", Style = (Style)FindResource("BodyCopy") });
            var actions = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            body.Children.Add(actions);
            var edit = new Button { Content = "Editar", Margin = new Thickness(0, 0, 6, 0) };
            edit.Click += (_, _) =>
            {
                editingReminder = item.Id;
                PlanEmail.Text = item.Email;
                PlanDate.Text = item.Renewal?.ToString("dd/MM/yyyy", BrazilianPortuguese) ?? "";
                PlanLead.Text = item.LeadDays.ToString(BrazilianPortuguese);
                PlanFormTitle.Text = "Editar conta";
                PlanDate.Focus();
            };
            actions.Children.Add(edit);
            var remove = new Button { Content = "Remover" };
            remove.Click += (_, _) =>
            {
                if (MessageBox.Show(this, $"Remover o lembrete de {item.Email}?", "Assinaturas",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                try
                {
                    SaveReminders(reminders.Where(entry => entry.Id != item.Id).ToList());
                    if (editingReminder == item.Id) ClearPlanForm();
                    RenderReminders();
                    PlanFeedback.Text = "Lembrete removido.";
                }
                catch (Exception error) { PlanFeedback.Text = error.Message; }
            };
            actions.Children.Add(remove);
        }
    }

    private void CheckReminders()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (renderedOn != today) RenderReminders();
        if (demo) return;
        var due = reminders.Where(item => item.Due(today)).ToArray();
        if (due.Length == 0) return;
        try
        {
            string text = due.Length == 1 ? $"{due[0].Email} · {due[0].Countdown(today)}"
                : $"{due.Length} contas do ChatGPT perto da renovação. Abra Assinaturas para conferir.";
            ShowPlanNotice(text);
            SaveReminders(reminders.Select(item => item.Due(today) ? item.AfterNotice(today) : item).ToList());
        }
        catch (Exception error) { PlanFeedback.Text = error.Message; }
    }

    private void TestPlanNotice_Click(object sender, RoutedEventArgs e)
    {
        try { ShowPlanNotice("Teste de aviso de renovação do ChatGPT."); }
        catch (Exception error) { PlanFeedback.Text = error.Message; }
    }

    private void ShowPlanNotice(string text)
    {
        PlanBannerText.Text = text;
        PlanBanner.Visibility = Visibility.Visible;
        if (!demo) trayIcon?.ShowBalloonTip(8000, "Renovação do ChatGPT", text, System.Windows.Forms.ToolTipIcon.Info);
    }

    private void DismissPlanBanner_Click(object sender, RoutedEventArgs e) => PlanBanner.Visibility = Visibility.Collapsed;

    private void OpenPlanBilling_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://chatgpt.com/#settings/Billing") { UseShellExecute = true }); }
        catch (Exception error) { PlanFeedback.Text = error.Message; }
    }

    private void CloseReminders() => reminderTimer.Stop();
}
