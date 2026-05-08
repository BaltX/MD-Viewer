using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using MDViewer.Models;
using MDViewer.Services;

namespace MDViewer.Controls;

[System.Runtime.InteropServices.ComVisible(true)]
public sealed class BrowserBridge(ProjectView view)
{
    public void navigate(string url) => view.HandleLinkNavigation(url);
}

public partial class ProjectView : UserControl
{
    private string? _currentFilePath;

    private readonly List<string> _history = new();
    private int _historyIndex = -1;
    private bool _isHistoryNavigation;

    public string? ProjectPath { get; private set; }

    public ProjectView()
    {
        InitializeComponent();
        Browser.ObjectForScripting = new BrowserBridge(this);
        SuppressBrowserScriptErrors();
        ThemeService.ThemeChanged += (_, _) => ReloadCurrentFile();

        FileTree.PreviewMouseWheel += (sender, e) =>
        {
            var sv = FindParent<ScrollViewer>(FileTree);
            if (sv == null) return;
            sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
            e.Handled = true;
        };
    }

    public void LoadProject(ProjectInfo project)
    {
        ProjectPath = project.Path;
        ProjectNameText.Text = project.Name;

        var root = FileTreeItem.CreateRoot(project.Path);
        root.EnsureLoaded();
        FileTree.Items.Clear();
        foreach (var child in root.Children)
            FileTree.Items.Add(child);

        ClearPreview();
    }

    public void ReloadCurrentFile()
    {
        if (_currentFilePath != null)
        {
            _isHistoryNavigation = true;
            OpenFile(_currentFilePath);
            _isHistoryNavigation = false;
        }
    }

