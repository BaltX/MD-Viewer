namespace MDViewer.Models;

public class ProjectInfo
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; } = DateTime.Now;
}
