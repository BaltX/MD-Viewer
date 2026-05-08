using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        RecentList.Children.Clear();

        var projects = RecentProjectsService.Load();
        NoRecentHint.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentList.Visibility   = projects.Count > 0  ? Visibility.Visible : Visibility.Collapsed;

        foreach (var project in projects)
            RecentList.Children.Add(BuildProjectItem(project));
    }

    private Border BuildProjectItem(ProjectInfo project)
    {
        var nameBlock = new TextBlock
        {
            Text = project.Name,
            Foreground = Brush("FgMain"),
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var pathBlock = new TextBlock
        {
            Text = project.Path,
            Foreground = Brush("FgMuted"),
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
        info.Children.Add(nameBlock);
        info.Children.Add(pathBlock);

        var dateBlock = new TextBlock
        {
            Text = project.LastOpened.ToString("dd.MM.yy"),
            Foreground = Brush("FgMuted"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0)
        };

        var deleteBtn = new Button
        {
            Content = "✕",
            ToolTip = "Удалить из истории",
            Cursor = Cursors.Hand,
            Foreground = Brush("FgMuted"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 12,
            Width = 24,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            FocusVisualStyle = null,
            Visibility = Visibility.Hidden
        };
        deleteBtn.Template = DeleteButtonTemplate();
        deleteBtn.Click += (_, e) =>
        {
            e.Handled = true;
            RecentProjectsService.Remove(project.Path);
            Refresh();
        };

        var grid = new Grid { Margin = new Thickness(16, 0, 16, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = "📁",
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0)
        };
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(info, 1);
        Grid.SetColumn(dateBlock, 2);
        Grid.SetColumn(deleteBtn, 3);
        grid.Children.Add(icon);
        grid.Children.Add(info);
        grid.Children.Add(dateBlock);
        grid.Children.Add(deleteBtn);

        var border = new Border
        {
            Background = Brush("BgSidebar"),
            CornerRadius = new CornerRadius(5),
            Height = 54,
            Margin = new Thickness(0, 0, 0, 3),
            Cursor = Cursors.Hand,
            Child = grid
        };

        border.MouseEnter += (_, _) =>
        {
            border.Background = Brush("Hover");
            deleteBtn.Visibility = Visibility.Visible;
        };
        border.MouseLeave += (_, _) =>
        {
            border.Background = Brush("BgSidebar");
            deleteBtn.Visibility = Visibility.Hidden;
        };
        border.MouseLeftButtonDown += (_, _) =>
        {
            if (!Directory.Exists(project.Path))
            {
                MessageBox.Show($"Папка не найдена:\n{project.Path}",
                    "Папка не найдена", MessageBoxButton.OK, MessageBoxImage.Warning);
                Refresh();
                return;
            }
            RecentProjectsService.AddOrUpdate(project);
            ProjectSelected?.Invoke(this, project);
        };

        return border;
    }

    private static ControlTemplate DeleteButtonTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));

        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        borderFactory.AppendChild(contentFactory);
        template.VisualTree = borderFactory;

        var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            Application.Current.Resources["Hover"] as Brush ?? Brushes.Transparent, "Bd"));
        hoverTrigger.Setters.Add(new Setter(Button.ForegroundProperty,
            Application.Current.Resources["FgMain"] as Brush ?? Brushes.White));

        borderFactory.Name = "Bd";
        template.Triggers.Add(hoverTrigger);
        return template;
    }

    private void OpenNew_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Выберите папку проекта" };
        if (dialog.ShowDialog() != true) return;

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

    private static SolidColorBrush Brush(string key) =>
        Application.Current.Resources[key] as SolidColorBrush
        ?? new SolidColorBrush(Colors.Gray);
}
