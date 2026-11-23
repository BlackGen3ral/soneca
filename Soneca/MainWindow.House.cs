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
    private int houseGeneration;

    private void House_Click(object sender, RoutedEventArgs e) => Navigate(7);

    private void ShowHouse(bool visible)
    {
        HousePage.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) CloseHouse();
        else if (houseView is null) ShowLockedHouse();
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

    private async Task UnlockHouseAsync()
    {
        if (!HouseUnlock.IsEnabled || houseView is not null || HousePassword.Password.Length == 0) return;
        HouseUnlock.IsEnabled = false;
        string password = HousePassword.Password;
        HousePassword.Clear();
        int opening = ++houseGeneration;
        HouseFeedback.Text = "Abrindo maquete…";
        try
        {
            string html = await Task.Run(() => HousePackage.Unlock(password));
            if (opening == houseGeneration) await OpenHouseAsync(html);
        }
        catch (CryptographicException)
        {
            if (opening == houseGeneration) ShowLockedHouse("Senha incorreta ou arquivo da maquete inválido.");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            if (opening == houseGeneration) ShowLockedHouse("Não foi possível ler a maquete deste PC.");
        }
    }

    private async Task OpenHouseAsync(string page)
    {
        var view = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(14, 25, 39) };
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
        ShowLockedHouse("Não foi possível abrir a maquete. Tente novamente.");
    });

    private void CloseHouse()
    {
        var view = houseView;
        houseGeneration++;
        HousePassword.Clear();
        houseView = null;
        HouseSurface.Content = null;
        view?.Dispose();
    }
}
