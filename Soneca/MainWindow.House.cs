using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Soneca;

public partial class MainWindow
{
    private WebView2? houseView;
    private string? unlockedHouse;
    private DateTime houseRevision;
    private int houseGeneration;
    private bool attemptedSavedHouseUnlock;

    private void House_Click(object sender, RoutedEventArgs e) => Navigate(7);

    private void ShowHouse(bool visible)
    {
        HousePage.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) CloseHouse();
        else if (houseView is null) OpenOrUnlockHouse();
    }

    private void OpenOrUnlockHouse()
    {
        if (unlockedHouse is null || File.GetLastWriteTimeUtc(HousePackage.PathOnDisk) != houseRevision)
        {
            unlockedHouse = null;
            ShowLockedHouse();
            if (!attemptedSavedHouseUnlock && File.Exists(HousePackage.PathOnDisk) && HouseCredential.Exists(demo))
                _ = UnlockHouseAsync(remembered: true);
            return;
        }
        HouseLock.Visibility = Visibility.Collapsed;
        _ = OpenHouseAsync(unlockedHouse);
    }

    private void ShowLockedHouse(string? message = null)
    {
        bool available = File.Exists(HousePackage.PathOnDisk);
        HouseLock.Visibility = Visibility.Visible;
        HouseFeedback.Text = message ?? (available ? "Digite a senha da maquete." : "Ainda não há uma maquete neste PC.");
        HousePassword.Visibility = HouseUnlock.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        HouseUnlock.IsEnabled = true;
    }

    private void HouseUnlock_Click(object sender, RoutedEventArgs e) => _ = UnlockHouseAsync();

    private void HousePassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = UnlockHouseAsync();
    }

    private async Task UnlockHouseAsync(bool remembered = false)
    {
        if (!HouseUnlock.IsEnabled || houseView is not null || (!remembered && HousePassword.Password.Length == 0)) return;
        HouseUnlock.IsEnabled = false;
        attemptedSavedHouseUnlock = true;
        string entered = HousePassword.Password;
        HousePassword.Clear();
        int opening = ++houseGeneration;
        HouseFeedback.Text = "Abrindo maquete…";
        try
        {
            var revision = File.GetLastWriteTimeUtc(HousePackage.PathOnDisk);
            var result = await Task.Run(() =>
            {
                string? password = remembered ? HouseCredential.Read(demo) : entered;
                if (string.IsNullOrEmpty(password)) return (Html: (string?)null, Saved: false);
                string html = HousePackage.Unlock(password);
                if (remembered) return (Html: (string?)html, Saved: true);
                try
                {
                    HouseCredential.Save(password, demo);
                    return (Html: (string?)html, Saved: true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
                {
                    return (Html: (string?)html, Saved: false);
                }
            });
            if (opening != houseGeneration) return;
            if (result.Html is null)
            {
                ShowLockedHouse("Digite a senha para recuperar o acesso neste aparelho.");
                return;
            }
            if (!result.Saved)
            {
                MessageBox.Show(this, "A maquete abriu, mas o Windows não conseguiu lembrar o acesso para a próxima vez.",
                    "Minha casa", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            houseRevision = revision;
            unlockedHouse = result.Html;
            await OpenHouseAsync(result.Html);
        }
        catch (CryptographicException)
        {
            if (opening == houseGeneration) ShowLockedHouse(remembered ? "Digite a senha atual da maquete."
                : "Senha incorreta ou arquivo da maquete inválido.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (opening == houseGeneration) ShowLockedHouse("Não foi possível ler a maquete deste PC.");
        }
    }

    private async Task OpenHouseAsync(string page)
    {
        var view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(9, 15, 26) };
        houseView = view;
        try
        {
            var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Soneca", demo ? "BootPreview" : "Boot");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
            if (houseView != view) return;
            HouseSurface.Content = view;
            await view.EnsureCoreWebView2Async(environment);
            if (houseView != view) return;

            var browser = view.CoreWebView2;
            browser.Settings.AreHostObjectsAllowed = false;
            browser.Settings.IsWebMessageEnabled = false;
            browser.Settings.AreDevToolsEnabled = false;
            browser.Settings.AreDefaultContextMenusEnabled = false;
            browser.Settings.AreBrowserAcceleratorKeysEnabled = false;
            browser.Settings.IsZoomControlEnabled = false;
            browser.Settings.IsGeneralAutofillEnabled = false;
            browser.Settings.IsPasswordAutosaveEnabled = false;
            bool openingPage = true;
            browser.NavigationStarting += (_, args) =>
            {
                bool allowed = openingPage &&
                    args.Uri.StartsWith("data:text/html;charset=utf-8;base64,", StringComparison.Ordinal);
                if (allowed) openingPage = false;
                args.Cancel = !allowed;
            };
            browser.NewWindowRequested += (_, args) => args.Handled = true;
            browser.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            foreach (string pattern in new[] { "http://*", "https://*" })
                browser.AddWebResourceRequestedFilter(pattern, CoreWebView2WebResourceContext.All,
                    CoreWebView2WebResourceRequestSourceKinds.All);
            browser.WebResourceRequested += (_, args) =>
                args.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "");
            browser.NavigationCompleted += (_, args) =>
            {
                if (houseView != view) return;
                if (!args.IsSuccess) QueueHouseFailure(view);
                else HouseLock.Visibility = Visibility.Collapsed;
            };
            browser.ProcessFailed += (_, _) => QueueHouseFailure(view);
            if (houseView == view) browser.NavigateToString(page);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Minha casa: " + ex.Message);
            QueueHouseFailure(view);
        }
    }

    private void QueueHouseFailure(WebView2 view) => Dispatcher.BeginInvoke(() =>
    {
        if (houseView != view) return;
        CloseHouse();
        unlockedHouse = null;
        ShowLockedHouse("Não foi possível abrir a maquete. Tente novamente.");
    });

    private void CloseHouse()
    {
        var view = houseView;
        houseGeneration++;
        attemptedSavedHouseUnlock = false;
        HousePassword.Clear();
        houseView = null;
        HouseSurface.Content = null;
        view?.Dispose();
    }

    private void EndHouseSession()
    {
        unlockedHouse = null;
        attemptedSavedHouseUnlock = false;
        CloseHouse();
    }
}
