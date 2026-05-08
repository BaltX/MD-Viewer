using System.IO;
using System.Text.Json;
using MDViewer.Models;

namespace MDViewer.Services;

public static class RecentProjectsService
{
    private static readonly string StoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MDViewer", "recent.json");

    public static List<ProjectInfo> Load()
    {
        try
        {
            if (!File.Exists(StoragePath)) return new();
            var json = File.ReadAllText(StoragePath);
            return JsonSerializer.Deserialize<List<ProjectInfo>>(json) ?? new();
        }
        catch { return new(); }
    }

    public static void Remove(string path)
    {
        var list = Load();
        list.RemoveAll(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
        Save(list);
    }

    public static void AddOrUpdate(ProjectInfo project)
    {
        var list = Load();
        var existing = list.FirstOrDefault(p =>
            string.Equals(p.Path, project.Path, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.LastOpened = DateTime.Now;
            existing.Name = project.Name;
        }
        else
        {
            list.Insert(0, project);
        }

        Save(list.OrderByDescending(p => p.LastOpened).Take(20));
    }

    private static void Save(IEnumerable<ProjectInfo> projects)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            var json = JsonSerializer.Serialize(projects.ToList(),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StoragePath, json);
        }
        catch { /* non-critical */ }
    }
}
