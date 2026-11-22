using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace Soneca;

public partial class CleanupView : UserControl
{
    private sealed record CleanupPreview(CleanPlan? Temp, CleanPlan? Dumps, CleanPlan? Prefetch, BinInfo? Bin)
    {
        public IEnumerable<CleanPlan> Eligible => new[] { Temp, Dumps, Prefetch }.OfType<CleanPlan>()
            .Where(plan => !plan.Protected && plan.Error is null && plan.Files.Count > 0);
        public bool IncludesBin => Bin is { Error: null, Items: > 0 };
        public bool CanClean => IncludesBin || Eligible.Any();
    }

    private sealed record PreviewRow(string Name, string Size);
    public bool Demo { get; set; }
    public bool Busy => operation is not null;
    private CleanupPreview? preview;
    private CancellationTokenSource? operation;

    public CleanupView() => InitializeComponent();

    private void Scope_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Busy) return;
        ResetPreview();
        SummaryLabel.Text = "Seleção alterada. Faça uma nova análise.";
        ResultLabel.Text = "";
        UpdateButtons();
    }

    private void ResetPreview()
    {
        preview = null;
        PreviewList.ItemsSource = null;
        Preview.Visibility = Visibility.Collapsed;
        Preview.IsExpanded = false;
        TotalCaption.Text = "Volume estimado";
        TotalLabel.Text = "—";
        SummaryDetail.Text = "";
        TempLabel.Text = Waiting(TempCheck);
        DumpsLabel.Text = Waiting(DumpsCheck);
        PrefetchLabel.Text = Waiting(PrefetchCheck);
        BinLabel.Text = Waiting(BinCheck);
    }

    private static string Waiting(CheckBox check) => check.IsChecked == true ? "Aguardando análise" : "Não selecionado";

    private void UpdateButtons()
    {
        TempCheck.IsEnabled = DumpsCheck.IsEnabled = PrefetchCheck.IsEnabled = BinCheck.IsEnabled = !Busy;
        TempAge.IsEnabled = !Busy && TempCheck.IsChecked == true;
        AnalyzeButton.IsEnabled = !Busy && new[] { TempCheck, DumpsCheck, PrefetchCheck, BinCheck }.Any(check => check.IsChecked == true);
        CleanButton.IsEnabled = !Busy && preview?.CanClean == true;
    }

    private CancellationTokenSource Begin()
    {
        operation = new CancellationTokenSource();
        UpdateButtons();
        AllowCancel(true);
        return operation;
    }

    private void End()
    {
        operation = null;
        AllowCancel(false);
        UpdateButtons();
    }

    private void AllowCancel(bool value)
    {
        CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.IsEnabled = value && operation?.IsCancellationRequested == false;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        operation?.Cancel();
        CancelButton.IsEnabled = false;
        ResultLabel.Text = "Interrompendo…";
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (Busy) return;
        ResetPreview();
        using var run = Begin();
        SummaryLabel.Text = "Analisando categorias";
        ResultLabel.Text = "Nenhum arquivo será removido nesta etapa.";
        try
        {
            bool readingTemp = true;
            var progress = new Progress<int>(count =>
            {
                if (operation == run && readingTemp && !run.IsCancellationRequested)
                    ResultLabel.Text = $"Temporários · {count:N0} itens conferidos";
            });
            int days = int.Parse((string)((ComboBoxItem)TempAge.SelectedItem).Tag);
            var temp = await ScanCategory(TempCheck, CleanupKind.Temp,
                () => Task.Run(() => Cleaner.ScanTemp(days, run.Token, progress)));
            readingTemp = false;
            run.Token.ThrowIfCancellationRequested();

            if (DumpsCheck.IsChecked == true) ResultLabel.Text = "Analisando os dumps de falha…";
            var dumps = await ScanCategory(DumpsCheck, CleanupKind.CrashDumps,
                () => Task.Run(() => Cleaner.ScanCrashDumps(run.Token)));
            run.Token.ThrowIfCancellationRequested();

            if (PrefetchCheck.IsChecked == true) ResultLabel.Text = "Analisando o Prefetch. O Windows pode pedir autorização.";
            var prefetch = await ScanCategory(PrefetchCheck, CleanupKind.Prefetch,
                () => Cleaner.ReadPrefetchAsync(run.Token));
            run.Token.ThrowIfCancellationRequested();

            BinInfo? bin = BinCheck.IsChecked == true ? (Demo ? new BinInfo(8, 25000000) : await Task.Run(Cleaner.QueryBin)) : null;
            run.Token.ThrowIfCancellationRequested();
            preview = new CleanupPreview(temp, dumps, prefetch, bin);
            ShowPreview(preview);
        }
        catch (OperationCanceledException)
        {
            SummaryLabel.Text = "Análise interrompida";
            ResultLabel.Text = "Faça uma nova análise para limpar.";
        }
        catch (Exception ex)
        {
            SummaryLabel.Text = "Análise incompleta";
            ResultLabel.Text = ex.Message;
        }
        finally { End(); }
    }

    private async Task<CleanPlan?> ScanCategory(CheckBox check, CleanupKind kind, Func<Task<CleanPlan>> scan)
    {
        if (check.IsChecked != true) return null;
        if (Demo)
        {
            string sample = kind switch
            {
                CleanupKind.Prefetch => "aplicativo.pf",
                CleanupKind.CrashDumps => "aplicativo.4820.dmp",
                _ => "instalador.tmp"
            };
            long size = kind switch { CleanupKind.Prefetch => 12000000, CleanupKind.CrashDumps => 78000000, _ => 48000000 };
            return new CleanPlan("Demonstração", kind, [new(Path.Combine("Demonstração", sample), size, 0)], 1, kind == CleanupKind.Temp ? 3 : 0);
        }
        try { return await scan(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return new CleanPlan("", kind, [], 0, 0, Error: ex.Message); }
    }

    private void ShowPreview(CleanupPreview report)
    {
        TempLabel.Text = Describe(report.Temp);
        DumpsLabel.Text = Describe(report.Dumps);
        PrefetchLabel.Text = Describe(report.Prefetch);
        BinLabel.Text = report.Bin is { } bin
            ? bin.Error ?? $"{CountLabel(bin.Items, "item", "itens")} · {Cleaner.Size(bin.Bytes)}" : "Não selecionado";
        var files = report.Eligible.ToArray();
        long count = files.Sum(plan => (long)plan.Files.Count) + (report.IncludesBin ? report.Bin!.Items : 0);
        long size = files.Sum(plan => plan.Bytes) + (report.IncludesBin ? report.Bin!.Bytes : 0);
        bool incomplete = new[] { report.Temp, report.Dumps, report.Prefetch }.Any(plan => plan?.Error is not null || plan?.Protected == true)
            || report.Bin?.Error is not null;
        SummaryLabel.Text = count > 0 ? $"{CountLabel(count, "item", "itens")} na prévia" : "Nenhum item elegível";
        SummaryDetail.Text = incomplete ? "A prévia inclui apenas as categorias analisadas com sucesso." : "";
        TotalLabel.Text = Cleaner.Size(size);
        var entries = files.SelectMany(plan => plan.Files.Select(file => (Plan: plan, File: file)))
            .OrderByDescending(item => item.File.Length).Take(15)
            .Select(item => new PreviewRow(NameOf(item.Plan.Kind) + " · " + Path.GetRelativePath(item.Plan.Root, item.File.Path),
                Cleaner.Size(item.File.Length))).ToArray();
        PreviewList.ItemsSource = entries;
        Preview.Visibility = entries.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultLabel.Text = Demo ? "Demonstração com arquivos de exemplo." : "";
    }

    private static string NameOf(CleanupKind kind) => kind switch
    {
        CleanupKind.Prefetch => "Prefetch",
        CleanupKind.CrashDumps => "Dumps de falha",
        _ => "Temporários"
    };

    private static string Describe(CleanPlan? plan)
    {
        if (plan is null) return "Não selecionado";
        if (plan.Error is not null || plan.Protected) return plan.Error ?? "Acesso bloqueado.";
        return $"{CountLabel(plan.Files.Count, "arquivo", "arquivos")} · {Cleaner.Size(plan.Bytes)}"
            + (plan.Recent == 0 ? "" : $" · {CountLabel(plan.Recent, "recente preservado", "recentes preservados")}")
            + (plan.Ignored == 0 ? "" : $" · {CountLabel(plan.Ignored, "ignorado", "ignorados")}");
    }

    private static string CountLabel(long count, string singular, string plural) => $"{count:N0} {(count == 1 ? singular : plural)}";

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (preview is not { CanClean: true } report || Busy) return;
        var categories = report.Eligible
            .Select(plan => $"• {NameOf(plan.Kind)} · {CountLabel(plan.Files.Count, "arquivo", "arquivos")} ({Cleaner.Size(plan.Bytes)})").ToList();
        if (report.IncludesBin) categories.Add("• Todo o conteúdo da sua Lixeira");
        var owner = Window.GetWindow(this);
        string message = (Demo ? "Demonstração\n\n" : "") + "Remover os itens desta prévia?\n\n"
            + string.Join("\n", categories) + "\n\nA remoção é permanente.";
        if (MessageBox.Show(owner, message, "Confirmar limpeza", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        using var run = Begin();
        ResultLabel.Text = "Limpando os itens da prévia…";
        SummaryLabel.Text = "Limpeza em andamento";
        var lines = new List<string>();
        int removed = 0, skipped = 0;
        long bytes = 0;
        try
        {
            foreach (var plan in report.Eligible)
            {
                if (run.IsCancellationRequested) break;
                string name = NameOf(plan.Kind);
                ResultLabel.Text = $"Limpando {name}…";
                AllowCancel(plan.Kind != CleanupKind.Prefetch);
                var progress = new Progress<int>(count =>
                {
                    if (operation == run && !run.IsCancellationRequested && ResultLabel.Text.StartsWith($"Limpando {name}"))
                        ResultLabel.Text = $"Limpando {name} · {count:N0} de {plan.Files.Count:N0} conferidos";
                });
                CleanResult result;
                try
                {
                    result = Demo ? new CleanResult(plan.Files.Count, 0, plan.Bytes)
                        : plan.Kind == CleanupKind.Prefetch ? await Cleaner.CleanPrefetchAsync(plan)
                        : await Task.Run(() => Cleaner.CleanFiles(plan, run.Token, progress));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { result = new CleanResult(0, 0, 0, ex.Message); }
                removed += result.Deleted;
                skipped += result.Skipped;
                bytes += result.Bytes;
                lines.Add($"{name} · {CountLabel(result.Deleted, "removido", "removidos")} · {CountLabel(result.Skipped, "ignorado", "ignorados")}."
                    + (result.Error is not null ? " " + result.Error : ""));
                if (result.Cancelled) break;
            }

            if (report.IncludesBin && !run.IsCancellationRequested)
            {
                AllowCancel(false);
                ResultLabel.Text = "Conferindo se a Lixeira mudou…";
                IntPtr handle = owner is null ? IntPtr.Zero : new WindowInteropHelper(owner).Handle;
                lines.Add(Demo ? "Lixeira esvaziada na demonstração." : await Task.Run(() => Cleaner.EmptyBin(report.Bin!, handle)));
            }
        }
        catch (Exception ex) { lines.Add("Uma etapa foi interrompida. " + ex.Message); }
        finally
        {
            ResetPreview();
            TotalCaption.Text = "Volume removido em arquivos";
            TotalLabel.Text = Cleaner.Size(bytes);
            SummaryLabel.Text = Demo ? "Demonstração concluída" : run.IsCancellationRequested ? "Limpeza interrompida" : "Resultado da limpeza";
            SummaryDetail.Text = $"{CountLabel(removed, "arquivo removido", "arquivos removidos")} · {CountLabel(skipped, "ignorado", "ignorados")}";
            ResultLabel.Text = string.Join("\n", lines);
            End();
        }
    }
}
