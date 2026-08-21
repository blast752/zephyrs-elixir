namespace ZephyrsElixir.UI.Pages;

public sealed partial class Tools : UserControl
{
    private readonly List<MenuItemBase> _tools;

    public Tools()
    {
        InitializeComponent();
        
        _tools =
        [
            new("apk_installer", "download", () => Strings.Tools_ApkInstaller_Title, () => Strings.Tools_ApkInstaller_Description,
                AppBrushes.GradientBlue),
            new("screen_mirror", "display", () => Strings.Tools_ScreenMirror_Title, () => Strings.Tools_ScreenMirror_Description,
                AppBrushes.GradientPurple),
            new("performance_monitor", "chart", () => Strings.Tools_PerformanceMonitor_Title, () => Strings.Tools_PerformanceMonitor_Description,
                AppBrushes.GradientCyan),
            new("reboot", "restore", () => Strings.Tools_Reboot_Title, () => Strings.Tools_Reboot_Description,
                AppBrushes.GradientOrange),
            new("file_manager", "folder-open", () => Strings.Tools_FileManager_Title, () => Strings.Tools_FileManager_Description,
                AppBrushes.GradientCyan),
            new("adb_shell", "console", () => Strings.Tools_AdbConsole_Title, () => Strings.Tools_AdbConsole_Description,
                AppBrushes.GradientGreen),
        ];

        DataContext = this;
        Loaded += OnLoaded;
        
        TranslationManager.Instance.LanguageChanged += (s, e) => UpdateLocalizedStrings();
    }

    public IEnumerable<MenuItemBase> ToolItems => _tools;

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateLocalizedStrings();

    private void UpdateLocalizedStrings()
    {
        foreach (var tool in _tools)
            tool.Refresh();
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;

        // A valid licence with the module on disk but not yet in memory: wake it here, or the first
        // click on a Pro tool would be spent doing nothing visible and the user would have to click twice.
        if (LicenseService.Instance.IsPro && !ProLoader.IsLoaded)
            ProLoader.ReloadIfNeeded();

        if (key == "screen_mirror" && !Features.IsAvailable(Features.ScreenMirror))
        {
            ShowProOrModuleError("Pro_Required_ScreenMirror");
            return;
        }

        if (key == "performance_monitor" && !Features.IsAvailable(Features.PerformanceMonitor))
        {
            ShowProOrModuleError("Pro_Required_PerformanceMonitor");
            return;
        }

        FrameworkElement? page = key switch
        {
            "apk_installer" => new ApkInstaller(CloseSubPage),
            "screen_mirror" => Pro.CreatePage(ProCommandIds.ScreenMirrorPage, CloseSubPage),
            "performance_monitor" => Pro.CreatePage(ProCommandIds.PerformanceMonitorPage, CloseSubPage),
            "reboot" => new PowerMenu(CloseSubPage),
            "file_manager" => new FileManager(CloseSubPage),
            "adb_shell" => new AdbShellConsoleView(CloseSubPage),
            _ => null
        };

        if (page == null && key is "screen_mirror" or "performance_monitor")
        {
            ShowProOrModuleError(key == "performance_monitor" ? "Pro_Required_PerformanceMonitor" : "Pro_Required_ScreenMirror");
            return;
        }

        if (page != null)
            ShowSubPage(page);
    }

    private void ShowProOrModuleError(string proRequiredKey = "Pro_Required_ScreenMirror")
    {
        if (LicenseService.Instance.IsPro && !ProLoader.IsLoaded)
            DialogService.Instance.ShowInfoDirect(
                Strings.Pro_Module_Title,
                Strings.Pro_Module_NotLoaded,
                Window.GetWindow(this));
        else
            DialogService.Instance.ShowProRequiredWithUpgrade(proRequiredKey);
    }

    private void ShowSubPage(FrameworkElement page)
    {
        SubPageContent.Content = page;
        SubPageHost.Visibility = Visibility.Visible;
    }

    public void CloseSubPage()
    {
        SubPageHost.Visibility = Visibility.Collapsed;
        SubPageContent.Content = null;
    }
}
