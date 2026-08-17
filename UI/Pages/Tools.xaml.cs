namespace ZephyrsElixir.UI.Pages;

public sealed partial class Tools : UserControl
{
    private readonly List<ToolItem> _tools;

    public Tools()
    {
        InitializeComponent();
        
        _tools =
        [
            new("apk_installer", "download", () => Strings.Tools_ApkInstaller_Title, () => Strings.Tools_ApkInstaller_Description,
                AppBrushes.GradientApk),
            new("screen_mirror", "display", () => Strings.Tools_ScreenMirror_Title, () => Strings.Tools_ScreenMirror_Description,
                AppBrushes.GradientApkm, true),
            new("performance_monitor", "chart", () => Strings.Tools_PerformanceMonitor_Title, () => Strings.Tools_PerformanceMonitor_Description,
                AppBrushes.GradientCyan, true),
            new("reboot", "restore", () => Strings.Tools_Reboot_Title, () => Strings.Tools_Reboot_Description,
                AppBrushes.GradientOrange, true),
            new("file_manager", "folder-open", () => Strings.Tools_FileManager_Title, () => Strings.Tools_FileManager_Description,
                AppBrushes.GradientCyan),
            new("adb_shell", "console", () => Strings.Tools_AdbConsole_Title, () => Strings.Tools_AdbConsole_Description,
                AppBrushes.GradientGreen),
        ];

        DataContext = this;
        Loaded += OnLoaded;
        
        TranslationManager.Instance.LanguageChanged += (s, e) => UpdateLocalizedStrings();
    }

    public IEnumerable<ToolItem> ToolItems => _tools;

    private void OnLoaded(object sender, RoutedEventArgs e) => UpdateLocalizedStrings();

    private void UpdateLocalizedStrings()
    {
        foreach (var tool in _tools)
            tool.Refresh();
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;

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
        {
            ProLoader.ReloadIfNeeded();
            if (ProLoader.IsLoaded) return;
            DialogService.Instance.ShowInfoDirect(
                "Pro Module",
                "Pro module not found. Please restart the application.",
                Window.GetWindow(this));
        }
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

public sealed class ToolItem : MenuItemBase
{
    private bool _isEnabled;

    public bool BaseEnabled { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public ToolItem(string key, string icon, Func<string> titleAccessor, Func<string> descriptionAccessor,
                    Brush iconBrush, bool baseEnabled = true)
        : base(key, icon, titleAccessor, descriptionAccessor, iconBrush)
    {
        BaseEnabled = baseEnabled;
        _isEnabled = baseEnabled;
    }
}
