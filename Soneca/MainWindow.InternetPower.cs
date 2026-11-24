using System.Windows;
using System.Windows.Threading;

namespace Soneca;

public partial class MainWindow
{
    private readonly DispatcherTimer internetPowerTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private CancellationTokenSource? internetPowerSession;
    private InternetShutdown? internetPowerClient;
    private bool internetPowerPolling;

    private void RestoreInternetPower()
    {
        StopInternetPower();
        try
        {
            var profile = demo ? null : PcStore.Read().Mobile;
            InternetPowerButton.IsEnabled = profile is not null;
            InternetPowerButton.Content = profile?.InternetPower == true
                ? "Desativar desligamento pela internet" : "Ativar desligamento pela internet";
            InternetPowerStatus.Text = profile is null ? "Pareie o celular para ativar." : "Desligamento pela internet desativado.";
            if (profile is not { InternetPower: true }) return;
            internetPowerClient = new InternetShutdown(profile.TokenHash);
            internetPowerSession = new CancellationTokenSource();
            InternetPowerStatus.Text = "Conectando ao serviço…";
            internetPowerTimer.Start();
            _ = PollInternetPowerAsync();
        }
        catch (Exception error) when (error is System.IO.IOException or FormatException or UnauthorizedAccessException)
        {
            InternetPowerButton.IsEnabled = false;
            InternetPowerStatus.Text = "Não foi possível carregar a configuração de acesso.";
        }
    }

    private void InternetPower_Click(object sender, RoutedEventArgs e)
    {
        if (demo) return;
        try
        {
            PcStore.Update(store =>
            {
                if (store.Mobile is not { } profile) return false;
                store.Mobile = profile with
                {
                    InternetPower = !profile.InternetPower,
                    LastInternetShutdown = Math.Max(profile.LastInternetShutdown, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                };
                return true;
            });
            RestoreInternetPower();
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            InternetPowerStatus.Text = "Não foi possível salvar a configuração.";
        }
    }

    private async Task PollInternetPowerAsync()
    {
        if (internetPowerPolling || internetPowerClient is not { } client ||
            internetPowerSession is not { } session) return;
        internetPowerPolling = true;
        try
        {
            var command = await client.PollAsync(session.Token);
            if (internetPowerSession != session) return;
            InternetPowerStatus.Text = "Pronto para receber pedidos do celular pela internet.";
            if (command is null) return;

            var profile = PcStore.Read().Mobile;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (profile is not { InternetPower: true } || !client.MatchesPairing(profile.TokenHash) ||
                !client.CanExecute(command, now, profile.LastInternetShutdown)) return;
            bool accepted = PcStore.Update(store =>
            {
                if (store.Mobile is not { InternetPower: true } current || current.TokenHash != profile.TokenHash ||
                    current.LastInternetShutdown >= command.Issued) return false;
                // Persist before invoking Windows so a repeated request cannot execute after a reboot.
                store.Mobile = current with { LastInternetShutdown = command.Issued };
                return true;
            });
            if (accepted)
                InternetPowerStatus.Text = LocalPower.RunInternetShutdown()
                    ? "Pedido recebido pelo Windows." : "O Windows recusou o desligamento.";
        }
        catch (Exception)
        {
            if (internetPowerSession == session)
                InternetPowerStatus.Text = "Conexão indisponível. O app vai tentar novamente.";
        }
        finally { internetPowerPolling = false; }
    }

    private void StopInternetPower()
    {
        internetPowerTimer.Stop();
        var previous = internetPowerSession;
        internetPowerSession = null;
        internetPowerClient = null;
        previous?.Cancel();
        previous?.Dispose();
    }
}
