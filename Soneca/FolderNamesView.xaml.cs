using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Soneca;
public partial class FolderNamesView : UserControl
{
    private FolderListing? listing;
    private string? loadedRoot;
    public bool Busy { get; private set; }
    public FolderNamesView() => InitializeComponent();
    private async void Choose_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Escolha a pasta para listar os nomes", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) { FolderInput.Text = dialog.FolderName; await LoadFolder(); }
    }
    private async void Read_Click(object sender, RoutedEventArgs e) => await LoadFolder();
    private async Task LoadFolder()
    {
        if (Busy) return;
        bool recursive = RecursiveCheck.IsChecked == true;
        string root = FolderInput.Text.Trim().Trim('"');
        Busy = true; IsEnabled = false; StatusLabel.Text = "Lendo nomes…";
        try
        {
            var result = await Task.Run(() => FolderNames.Read(root, recursive));
            listing = result; loadedRoot = Path.GetFullPath(root); Render();
        }
        catch (Exception ex) { listing = null; Preview.Clear(); CopyButton.IsEnabled = SaveButton.IsEnabled = false; StatusLabel.Text = "Não foi possível ler: " + ex.Message; }
        finally { Busy = false; IsEnabled = true; }
    }
    private void Render()
    {
        if (listing is null) return;
        Preview.Text = FolderNames.Format(listing.Entries, KindBox.SelectedIndex, ExtensionsCheck.IsChecked == true, NumberedCheck.IsChecked == true, OrderBox.SelectedIndex);
        int count = listing.Entries.Count(e => KindBox.SelectedIndex == 0 || (KindBox.SelectedIndex == 1 ? !e.Directory : e.Directory));
        StatusLabel.Text = $"{count} nomes · {loadedRoot}" + (listing.Skipped > 0 ? $" · {listing.Skipped} itens sem acesso ignorados" : "");
        CopyButton.IsEnabled = SaveButton.IsEnabled = Preview.Text.Length > 0;
    }
    private void Options_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) Render(); }
    private void Recursive_Changed(object sender, RoutedEventArgs e) { listing = null; Preview.Clear(); CopyButton.IsEnabled = SaveButton.IsEnabled = false; StatusLabel.Text = "Clique em Ler pasta para aplicar a inclusão de subpastas."; }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Preview.Text); StatusLabel.Text = "Lista copiada! Cole onde quiser com Ctrl+V."; }
        catch { StatusLabel.Text = "A área de transferência está ocupada. Tente copiar novamente."; }
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Salvar lista de nomes", Filter = "Arquivo de texto (*.txt)|*.txt", FileName = "Nomes da pasta.txt" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { File.WriteAllText(dialog.FileName, Preview.Text, new System.Text.UTF8Encoding(true)); StatusLabel.Text = "Lista salva em " + dialog.FileName; }
        catch (Exception ex) { StatusLabel.Text = "Não foi possível salvar: " + ex.Message; }
    }
}
