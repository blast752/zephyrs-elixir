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
                UIHelpers.CreateGradientBrush("#63B5FF", "#1175E6"), Color.FromRgb(99, 181, 255)),
            new("screen_mirror", "\uE7F4", () => Strings.Tools_ScreenMirror_Title, () => Strings.Tools_ScreenMirror_Description, 
                UIHelpers.CreateGradientBrush("#7D64FF", "#5A3FD9"), Color.FromRgb(125, 100, 255), true),
            new("file_manager", "\uE8B7", () => Strings.Tools_FileManager_Title, () => Strings.Tools_FileManager_Description, 
                UIHelpers.CreateGradientBrush("#00D68F", "#00B377"), Color.FromRgb(0, 214, 143), false),
            new("logcat", "\uE756", () => Strings.Tools_Logcat_Title, () => Strings.Tools_Logcat_Description, 
                UIHelpers.CreateGradientBrush("#FFD000", "#FF9500"), Color.FromRgb(255, 208, 0), false),
            new("backup", "\uE8F1", () => Strings.Tools_Backup_Title, () => Strings.Tools_Backup_Description, 
                UIHelpers.CreateGradientBrush("#FF6B6B", "#DC143C"), Color.FromRgb(255, 107, 107), false),
            new("permissions", "\uE72E", () => Strings.Tools_Permissions_Title, () => Strings.Tools_Permissions_Description, 
                UIHelpers.CreateGradientBrush("#00BFFF", "#0099CC"), Color.FromRgb(0, 191, 255), false),
            new("shell", "\uE756", () => Strings.Tools_Shell_Title, () => Strings.Tools_Shell_Description, 
                UIHelpers.CreateGradientBrush("#A0A0A0", "#707070"), Color.FromRgb(160, 160, 160), false),
            new("reboot", "\uE777", () => Strings.Tools_Reboot_Title, () => Strings.Tools_Reboot_Description, 
                UIHelpers.CreateGradientBrush("#FF9F43", "#E67E22"), Color.FromRgb(255, 159, 67), true)
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
        {
            tool.RefreshStrings();
        }
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
            DialogService.Instance.ShowProRequiredWithUpgrade("Pro_Required_ScreenMirror");
            return;
        }

        FrameworkElement? page = key switch
        {
            "apk_installer" => new ApkInstaller(CloseSubPage),  
            "screen_mirror" => new ScreenMirror(CloseSubPage),
            "reboot" => new PowerMenu(CloseSubPage),         
            _ => null
        };

        if (page != null)
            ShowSubPage(page);
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

public sealed class ToolItem : INotifyPropertyChanged
{
    private bool _isEnabled;
    private readonly Func<string> _titleAccessor;
    private readonly Func<string> _descriptionAccessor;

    public string Key { get; }
    public string Icon { get; }
    public string Title => _titleAccessor();
    public string Description => _descriptionAccessor();
    public Brush IconBrush { get; }
    public Color GlowColor { get; }
    public bool BaseEnabled { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ToolItem(string key, string icon, Func<string> titleAccessor, Func<string> descriptionAccessor, 
                    Brush iconBrush, Color glowColor, bool baseEnabled = true)
    {
        Key = key;
        Icon = icon;
        _titleAccessor = titleAccessor;
        _descriptionAccessor = descriptionAccessor;
        IconBrush = iconBrush;
        GlowColor = glowColor;
        BaseEnabled = baseEnabled;
        _isEnabled = baseEnabled;
    }

    public void RefreshStrings()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}