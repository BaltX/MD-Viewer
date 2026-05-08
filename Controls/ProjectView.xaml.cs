using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using MDViewer.Models;
using MDViewer.Services;

namespace MDViewer.Controls;

public partial class ProjectView : UserControl
{
    private string? _currentFilePath;
    private bool _isBrowserNavigating;

    public string? ProjectPath { get; private set; }

    public ProjectView()
    {
        InitializeComponent();
        SuppressBrowserScriptErrors();
        ThemeService.ThemeChanged += (_, _) => ReloadCurrentFile();

        // WPF TreeView doesn't scroll with the mouse wheel unless focused.
        // Intercept PreviewMouseWheel on the TreeView and forward it to
        // the parent ScrollViewer so scrolling works on hover without a click.
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
        FileTree.Items.Add(root);

        if (FileTree.ItemContainerGenerator.ContainerFromItem(root) is TreeViewItem tvi)
            tvi.IsExpanded = true;

        ClearPreview();
    }

    public void ReloadCurrentFile()
    {
        if (_currentFilePath != null)
            OpenFile(_currentFilePath);
    }

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

            var tempPath = GetTempHtmlPath();
            File.WriteAllText(tempPath, html, System.Text.Encoding.UTF8);

            WelcomePlaceholder.Visibility = Visibility.Collapsed;
            Browser.Visibility = Visibility.Visible;
            PrintBtn.IsEnabled = true;
            _isBrowserNavigating = true;
            Browser.Navigate(new Uri(tempPath));
        }
        catch (Exception ex)
        {
            ShowPlaceholder($"Ошибка при открытии файла:\n{ex.Message}");
        }
    }

    private void Browser_Navigating(object sender, NavigatingCancelEventArgs e)
    {
        if (_isBrowserNavigating)
        {
            _isBrowserNavigating = false;
            return;
        }

        if (e.Uri?.IsFile == true)
        {
            e.Cancel = true;

            // Uri.LocalPath decodes percent-encoding (including Cyrillic) on Windows.
            // After MarkdownService.FixLinkPaths all .md hrefs are already absolute,
            // so this path points directly to the real project file, not to temp.
            var localPath = e.Uri.LocalPath;

            // Fallback fuzzy resolver handles edge cases:
            // links without .md extension, incorrect capitalisation, etc.
            var resolved = ResolveFilePath(localPath);
            if (resolved != null)
                OpenFile(resolved);
            else
                ShowPlaceholder($"Файл не найден:\n{localPath}");
            return;
        }

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

    /// <summary>
    /// Tries to find a file:
    /// 1. Exact path as given
    /// 2. Relative to current file's directory
    /// 3. Relative to project root
    /// 4. Fuzzy: search by filename in the whole project tree
    /// </summary>
    private string? ResolveFilePath(string path)
    {
        // Decode percent-encoded characters (Cyrillic etc.) that may survive from the URI
        if (path.Contains('%'))
            path = Uri.UnescapeDataString(path);

        // 1. Exact
        if (File.Exists(path)) return path;

        // 2. Relative to current file directory
        if (_currentFilePath != null)
        {
            var dir = Path.GetDirectoryName(_currentFilePath) ?? "";
            var rel = Path.GetFullPath(Path.Combine(dir, path));
            if (File.Exists(rel)) return rel;

            // Try with .md extension if missing
            if (!Path.HasExtension(rel))
            {
                var withMd = rel + ".md";
                if (File.Exists(withMd)) return withMd;
            }
        }

        // 3. Relative to project root
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

        // 4. Fuzzy: search by filename across project
        var fileName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(fileName) && ProjectPath != null)
        {
            try
            {
                var matches = Directory.GetFiles(ProjectPath, fileName, SearchOption.AllDirectories);
                if (matches.Length == 1) return matches[0];

                // If filename has no extension, try adding .md
                if (!Path.HasExtension(fileName))
                {
                    var mdMatches = Directory.GetFiles(ProjectPath, fileName + ".md",
                        SearchOption.AllDirectories);
                    if (mdMatches.Length == 1) return mdMatches[0];
                }
            }
            catch { /* access denied */ }
        }

        return null;
    }

    private void PrintBtn_Click(object sender, RoutedEventArgs e)
    {
        try { Browser.InvokeScript("print"); }
        catch { /* non-critical */ }
    }

    private void ClearPreview()
    {
        _currentFilePath = null;
        CurrentFilePath.Text = "Выберите файл в дереве →";
        PrintBtn.IsEnabled = false;
        Browser.Visibility = Visibility.Collapsed;
        WelcomePlaceholder.Visibility = Visibility.Visible;
        PlaceholderMsg.Text = "Выберите .md файл";
    }

    private void ShowPlaceholder(string message)
    {
        Browser.Visibility = Visibility.Collapsed;
        WelcomePlaceholder.Visibility = Visibility.Visible;
        PlaceholderMsg.Text = message;
    }

    private string GetRelativePath(string fullPath)
    {
        if (ProjectPath is null) return fullPath;
        try { return Path.GetRelativePath(ProjectPath, fullPath); }
        catch { return fullPath; }
    }

    private static string GetTempHtmlPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MDViewer");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "preview.html");
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
