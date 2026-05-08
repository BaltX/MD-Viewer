using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

namespace MDViewer.Models;

public class FileTreeItem : INotifyPropertyChanged
{
    private static readonly FileTreeItem Placeholder = new("", "", false);

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public ObservableCollection<FileTreeItem> Children { get; } = new();

    public string Icon => IsDirectory
        ? "📁"
        : System.IO.Path.GetExtension(Name).ToLowerInvariant() == ".md" ? "📄" : "📃";

    private FileTreeItem(string name, string fullPath, bool isDirectory)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        if (isDirectory)
            Children.Add(Placeholder);
    }

    public static FileTreeItem CreateRoot(string path) =>
        new(System.IO.Path.GetFileName(path).Length > 0
                ? System.IO.Path.GetFileName(path)
                : path,
            path, true);

    public bool IsLoaded => !(Children.Count == 1 && Children[0] == Placeholder);

    public void EnsureLoaded()
    {
        if (IsLoaded) return;
        Children.Clear();
        try
        {
            foreach (var dir in Directory.GetDirectories(FullPath).OrderBy(System.IO.Path.GetFileName))
                Children.Add(new FileTreeItem(System.IO.Path.GetFileName(dir)!, dir, true));

            foreach (var file in Directory.GetFiles(FullPath).OrderBy(System.IO.Path.GetFileName))
                Children.Add(new FileTreeItem(System.IO.Path.GetFileName(file)!, file, false));
        }
        catch { /* access denied etc. */ }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
