using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Soneca;

public partial class MainWindow
{
    private readonly DispatcherTimer dashboardTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
    private PcPerformanceSampler? dashboardSampler;
    private bool samplingDashboard;

    private void ConfigureDashboard()
    {
        dashboardTimer.Tick += async (_, _) => await SampleDashboardAsync();
        Loaded += (_, _) => UpdateDashboardSampling();
        HomePanel.IsVisibleChanged += (_, _) => UpdateDashboardSampling();
        DashboardContent.IsEnabledChanged += (_, _) => UpdateDashboardSampling();
        StateChanged += (_, _) => UpdateDashboardSampling();
        Closed += (_, _) =>
        {
            dashboardTimer.Stop();
            dashboardSampler = null;
        };
    }

    private void UpdateDashboardSampling()
    {
        if (!IsLoaded || !HomePanel.IsVisible || !DashboardContent.IsEnabled || WindowState == WindowState.Minimized)
        {
            dashboardTimer.Stop();
            dashboardSampler = null;
            PerformanceStatus.Text = "Atualização pausada";
            return;
        }
        if (dashboardSampler is not null) return;
        dashboardSampler = new PcPerformanceSampler();
        PerformanceStatus.Text = "Lendo o estado do PC…";
        dashboardTimer.Start();
        _ = SampleDashboardAsync();
    }

    private async Task SampleDashboardAsync()
    {
        if (samplingDashboard || dashboardSampler is not { } sampler) return;
        samplingDashboard = true;
        try
        {
            var reading = await Task.Run(sampler.Read);
            if (dashboardSampler == sampler) RenderDashboard(reading);
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            if (dashboardSampler == sampler) PerformanceStatus.Text = "Aguardando nova leitura";
        }
        finally { samplingDashboard = false; }
    }

    private void RenderDashboard(PcPerformance reading)
    {
        var local = reading.Computer;
        ComputerNameLabel.Text = local.Name;
        ComputerDetailsLabel.Text = $"Windows · compilação {Environment.OSVersion.Version.Build}";
        UptimeValue.Text = local.Uptime.Days > 0
            ? $"Ligado há {local.Uptime.Days} d {local.Uptime.Hours} h"
            : $"Ligado há {local.Uptime.Hours} h {local.Uptime.Minutes} min";
        PerformanceStatus.Text = $"Atualizado às {DateTime.Now:HH:mm:ss}";

        ProcessorDetails.Text = reading.ProcessorName ?? $"{local.ProcessorCount} processadores lógicos";
        ProcessorDetails.ToolTip = ProcessorDetails.Text;
        ProcessorScope.Text = reading.ProcessorGroups > 1 ? "Grupo de processadores atual" : $"{local.ProcessorCount} lógicos";
        SetGauge(ProcessorValue, ProcessorGauge, reading.ProcessorLoad);

        double? memoryLoad = local.TotalMemoryGb > 0 ? 100 * local.MemoryInUseGb / local.TotalMemoryGb : null;
        MemoryDetails.Text = memoryLoad.HasValue
            ? $"{Number(local.MemoryInUseGb)} de {Number(local.TotalMemoryGb)} GB em uso" : "Leitura indisponível";
        SetGauge(MemoryValue, MemoryGauge, memoryLoad);

        DiskLabel.Text = reading.Disk is { } volume ? $"Disco do Windows ({volume.Volume})" : "Disco do Windows";
        DiskDetails.Text = reading.Disk is { } disk
            ? $"{Number(disk.Free / 1073741824d)} GB livres de {Number(disk.Total / 1073741824d)} GB" : "Leitura indisponível";
        SetGauge(DiskValue, DiskGauge, reading.Disk is { } space ? 100d * space.Used / space.Total : null);

        NetworkDetails.Text = reading.Network;
        NetworkReceived.Text = reading.Traffic is { } traffic ? Rate(traffic.Received) : "—";
        NetworkSent.Text = reading.Traffic is { } outgoing ? Rate(outgoing.Sent) : "—";
        NetworkStatusLabel.Text = local.NetworkAvailable ? "Rede disponível" : "Rede indisponível";
        NetworkIndicator.Fill = (System.Windows.Media.Brush)FindResource(local.NetworkAvailable ? "Accent" : "Muted");
    }

    private static void SetGauge(TextBlock label, ProgressBar gauge, double? load)
    {
        label.Text = load is { } value ? value.ToString("0", BrazilianPortuguese) + "%" : "—";
        gauge.Opacity = load.HasValue ? 1 : .45;
        gauge.Value = load ?? 0;
    }

    private static string Number(double value) => value.ToString("0.#", BrazilianPortuguese);
    private static string Rate(double bytes) => bytes >= 1_000_000
        ? $"{Number(bytes / 1_000_000)} MB/s"
        : $"{Number(bytes / 1000)} KB/s";
}
