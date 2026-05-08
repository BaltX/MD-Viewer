using System.Diagnostics;
using System.IO;
using System.Windows;
using MDViewer.Services;

namespace MDViewer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SetBrowserEmulationMode();
        ThemeService.Initialize();
    }

    private static void SetBrowserEmulationMode()
    {
        try
        {
            var fileName = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName ?? "");
            if (string.IsNullOrEmpty(fileName)) return;

            SetFeatureFlag(@"FEATURE_BROWSER_EMULATION", fileName, 11001u);
            // Disable Local Machine Zone lockdown — suppresses the IE security bar
            // that appears when navigating local file:// HTML pages.
            SetFeatureFlag(@"FEATURE_LOCALMACHINE_LOCKDOWN", fileName, 0u);
        }
        catch { /* non-critical */ }
    }

    private static void SetFeatureFlag(string feature, string processFileName, uint value)
    {
        var regPath = $@"SOFTWARE\Microsoft\Internet Explorer\Main\FeatureControl\{feature}";
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(regPath, writable: true)
                        ?? Microsoft.Win32.Registry.CurrentUser.CreateSubKey(regPath);
        key?.SetValue(processFileName, value, Microsoft.Win32.RegistryValueKind.DWord);
    }
}
