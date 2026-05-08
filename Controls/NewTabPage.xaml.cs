using System.IO;
using System.Windows;
using System.Windows.Controls;
using MDViewer.Models;
using MDViewer.Services;
using Microsoft.Win32;

namespace MDViewer.Controls;

public partial class NewTabPage : UserControl
{
    public event EventHandler<ProjectInfo>? ProjectSelected;

    public NewTabPage()
    {
        InitializeComponent();
        Refresh();
    }

    public void Refresh()
    {
        var projects = RecentProjectsService.Load();
        RecentList.ItemsSource = projects;
        NoRecentHint.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentList.Visibility  = projects.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OpenNew_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите папку проекта"
        };

        if (dialog.ShowDialog() == true)
        {
            var folderPath = dialog.FolderName;
            var project = new ProjectInfo
            {
                Name = Path.GetFileName(folderPath.TrimEnd('\\', '/'))
                       is { Length: > 0 } n ? n : folderPath,
                Path = folderPath,
                LastOpened = DateTime.Now
            };
            RecentProjectsService.AddOrUpdate(project);
            ProjectSelected?.Invoke(this, project);
        }
    }

    private void RecentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecentList.SelectedItem is not ProjectInfo project) return;
        RecentList.SelectedItem = null;

        if (!Directory.Exists(project.Path))
        {
            MessageBox.Show($"Папка не найдена:\n{project.Path}",
                "Папка не найдена", MessageBoxButton.OK, MessageBoxImage.Warning);
            Refresh();
            return;
        }

        RecentProjectsService.AddOrUpdate(project);
        ProjectSelected?.Invoke(this, project);
    }
}
