using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using MDViewer.Controls;
using MDViewer.Models;
using MDViewer.Services;

namespace MDViewer;

public partial class MainWindow : Window
{
    // ------------------------------------------------------------------ //
    //  DWM – rounded corners + shadow                                     //
    // ------------------------------------------------------------------ //
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr,
        ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd,
        ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND                   = 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        // Rounded corners (Windows 11 only; silently ignored on Windows 10)
        var corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
            ref corner, sizeof(int));

        // Thin DWM shadow on all sides
        var m = new Margins { Left = 0, Right = 0, Top = 0, Bottom = 1 };
        DwmExtendFrameIntoClientArea(hwnd, ref m);
    }

    // ------------------------------------------------------------------ //
    //  Window state                                                        //
    // ------------------------------------------------------------------ //
    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Update icon: E922 = maximize, E923 = restore
        WinMaxBtn.Content = WindowState == WindowState.Maximized
            ? "" : "";
        WinMaxBtn.ToolTip = WindowState == WindowState.Maximized
            ? "Восстановить" : "Развернуть";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // PreviewMouseLeftButtonDown tunnels down before children handle it.
        // Walk from the original source up to this Border: if we pass through any
        // Button or tab-button Border, it's an interactive element — let it handle the click.
        var el = e.OriginalSource as DependencyObject;
        while (el != null && !ReferenceEquals(el, sender))
        {
            if (el is Button) return;
            if (el is Border { Tag: TabButtonState }) return;
            el = VisualTreeHelper.GetParent(el);
        }

        if (e.ClickCount >= 2)
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }
        else
        {
            try { DragMove(); } catch { /* ignore spurious calls */ }
        }
    }

    private void WinMin_Click(object sender, RoutedEventArgs e)  => SystemCommands.MinimizeWindow(this);
    private void WinClose_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void WinMax_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

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
    private ProjectView? _activeProjectView;

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
    //  Print (title-bar button delegates to the active ProjectView)       //
    // ------------------------------------------------------------------ //
    private void PrintBtn_Click(object sender, RoutedEventArgs e)
        => _activeProjectView?.PrintCurrentFile();

    private void TrackActiveProjectView(ProjectView? next)
    {
        if (_activeProjectView != null)
            _activeProjectView.PrintableChanged -= OnPrintableChanged;

        _activeProjectView = next;

        if (_activeProjectView != null)
        {
            _activeProjectView.PrintableChanged += OnPrintableChanged;
            PrintBtn.IsEnabled = _activeProjectView.CanPrint;
        }
        else
        {
            PrintBtn.IsEnabled = false;
        }
    }

    private void OnPrintableChanged(object? sender, bool canPrint)
        => PrintBtn.IsEnabled = canPrint;

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
            ["SolarizedDark"] = "Solarized Dark",
            ["Nord"]          = "Nord",
            ["Dracula"]       = "Dracula",
            ["OneDark"]       = "One Dark"
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
            Title       = title,
            Content     = content,
            IsNewTabPage = isNewTabPage,
            TabButton   = BuildTabButton(title)
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

        // Update print button state
        TrackActiveProjectView(_tabs[index].Content as ProjectView);

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
    private sealed class TabButtonState
    {
        public required System.Windows.Shapes.Rectangle Indicator;
        public required TextBlock Label;
        public bool IsActive;
    }

    private static Border BuildTabButton(string title)
    {
        var label = new TextBlock
        {
            Text = title,
            Foreground = GetBrush("FgMuted"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(12, 0, 4, 0)
        };

        var closeBtn = new Button
        {
            Name = "CloseBtn",
            Margin = new Thickness(0, 0, 6, 0),
            Style = Application.Current.FindResource("CloseTabBtn") as Style
        };

        var inner = new DockPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            LastChildFill = true
        };
        DockPanel.SetDock(closeBtn, Dock.Right);
        inner.Children.Add(closeBtn);
        inner.Children.Add(label);

        var indicator = new System.Windows.Shapes.Rectangle
        {
            Height = 3,
            Fill = Brushes.Transparent
        };
        Grid.SetRow(indicator, 0);
        Grid.SetRow(inner, 1);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3) });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(indicator);
        grid.Children.Add(inner);

        var state = new TabButtonState { Indicator = indicator, Label = label };

        var border = new Border
        {
            MinWidth = 110,
            MaxWidth = 220,
            Height = 35,
            Background = GetBrush("TabInactive"),
            BorderBrush = GetBrush("BorderColor"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Cursor = Cursors.Hand,
            Tag = state,
            Child = grid
        };

        border.MouseEnter += (_, _) =>
        {
            if (!state.IsActive) border.Background = GetBrush("Hover");
        };
        border.MouseLeave += (_, _) =>
        {
            if (!state.IsActive) border.Background = GetBrush("TabInactive");
        };

        return border;
    }

    private static void SetTabActive(Border tab, bool active)
    {
        if (tab.Tag is not TabButtonState state) return;
        state.IsActive = active;
        state.Indicator.Fill = active ? GetBrush("TabActiveIndicator") : Brushes.Transparent;
        state.Label.Foreground = GetBrush(active ? "FgMain" : "FgMuted");
        state.Label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        tab.Background = GetBrush(active ? "TabActive" : "TabInactive");
        tab.BorderBrush = GetBrush("BorderColor");
        tab.BorderThickness = new Thickness(0, 0, 1, 0);
    }

    private void RefreshAllTabColors()
    {
        for (var i = 0; i < _tabs.Count; i++)
            SetTabActive(_tabs[i].TabButton, _tabs[i].IsActive);
    }

    private static void UpdateTabButtonLabel(TabEntry entry)
    {
        if (entry.TabButton.Tag is TabButtonState state)
            state.Label.Text = entry.Title;
    }

    // ------------------------------------------------------------------ //
    //  Tab strip mouse wheel → horizontal scroll                          //
    // ------------------------------------------------------------------ //
    private void TabScroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 3.0);
            e.Handled = true;
        }
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

    private static T? FindChild<T>(DependencyObject parent, string? name = null)
        where T : FrameworkElement
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
