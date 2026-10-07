using Avalonia;

namespace BulkRenameUtility;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { WmClass = "bulk-rename-utility", UseDBusMenu = false, UseDBusFilePicker = false })
            .LogToTrace();
}
