namespace ZephyrsElixir.UI.Pages;

public sealed partial class Tools : UserControl
{
    private readonly List<ToolItem> _tools;
    private FrameworkElement? _activeSubPage;

    public Tools()
    {
        InitializeComponent();
        
        _tools = new List<ToolItem>
        {
            new("apk_installer", "\uE896", () => Strings.Tools_ApkInstaller_Title, () => Strings.Tools_ApkInstaller_Description, 
                AppBrushes.GradientApk, Color.FromRgb(99, 181, 255)),
            new("screen_mirror", "\uE7F4", () => Strings.Tools_ScreenMirror_Title, () => Strings.Tools_ScreenMirror_Description, 
                AppBrushes.GradientApkm, Color.FromRgb(125, 100, 255), true),
            new("reboot", "\uE777", () => Strings.Tools_Reboot_Title, () => Strings.Tools_Reboot_Description, 
                AppBrushes.GradientOrange, Color.FromRgb(255, 159, 67), true)
        };

        DataContext = this;
        Loaded += OnLoaded;
        
        TranslationManager.Instance.LanguageChanged += (s, e) => UpdateLocalizedStrings();
    }

    public IEnumerable<ToolItem> ToolItems => _tools;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateLocalizedStrings();
        UpdateToolsAvailability();
    }

    private void UpdateLocalizedStrings()
    {
        foreach (var tool in _tools)
            tool.Refresh();
    }

    private void UpdateToolsAvailability()
    {
        ToolsGrid.ItemsSource = null;
        ToolsGrid.ItemsSource = _tools;
    }

    private void OnToolClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string key }) return;

        if (key == "screen_mirror" && !Features.IsAvailable(Features.ScreenMirror))
        {
            ShowProOrModuleError();
            return;
        }

        FrameworkElement? page = key switch
        {
            "apk_installer" => new ApkInstaller(CloseSubPage),
            "screen_mirror" => Pro.CreatePage(ProCommandIds.ScreenMirrorPage, CloseSubPage),
            "reboot" => new PowerMenu(CloseSubPage),
            _ => null
        };

        if (page == null && key == "screen_mirror")
        {
            ShowProOrModuleError();
            return;
        }

        if (page != null)
            ShowSubPage(page);
    }

    private void ShowProOrModuleError()
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
            DialogService.Instance.ShowProRequiredWithUpgrade("Pro_Required_ScreenMirror");
    }

    private void ShowSubPage(FrameworkElement page)
    {
        _activeSubPage = page;
        SubPageContent.Content = page;
        SubPageHost.Visibility = Visibility.Visible;
    }

    public void CloseSubPage()
    {
        SubPageHost.Visibility = Visibility.Collapsed;
        SubPageContent.Content = null;
        _activeSubPage = null;
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
                    Brush iconBrush, Color glowColor, bool baseEnabled = true)
        : base(key, icon, titleAccessor, descriptionAccessor, iconBrush, glowColor)
    {
        BaseEnabled = baseEnabled;
        _isEnabled = baseEnabled;
    }

    public void RefreshStrings() => Refresh();
}