    // ------------------------------------------------------------------ //
    //  File opening                                                        //
    // ------------------------------------------------------------------ //
    private void FileTree_ItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: FileTreeItem item })
            item.EnsureLoaded();
    }

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeItem { IsDirectory: false } item)
            OpenFile(item.FullPath);
    }

    private void OpenFile(string path)
    {
        _currentFilePath = path;
        CurrentFilePath.Text = GetRelativePath(path);

        if (!_isHistoryNavigation)
        {
            if (_historyIndex < _history.Count - 1)
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
            if (_history.Count == 0 || _history[_historyIndex] != path)
            {
                _history.Add(path);
                _historyIndex = _history.Count - 1;
            }
        }
        UpdateNavButtons();

        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            ShowPlaceholder("Файл не является Markdown (.md)");
            return;
        }

        try
        {
            var markdown = File.ReadAllText(path);
            var baseDir = Path.GetDirectoryName(path) ?? "";
            var html = MarkdownService.ConvertToHtml(markdown, baseDir);

            WelcomePlaceholder.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
            PrintBtn.IsEnabled = true;
            Browser.NavigateToString(html);
        }
        catch (Exception ex)
        {
            ShowPlaceholder($"Ошибка при открытии файла:\n{ex.Message}");
        }
    }

    // ------------------------------------------------------------------ //
    //  Navigation buttons                                                  //
    // ------------------------------------------------------------------ //
    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex <= 0) return;
        _historyIndex--;
        _isHistoryNavigation = true;
        OpenFile(_history[_historyIndex]);
        _isHistoryNavigation = false;
    }

    private void FwdBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_historyIndex >= _history.Count - 1) return;
        _historyIndex++;
        _isHistoryNavigation = true;
        OpenFile(_history[_historyIndex]);
        _isHistoryNavigation = false;
    }

    private void UpdateNavButtons()
    {
        BackBtn.IsEnabled = _historyIndex > 0;
        FwdBtn.IsEnabled = _historyIndex < _history.Count - 1;
    }

    // ------------------------------------------------------------------ //
    //  Browser navigation                                                  //
    // ------------------------------------------------------------------ //
    // Called from JavaScript via window.external.navigate(href)
    internal void HandleLinkNavigation(string url)
    {
        try
        {
            var uri = new Uri(url);
            if (uri.IsFile)
            {
                var localPath = uri.LocalPath;
                var resolved = ResolveFilePath(localPath);
                if (resolved != null) { ClearTreeSelection(); OpenFile(resolved); }
                else ShowPlaceholder($"Файл не найден:\n{localPath}");
            }
            else
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
        }
        catch { /* ignore */ }
    }

    // Fallback: catches any navigation the JS bridge misses (e.g. form submit, redirects)
    private void Browser_Navigating(object sender, NavigatingCancelEventArgs e)
    {
        if (e.Uri is { IsFile: false, Scheme: not "about" and not "res" })
        {
            e.Cancel = true;
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
            }
            catch { /* ignore */ }
        }
    }

    // ------------------------------------------------------------------ //
    //  Tree context menu                                                   //
    // ------------------------------------------------------------------ //
    private void TreeItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FileTreeItem item, ContextMenu: ContextMenu menu }) return;
        var isFile = !item.IsDirectory;
        // Items: 0=ViewContents, 1=OpenLocation, 2=Separator, 3=Delete
        if (menu.Items[0] is MenuItem mi0) mi0.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        if (menu.Items[2] is Separator sep) sep.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
        if (menu.Items[3] is MenuItem mi3) mi3.Visibility = isFile ? Visibility.Visible : Visibility.Collapsed;
    }

    private static FileTreeItem? GetContextItem(object sender) =>
        sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { Tag: FileTreeItem item } } }
            ? item : null;

    private void TreeMenu_ViewContents(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is not { IsDirectory: false } item) return;
        try
        {
            var text = File.ReadAllText(item.FullPath);
            var html = MarkdownService.WrapRaw(text);
            WelcomePlaceholder.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
            PrintBtn.IsEnabled = true;
            Browser.NavigateToString(html);
        }
        catch (Exception ex)
        {
            ShowPlaceholder($"Ошибка:\n{ex.Message}");
        }
    }

    private void TreeMenu_OpenLocation(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is not { } item) return;
        try
        {
            if (item.IsDirectory)
                System.Diagnostics.Process.Start("explorer.exe", item.FullPath);
            else
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FullPath}\"");
        }
        catch { /* ignore */ }
    }

    private void TreeMenu_Delete(object sender, RoutedEventArgs e)
    {
        if (GetContextItem(sender) is not { IsDirectory: false } item) return;
        var result = MessageBox.Show(
            $"Удалить файл?\n{item.Name}",
            "Подтверждение удаления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            File.Delete(item.FullPath);
            RemoveFromTree(item);
            if (_currentFilePath == item.FullPath)
                ClearPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось удалить файл:\n{ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveFromTree(FileTreeItem target)
    {
        if (FileTree.Items.Contains(target)) { FileTree.Items.Remove(target); return; }
        foreach (var item in FileTree.Items.OfType<FileTreeItem>())
            if (RemoveFromChildren(item, target)) return;
    }

    private static bool RemoveFromChildren(FileTreeItem parent, FileTreeItem target)
    {
        if (parent.Children.Remove(target)) return true;
        foreach (var child in parent.Children)
            if (RemoveFromChildren(child, target)) return true;
        return false;
    }

    // ------------------------------------------------------------------ //
    //  Tree selection helpers                                              //
    // ------------------------------------------------------------------ //
    private void ClearTreeSelection()
    {
        if (FileTree.SelectedItem == null) return;
        var target = FileTree.SelectedItem;
        ClearSelected(FileTree, target);
    }

    private static bool ClearSelected(ItemsControl container, object target)
    {
        foreach (var item in container.Items)
        {
            if (container.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem tvi)
                continue;
            if (item == target) { tvi.IsSelected = false; return true; }
            if (ClearSelected(tvi, target)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ //
    //  Path resolution                                                     //
    // ------------------------------------------------------------------ //
    private string? ResolveFilePath(string path)
    {
        if (path.Contains('%'))
            path = Uri.UnescapeDataString(path);

        if (File.Exists(path)) return path;

        if (_currentFilePath != null)
        {
            var dir = Path.GetDirectoryName(_currentFilePath) ?? "";
            var rel = Path.GetFullPath(Path.Combine(dir, path));
            if (File.Exists(rel)) return rel;
            if (!Path.HasExtension(rel))
            {
                var withMd = rel + ".md";
                if (File.Exists(withMd)) return withMd;
            }
        }

        if (ProjectPath != null)
        {
            var rel = Path.GetFullPath(Path.Combine(ProjectPath, path));
            if (File.Exists(rel)) return rel;
            if (!Path.HasExtension(rel))
            {
                var withMd = rel + ".md";
                if (File.Exists(withMd)) return withMd;
            }
        }

        var fileName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(fileName) && ProjectPath != null)
        {
            try
            {
                var matches = Directory.GetFiles(ProjectPath, fileName, SearchOption.AllDirectories);
                if (matches.Length == 1) return matches[0];
                if (!Path.HasExtension(fileName))
                {
                    var mdMatches = Directory.GetFiles(ProjectPath, fileName + ".md", SearchOption.AllDirectories);
                    if (mdMatches.Length == 1) return mdMatches[0];
                }
            }
            catch { /* access denied */ }
        }

        return null;
    }

    // ------------------------------------------------------------------ //
    //  UI state helpers                                                    //
    // ------------------------------------------------------------------ //
    private void ClearPreview()
    {
        _currentFilePath = null;
        _history.Clear();
        _historyIndex = -1;
        CurrentFilePath.Text = "Выберите файл в дереве →";
        PrintBtn.IsEnabled = false;
        BackBtn.IsEnabled = false;
        FwdBtn.IsEnabled = false;
        Browser.Visibility = Visibility.Collapsed;
        WelcomePlaceholder.Visibility = Visibility.Visible;
        PlaceholderMsg.Text = "Выберите .md файл";
    }

    private void ShowPlaceholder(string message)
    {
        PrintBtn.IsEnabled = false;
        Browser.Visibility = Visibility.Collapsed;
        WelcomePlaceholder.Visibility = Visibility.Visible;
        PlaceholderMsg.Text = message;
    }

    private void PrintBtn_Click(object sender, RoutedEventArgs e)
    {
        try { Browser.InvokeScript("print"); }
        catch { /* non-critical */ }
    }

    private string GetRelativePath(string fullPath)
    {
        if (ProjectPath is null) return fullPath;
        try { return Path.GetRelativePath(ProjectPath, fullPath); }
        catch { return fullPath; }
    }

    private void SuppressBrowserScriptErrors()
    {
        Browser.Loaded += (_, _) =>
        {
            try
            {
                var field = typeof(WebBrowser).GetField("_axIWebBrowser2",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var obj = field?.GetValue(Browser);
                obj?.GetType().InvokeMember("Silent", BindingFlags.SetProperty, null, obj,
                    new object[] { true });
            }
            catch { /* non-critical */ }
        };
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);
        return parent switch
        {
            null => null,
            T t  => t,
            _    => FindParent<T>(parent)
        };
    }
}
