using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MDViewer.Controls;
using MDViewer.Models;
using MDViewer.Services;

namespace MDViewer;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------ //
    //  Tab model                                                           //
    // ------------------------------------------------------------------ //
    private sealed class TabEntry
    {
        public string Title { get; set; } = "Новая вкладка";
        public FrameworkElement Content { get; set; } = null!;
        public Border TabButton { get; set; } = null!;
        public bool IsNewTabPage { get; set; }
        public bool IsActive { get; set; }
    }

    private readonly List<TabEntry> _tabs = new();
    private int _activeIndex = -1;

    // ------------------------------------------------------------------ //
    //  Init                                                                //
    // ------------------------------------------------------------------ //
    public MainWindow()
    {
        InitializeComponent();
        ThemeService.ThemeChanged += OnThemeChanged;
        UpdateCurrentThemeLabel();
        AddNewTabPage();
    }

    // ------------------------------------------------------------------ //
    //  Theme                                                               //
    // ------------------------------------------------------------------ //
    private void ThemeBtn_Click(object sender, RoutedEventArgs e)
    {
        UpdateCurrentThemeLabel();
        ThemePopup.IsOpen = !ThemePopup.IsOpen;
    }

    private void ThemeOption_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string theme })
        {
            ThemeService.Apply(theme);
            ThemePopup.IsOpen = false;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RefreshAllTabColors();
        UpdateCurrentThemeLabel();

        // Reload markdown preview in the active tab if it has a loaded file
        if (_activeIndex >= 0 && _tabs[_activeIndex].Content is ProjectView pv)
            pv.ReloadCurrentFile();
    }

    private void UpdateCurrentThemeLabel()
    {
        var names = new Dictionary<string, string>
        {
            ["Dark"]          = "Тёмная",
            ["Light"]         = "Светлая",
            ["Monokai"]       = "Monokai",
            ["SolarizedDark"] = "Solarized Dark"
        };
        CurrentThemeLabel.Text = names.TryGetValue(ThemeService.Current, out var n)
            ? $"Текущая: {n}" : "";
    }

    // ------------------------------------------------------------------ //
    //  Project open                                                        //
    // ------------------------------------------------------------------ //
    private void OpenProject(ProjectInfo project, int replaceIndex = -1)
    {
        var view = new ProjectView();
        view.LoadProject(project);

        if (replaceIndex >= 0 && replaceIndex < _tabs.Count)
        {
            _tabs[replaceIndex].Title = project.Name;
            _tabs[replaceIndex].Content = view;
            _tabs[replaceIndex].IsNewTabPage = false;
            UpdateTabButtonLabel(_tabs[replaceIndex]);
            SelectTab(replaceIndex);
        }
        else
        {
            AddTab(project.Name, view, isNewTabPage: false);
        }
    }

    // ------------------------------------------------------------------ //
    //  Tab management                                                      //
    // ------------------------------------------------------------------ //
    private void AddNewTabPage()
    {
        var page = new NewTabPage();
        page.ProjectSelected += (_, project) =>
        {
            var idx = _tabs.FindIndex(t => t.Content == page);
            OpenProject(project, replaceIndex: idx);
        };
        AddTab("Новая вкладка", page, isNewTabPage: true);
    }

    private void AddTab(string title, FrameworkElement content, bool isNewTabPage)
    {
        var entry = new TabEntry
        {
            Title = title,
            Content = content,
            IsNewTabPage = isNewTabPage,
            TabButton = BuildTabButton(title)
        };
        _tabs.Add(entry);

        entry.TabButton.MouseLeftButtonDown += (_, e) =>
        {
            SelectTab(_tabs.IndexOf(entry));
            e.Handled = true;
        };

        var closeBtn = FindChild<Button>(entry.TabButton, "CloseBtn");
        if (closeBtn != null)
            closeBtn.Click += (_, e) =>
            {
                e.Handled = true;
                CloseTab(_tabs.IndexOf(entry));
            };

        // Insert before the permanent "+" button (always the last child)
        TabStrip.Children.Insert(TabStrip.Children.Count - 1, entry.TabButton);
        SelectTab(_tabs.Count - 1);
    }

    private void SelectTab(int index)
    {
        if (index < 0 || index >= _tabs.Count) return;

        if (_activeIndex >= 0 && _activeIndex < _tabs.Count)
        {
            _tabs[_activeIndex].IsActive = false;
            SetTabActive(_tabs[_activeIndex].TabButton, false);
        }

        _activeIndex = index;
        _tabs[index].IsActive = true;
        SetTabActive(_tabs[index].TabButton, true);
        ContentArea.Content = _tabs[index].Content;

        if (_tabs[index].IsNewTabPage && _tabs[index].Content is NewTabPage page)
            page.Refresh();
    }

    private void CloseTab(int index)
    {
        if (_tabs.Count == 1)
        {
            var page = new NewTabPage();
            var entry = _tabs[0];
            page.ProjectSelected += (_, project) => OpenProject(project, replaceIndex: 0);
            entry.Title = "Новая вкладка";
            entry.Content = page;
            entry.IsNewTabPage = true;
            UpdateTabButtonLabel(entry);
            SelectTab(0);
            return;
        }

        TabStrip.Children.Remove(_tabs[index].TabButton);
        _tabs.RemoveAt(index);

        var newIndex = Math.Min(index, _tabs.Count - 1);
        _activeIndex = -1;
        SelectTab(newIndex);
    }

    // ------------------------------------------------------------------ //
    //  Tab button building                                                 //
    // ------------------------------------------------------------------ //
    private static Border BuildTabButton(string title)
    {
        var label = new TextBlock
        {
            Text = title,
            Foreground = GetBrush("FgMuted"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 160,
            Margin = new Thickness(12, 0, 4, 0)
        };

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Margin = new Thickness(0, 0, 6, 0),
            Style = Application.Current.FindResource("CloseTabBtn") as Style
        };

        var inner = new StackPanel { Orientation = Orientation.Horizontal };
        inner.Children.Add(label);
        inner.Children.Add(closeBtn);

        var border = new Border
        {
            MinWidth = 110,
            MaxWidth = 220,
            Height = 35,
            Background = GetBrush("TabInactive"),
            BorderBrush = GetBrush("BorderColor"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Cursor = Cursors.Hand,
            Child = inner
        };

        border.MouseEnter += (_, _) =>
        {
            if (border.Tag is not true) border.Background = GetBrush("Hover");
        };
        border.MouseLeave += (_, _) =>
        {
            if (border.Tag is not true) border.Background = GetBrush("TabInactive");
        };

        return border;
    }

    private static void SetTabActive(Border tab, bool active)
    {
        tab.Background = GetBrush(active ? "TabActive" : "TabInactive");
        tab.BorderThickness = active ? new Thickness(0, 2, 1, 0) : new Thickness(0, 0, 1, 0);
        tab.BorderBrush = GetBrush(active ? "TabActiveIndicator" : "BorderColor");
        tab.Tag = active;

        var panel = tab.Child as StackPanel;
        if (panel?.Children[0] is TextBlock lbl)
            lbl.Foreground = GetBrush(active ? "FgMain" : "FgMuted");
    }

    private void RefreshAllTabColors()
    {
        for (var i = 0; i < _tabs.Count; i++)
            SetTabActive(_tabs[i].TabButton, _tabs[i].IsActive);
    }

    private static void UpdateTabButtonLabel(TabEntry entry)
    {
        var panel = entry.TabButton.Child as StackPanel;
        if (panel?.Children[0] is TextBlock lbl)
            lbl.Text = entry.Title;
    }

    // ------------------------------------------------------------------ //
    //  Event handlers                                                      //
    // ------------------------------------------------------------------ //
    private void AddTab_Click(object sender, RoutedEventArgs e) => AddNewTabPage();

    // ------------------------------------------------------------------ //
    //  Helpers                                                             //
    // ------------------------------------------------------------------ //
    private static SolidColorBrush GetBrush(string key) =>
        Application.Current.Resources[key] as SolidColorBrush
        ?? new SolidColorBrush(Colors.Gray);

    private static T? FindChild<T>(DependencyObject parent, string? name = null) where T : FrameworkElement
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && (name == null || t.Name == name)) return t;
            var found = FindChild<T>(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
