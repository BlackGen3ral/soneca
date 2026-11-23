using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Soneca;

public partial class MainWindow
{
    private ReleaseUpdate? availableUpdate;
    private DownloadedUpdate? downloadedUpdate;
    private CancellationTokenSource? downloadCancellation;
    private bool updateBusy;
    private bool launchingInstaller;

    private void UpdatesNav_Click(object sender, RoutedEventArgs e) => Navigate(6);

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (updateBusy) return;
        if (downloadedUpdate is not null) await ApplyUpdateAsync();
        else if (availableUpdate is not null) await DownloadUpdateAsync(availableUpdate);
        else await CheckUpdateAsync();
    }

    private async Task CheckUpdateAsync()
    {
        if (updateBusy) return;
        if (demo)
        {
            UpdateLabel.Text = "A consulta ao GitHub está desativada no modo de demonstração.";
            return;
        }
        SetUpdateBusy(true);
        UpdateHeading.Text = "Consultando o GitHub";
        UpdateLabel.Text = "Verificando a última versão publicada…";
        try
        {
            availableUpdate = await Updater.Check();
            UpdateCheckedAt.Text = $"Última consulta · {DateTime.Now:dd/MM 'às' HH:mm}";
            if (availableUpdate is { } update)
            {
                UpdateHeading.Text = $"Versão {update.Version.ToString(3)} disponível";
                UpdateLabel.Text = $"{update.Size / (1024d * 1024):0.#} MB para baixar. O Artho AI vai reabrir após a instalação.";
                HomeUpdateText.Text = UpdateHeading.Text;
                HomeUpdateBanner.Visibility = Visibility.Visible;
                UpdatesNav.Content = "Atualizações •";
                UpdateNotes.Visibility = Visibility.Visible;
                UpdateButton.Content = "Baixar atualização";
            }
            else
            {
                UpdateHeading.Text = "Você está em dia";
                UpdateLabel.Text = "A versão instalada é a mais recente do canal Windows.";
            }
        }
        catch (Exception)
        {
            UpdateHeading.Text = "Não deu para verificar";
            UpdateLabel.Text = "Confira a conexão com o GitHub e tente novamente.";
        }
        finally { SetUpdateBusy(false); }
    }

    private async Task DownloadUpdateAsync(ReleaseUpdate release)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        downloadCancellation = cancellation;
        SetUpdateBusy(true);
        UpdateHeading.Text = $"Baixando {release.Version.ToString(3)}";
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateCancel.Visibility = Visibility.Visible;
        UpdateLabel.Text = "Preparando o download…";
        var progress = new Progress<int>(percent =>
        {
            if (downloadCancellation != cancellation || cancellation.IsCancellationRequested) return;
            UpdateProgress.Value = percent;
            UpdateLabel.Text = percent == 100 ? "Verificando a integridade…" : $"{percent}% baixado";
        });
        try
        {
            downloadedUpdate = await Updater.Download(release, progress, cancellation.Token);
            UpdateHeading.Text = "Pronto para instalar";
            UpdateLabel.Text = "Download verificado. O Artho AI vai fechar durante a instalação e abrir novamente.";
            UpdateButton.Content = "Instalar e reabrir";
        }
        catch (OperationCanceledException)
        {
            UpdateHeading.Text = "Download interrompido";
            UpdateLabel.Text = "Use Baixar atualização para tentar de novo.";
        }
        catch (Exception ex)
        {
            UpdateHeading.Text = "Download não concluído";
            UpdateLabel.Text = ex.Message;
        }
        finally
        {
            downloadCancellation = null;
            UpdateProgress.Visibility = Visibility.Collapsed;
            UpdateCancel.Visibility = Visibility.Collapsed;
            UpdateCancel.IsEnabled = true;
            SetUpdateBusy(false);
        }
    }

    private void UpdateCancel_Click(object sender, RoutedEventArgs e)
    {
        UpdateCancel.IsEnabled = false;
        UpdateLabel.Text = "Cancelando o download…";
        downloadCancellation?.Cancel();
    }

    private async Task ApplyUpdateAsync()
    {
        if (downloadedUpdate is not { } staged || InstallBlocked()) return;
        SetUpdateBusy(true);
        try
        {
            UpdateLabel.Text = "Conferindo o instalador…";
            if (!await Updater.Verify(staged))
            {
                DeleteStaged();
                UpdateHeading.Text = "Baixe a atualização novamente";
                UpdateLabel.Text = "O arquivo baixado mudou ou foi removido.";
                UpdateButton.Content = "Baixar atualização";
                return;
            }
            if (!IsLoaded || InstallBlocked()) return;
            Updater.Install(staged.Path);
            launchingInstaller = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex) { UpdateLabel.Text = "Não foi possível iniciar a instalação. " + ex.Message; }
        finally { SetUpdateBusy(false); }
    }

    private bool InstallBlocked()
    {
        if (CleanupPanel.Busy || NamesPanel.Busy)
        {
            UpdateLabel.Text = "Aguarde a ferramenta em uso terminar antes de instalar.";
            return true;
        }
        try
        {
            if (schedule.Read() > DateTimeOffset.Now)
            {
                UpdateLabel.Text = "Existe um desligamento agendado neste PC. Instale depois que ele terminar ou for cancelado.";
                return true;
            }
            if (suspendSchedule.Read() > DateTimeOffset.Now)
            {
                UpdateLabel.Text = "Existe uma suspensão agendada neste PC. Instale depois que ela terminar ou for cancelada.";
                return true;
            }
        }
        catch (Exception)
        {
            UpdateLabel.Text = "Não foi possível consultar o agendamento do Windows. Tente instalar novamente.";
            return true;
        }
        return false;
    }

    private void UpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is not { } update) return;
        try { using var browser = Process.Start(new ProcessStartInfo(update.Notes.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception) { UpdateLabel.Text = "Não foi possível abrir a página da versão."; }
    }

    private void SetUpdateBusy(bool active)
    {
        updateBusy = active;
        UpdateButton.IsEnabled = !active;
    }

    private void CloseUpdates()
    {
        if (!launchingInstaller) DeleteStaged();
    }

    private void DeleteStaged()
    {
        if (downloadedUpdate is not { } staged) return;
        downloadedUpdate = null;
        try
        {
            File.Delete(staged.Path);
            Directory.Delete(Path.GetDirectoryName(staged.Path)!);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
