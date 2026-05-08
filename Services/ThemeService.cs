using System.IO;
using System.Text.Json;
using System.Windows;

namespace MDViewer.Services;

public static class ThemeService
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MDViewer", "settings.json");

    public static string Current { get; private set; } = "Dark";

    public static event EventHandler? ThemeChanged;

    public static void Initialize()
    {
        var saved = LoadSaved();
        ApplyInternal(saved, notify: false);
    }

    public static void Apply(string themeName)
    {
        if (themeName == Current) return;
        ApplyInternal(themeName, notify: true);
        Save(themeName);
    }

    private static void ApplyInternal(string themeName, bool notify)
    {
        Current = themeName;
        var uri = new Uri($"Themes/{themeName}Theme.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(dict);

        if (notify) ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static string LoadSaved()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return "Dark";
            var json = File.ReadAllText(SettingsPath);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("theme").GetString() ?? "Dark";
        }
        catch { return "Dark"; }
    }

    private static void Save(string themeName)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var json = JsonSerializer.Serialize(new { theme = themeName });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* non-critical */ }
    }
}
