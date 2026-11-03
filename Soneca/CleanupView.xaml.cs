using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Text;

namespace Soneca;
public partial class CleanupView : UserControl
{
    public bool Demo { get; set; }
    public bool Busy { get; private set; }
    private CleanPlan? temp, prefetch;
    private BinInfo? bin;
    private bool analyzed;
    public CleanupView() => InitializeComponent();
    private void Selection_Changed(object sender, RoutedEventArgs e) { analyzed = false; CleanButton.IsEnabled = false; TotalLabel.Text = "—"; SummaryLabel.Text = "Seleção alterada. Analise novamente antes de limpar."; }
    public void SetEnabled(bool enabled) { IsEnabled = enabled; }
    private void SetBusy(bool value)
    {
        Busy = value; TempCheck.IsEnabled = PrefetchCheck.IsEnabled = BinCheck.IsEnabled = AnalyzeButton.IsEnabled = !value;
        CleanButton.IsEnabled = !value && analyzed;
    }
    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        bool useTemp = TempCheck.IsChecked == true, usePrefetch = PrefetchCheck.IsChecked == true, useBin = BinCheck.IsChecked == true;
        if (!useTemp && !usePrefetch && !useBin) { ResultLabel.Text = "Selecione pelo menos uma categoria."; return; }
        SetBusy(true); ResultLabel.Text = "Analisando… Nenhum arquivo será removido nesta etapa.";
        try
        {
            await Task.Run(() =>
            {
                temp = useTemp ? (Demo ? new("Demonstração", false, [new("exemplo.tmp", 48000000, 0)], 2, false) : Cleaner.ScanTemp()) : null;
                prefetch = usePrefetch ? (Demo ? new("Demonstração", true, [new("exemplo.pf", 12000000, 0)], 0, false) : Cleaner.ScanPrefetch()) : null;
                bin = useBin ? (Demo ? new(8, 25000000) : Cleaner.QueryBin()) : null;
            });
            TempLabel.Text = Describe(temp); PrefetchLabel.Text = Describe(prefetch);
            BinLabel.Text = bin is null ? "Não selecionada" : bin.Error ?? $"{bin.Items} itens · {Cleaner.Size(bin.Bytes)}";
            long total = (temp?.Bytes ?? 0) + (prefetch?.Bytes ?? 0) + (bin?.Bytes ?? 0);
            TotalLabel.Text = Cleaner.Size(total);
            SummaryLabel.Text = prefetch?.Protected == true ? "Prévia parcial: o Prefetch será analisado e limpo após a autorização do Windows." : "Estimativa dos itens selecionados. Arquivos em uso ou alterados serão ignorados.";
            analyzed = true; ResultLabel.Text = Demo ? "Demonstração: a limpeza será apenas simulada." : "Análise concluída. Confira as categorias antes de limpar.";
        }
        catch (Exception ex) { analyzed = false; ResultLabel.Text = "Não foi possível analisar: " + ex.Message; }
        finally { SetBusy(false); }
    }
    private static string Describe(CleanPlan? plan) => plan is null ? "Não selecionado" : plan.Protected ? "Acesso protegido · será solicitada autorização ao limpar" : $"{plan.Files.Count} arquivos · {Cleaner.Size(plan.Bytes)}" + (plan.Skipped > 0 ? $" · {plan.Skipped} itens ignorados" : "");
    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        if (!analyzed || Busy) return;
        SetBusy(true);
        string categories = string.Join("\n", new[] { temp is not null ? "• Arquivos de %temp%" : null, prefetch is not null ? "• Cache .pf do Prefetch" : null, bin is not null ? "• Todo o conteúdo da sua lixeira" : null }.Where(s => s is not null));
        if (MessageBox.Show(Window.GetWindow(this), (Demo ? "SIMULAÇÃO\n\n" : "") + "Limpar estas categorias?\n\n" + categories + "\n\nA exclusão é permanente. Arquivos em uso ou modificados serão ignorados." + (prefetch is not null && !Demo && !Cleaner.IsAdministrator ? "\nO Windows pedirá autorização para o Prefetch." : ""), "Artho Control · confirmar limpeza", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) { SetBusy(false); return; }
        SetBusy(true); ResultLabel.Text = "Limpando…";
        try
        {
            var messages = new StringBuilder();
            void Add(string name, CleanResult result) => messages.AppendLine($"{name}: {result.Deleted} arquivos removidos · {Cleaner.Size(result.Bytes)} · {result.Skipped} ignorados." + (result.Error is null ? "" : " " + result.Error));
            if (temp is not null) Add("Temporários", Demo ? new(1, 2, temp.Bytes) : await Task.Run(() => Cleaner.CleanTemp(temp)));
            if (prefetch is not null) Add("Prefetch", Demo ? new(1, 0, prefetch.Bytes) : await Cleaner.CleanPrefetch(prefetch));
            if (bin is not null)
            {
                if (Demo) messages.AppendLine("Lixeira: esvaziamento simulado.");
                else if (bin.Error is not null) messages.AppendLine("Lixeira: análise indisponível; não foi esvaziada.");
                else
                {
                    var before = Cleaner.QueryBin();
                    string? error = before.Error ?? (before.Items == 0 ? null : await Task.Run(() => Cleaner.EmptyBin(IntPtr.Zero)));
                    var after = Cleaner.QueryBin();
                    messages.AppendLine(error ?? (after.Error is not null ? "Lixeira: comando enviado; não foi possível conferir o resultado." : after.Items == 0 ? "Lixeira esvaziada." : "Lixeira parcialmente esvaziada; alguns itens permaneceram."));
                }
            }
            ResultLabel.Text = (Demo ? "SIMULAÇÃO CONCLUÍDA\n" : "LIMPEZA CONCLUÍDA\n") + messages;
            SummaryLabel.Text = "Analise novamente para atualizar o espaço disponível.";
        }
        catch (Exception ex) { ResultLabel.Text = "A limpeza foi interrompida: " + ex.Message + " Os itens já removidos não são restaurados."; }
        finally { analyzed = false; SetBusy(false); }
    }
}
