namespace ZephyrsElixir.ViewModels;

public sealed class HelpViewModel : INotifyPropertyChanged
{
    #region Static Content Data

    private static class Icons
    {
        // Navigation
        public const string Home = "\uE8A5";
        public const string Steps = "\uE7C3";
        public const string Book = "\uE82D";
        public const string Lightning = "\uE8B8";
        public const string Document = "\uE8C8";
        public const string Wrench = "\uE90F";
        public const string Lightbulb = "\uEA80";
        public const string Question = "\uE897";
        public const string Info = "\uE77B";

        // Actions
        public const string Connect = "\uE839";
        public const string Play = "\uE768";
        public const string Monitor = "\uE8AB";
        public const string Restart = "\uE7BA";
        public const string Keyboard = "\uE72E";
        public const string Shield = "\uE72C";
        
        // Features
        public const string Star = "\uE946";
        public const string Speed = "\uE8FD";
        public const string Battery = "\uE8C1";
        public const string Backup = "\uE753";
        public const string Progress = "\uE8B7";
        public const string Wireless = "\uEA92";
    }

    #endregion

    #region Collections

    public ObservableCollection<HelpNavItem> NavItems { get; }
    public ObservableCollection<QuickStep> QuickSteps { get; }
    public ObservableCollection<FaqItem> Faq { get; }
    public ObservableCollection<TipItem> Tips { get; }

    public ICollectionView FilteredFaqView { get; }

    #endregion

    #region Properties

    private HelpNavItem _selectedNav = null!;
    public HelpNavItem SelectedNav
    {
        get => _selectedNav;
        set => SetField(ref _selectedNav, value);
    }

    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value))
            {
                FilteredFaqView.Refresh();
                OnPropertyChanged(nameof(IsFaqEmpty));
                OnPropertyChanged(nameof(SearchResultCount));
            }
        }
    }

    public bool IsFaqEmpty => FilteredFaqView.IsEmpty;

    public int SearchResultCount => FilteredFaqView switch
    {
        ListCollectionView listView => listView.Count,
        { } view => view.Cast<object>().Count(),
        _ => 0
    };

    #endregion

    #region Constructor

    public HelpViewModel()
    {
        NavItems = BuildNavItems();
        QuickSteps = BuildQuickSteps();
        Faq = BuildFaqItems();
        Tips = BuildTipItems();

        _selectedNav = NavItems[0];

        FilteredFaqView = CollectionViewSource.GetDefaultView(Faq);
        FilteredFaqView.Filter = FilterFaq;
    }

    #endregion

    #region Navigation Items

    private static ObservableCollection<HelpNavItem> BuildNavItems() => new()
    {
        new("welcome", "Welcome", Icons.Home),
        new("first-steps", "First Steps", Icons.Steps),
        new("basics", "The Basics", Icons.Book),
        new("quick-guide", "Quick Guide", Icons.Lightning),
        new("complete-guide", "Complete Guide", Icons.Document),
        new("troubleshooting", "Troubleshooting", Icons.Wrench),
        new("tips", "Tips & Tricks", Icons.Lightbulb),
        new("faq", "FAQ", Icons.Question),
        new("about", "About & Credits", Icons.Info)
    };

    #endregion

    #region Quick Steps

    private static ObservableCollection<QuickStep> BuildQuickSteps() => new()
    {
        new(Icons.Connect, 
            "Connect your device via USB or use Wireless Connection from the Home screen."),
        new(Icons.Play, 
            "Navigate to Optimize and press 'Start Optimization' to begin the automated enhancement process."),
        new(Icons.Monitor, 
            "Monitor the real-time console output to see exactly what's happening under the hood."),
        new(Icons.Restart, 
            "Restart your device when prompted to apply all optimizations and enjoy the results.")
    };

    #endregion

    #region FAQ Items

    private static ObservableCollection<FaqItem> BuildFaqItems() => new()
    {
        // Safety & Trust
        new("Is Zephyr's Elixir safe to use?",
            "Absolutely. Every optimization is non-invasive and fully reversible. " +
            "The application is designed with safety as the top priority. " +
            "You can restore default settings anytime from the Settings page, " +
            "and no system files are permanently modified without your explicit consent."),

        new("Will optimizations void my warranty?",
            "No. Zephyr's Elixir doesn't modify your bootloader or install custom ROMs. " +
            "All changes are made through standard Android APIs and can be reverted. " +
            "However, always check your device manufacturer's warranty terms to be certain."),

        // Requirements
        new("Do I need root access?",
            "No, root access is not required. Zephyr's Elixir uses ADB (Android Debug Bridge) " +
            "to perform optimizations, which only requires USB debugging to be enabled on your device. " +
            "Some advanced features may benefit from root, but core functionality works perfectly without it."),

        new("What Android versions are supported?",
            "Zephyr's Elixir supports Android 5.0 (Lollipop) and newer, " +
            "with full feature support on Android 10 and later. " +
            "Most features work best on Android 8.0+ where modern optimization APIs are available."),

        new("What are the system requirements?",
            "You need Windows 10 or Windows 11 (64-bit recommended), " +
            "ADB installed and accessible, an Android device with USB Debugging enabled, " +
            "and a USB cable or wireless network connection."),

        // Connectivity
        new("Can I use the app wirelessly?",
            "Yes! Use the 'Wireless Connection' feature on the Home screen to set up ADB over Wi-Fi. " +
            "Both Android 10 (TCP/IP mode) and Android 11+ (Wireless Debugging with pairing code) are supported. " +
            "Once configured, you can disconnect the USB cable and continue optimizing wirelessly."),

        new("Why is my device not being detected?",
            "Make sure USB debugging is enabled in Developer Options on your Android device. " +
            "Check that your USB cable supports data transfer (not just charging). " +
            "Try different USB ports or cables if the issue persists. " +
            "Verify ADB is installed by checking Settings → ADB Status. " +
            "Install your device's USB drivers if on Windows."),

        new("What if wireless connection fails?",
            "Ensure both devices are on the same Wi-Fi network. " +
            "Check that your router doesn't have client isolation (AP isolation) enabled. " +
            "For Android 10, you must connect via USB first to enable TCP/IP mode. " +
            "For Android 11+, ensure Wireless Debugging is enabled and use the correct port and pairing code. " +
            "Some corporate or public networks block ADB over Wi-Fi for security."),

        // Functionality
        new("What happens during optimization?",
            "The optimization process executes multiple stages: cache clearing (100 iterations), " +
            "memory management (terminating non-essential background processes), deep cleaning, " +
            "network optimization, animation adjustment, package compilation, and DEX optimization. " +
            "All actions are logged in real-time through the Optimization Console."),

        new("How often should I run optimizations?",
            "After initial optimization, your device should maintain improved performance. " +
            "You need to re-run optimizations after any Android update or if you clear app caches. " +
            "There's no need to optimize daily—once after setup and after major updates is typically sufficient."),

        new("Can I customize which optimizations are applied?",
            "Yes! While the automated 'Optimize' mode handles everything for you, " +
            "the 'Advanced' section gives you granular control over each optimization category. " +
            "You can configure Private DNS, animation speeds, advertising ID, and more individually."),

        new("What's the difference between Standard and Extreme Mode?",
            "Standard Mode (speed) focuses on faster compilation of frequently used code paths. " +
            "Extreme Mode (everything) performs comprehensive compilation of all application code " +
            "for maximum performance. Extreme Mode is recommended for older devices."),

        // Recovery & Troubleshooting
        new("What happens if something goes wrong?",
            "Every change made by Zephyr's Elixir can be undone. " +
            "Go to Advanced → Session Time Machine → Reset All to revert all session optimizations. " +
            "For a complete restore, use Settings → Restore Defaults."),

        new("Where can I find the logs?",
            "Navigate to Settings → Export Diagnostic Log to save comprehensive diagnostic information " +
            "including application version, system info, ADB status, device information, " +
            "and complete operation history with timestamps. " +
            "This is useful for troubleshooting or sharing diagnostics."),

        // Privacy & Data
        new("Does the app collect my data?",
            "No. Zephyr's Elixir is fully offline and doesn't collect, transmit, or store any personal data. " +
            "All operations are performed locally between your PC and device. " +
            "No analytics or tracking is implemented. " +
            "Crash reports are only saved if you explicitly export them. Your privacy is paramount."),

        // Pro Features
        new("What features require Pro license?",
            "Pro features include: Screen Mirror (mirror and record device screen), " +
            "Multi-APK Install (queue and install multiple APK files), " +
            "Reset Advertising ID, Disable Safety Core, Captive Portal Control, " +
            "Google Core Control, and RAM Expansion Control."),

        new("How do I activate Pro?",
            "Navigate to Settings → Pro License. Note your Device ID, enter your license key, " +
            "and click Activate. Pro features become immediately available upon successful activation. " +
            "Offline validation is supported with a grace period if you lose internet connectivity."),

        // Installation Errors
        new("What do APK installation errors mean?",
            "ALREADY_EXISTS: App is already installed (uninstall first or use update flag). " +
            "INVALID_APK: The APK file is corrupted or malformed. " +
            "INSUFFICIENT_STORAGE: Not enough storage space on device. " +
            "VERSION_DOWNGRADE: Cannot install older version over newer one. " +
            "NO_CERTIFICATES: APK is not properly signed. " +
            "OLDER_SDK: App requires a newer Android version. " +
            "MISSING_SPLIT: Split APK bundle is incomplete.")
    };

    #endregion

    #region Tips Items

    private static ObservableCollection<TipItem> BuildTipItems() => new()
    {
        new(Icons.Keyboard, "Keyboard Shortcut",
            "Press F1 anywhere in the app to instantly open the Help section. " +
            "The Wireless Connection dialog also supports Enter key to submit."),

        new(Icons.Speed, "Performance Tip",
            "After optimizations, disable unused system apps in the Debloat section for even better performance. " +
            "Use the AI-powered safety analysis to identify apps safe to remove."),

        new(Icons.Battery, "Battery Boost",
            "The optimization process includes battery life improvements. " +
            "You should notice extended usage time after the first full charge cycle post-optimization."),

        new(Icons.Progress, "Progress Tracking",
            "Watch the console during optimization—it's not just diagnostic info, " +
            "it shows you exactly what's being enhanced and why, command by command."),

        new(Icons.Backup, "Backup Reminder",
            "Use 'Uninstall with Backup' in Debloat to create local APK backups before removing apps. " +
            "This enables easy restoration from the History section."),

        new(Icons.Wireless, "Wireless Workflow",
            "Set up wireless ADB once, and you'll never need to plug in your phone again. " +
            "Works from anywhere on your network. Supports both Android 10 TCP/IP and Android 11+ pairing."),

        new(Icons.Shield, "Safety Net",
            "Every change in Advanced section is tracked. The Session Time Machine at the bottom " +
            "shows how many operations can be reverted. Click Reset All to undo all session changes."),

        new(Icons.Star, "Pro Features",
            "Upgrade to Pro for Screen Mirror with recording, multi-APK installation queue, " +
            "advertising ID reset, and advanced privacy controls like Safety Core and Captive Portal management.")
    };

    #endregion

    #region Filtering

    private bool FilterFaq(object item)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
            return true;

        if (item is not FaqItem faqItem)
            return false;

        return faqItem.Question.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
               faqItem.Answer.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region INotifyPropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    #endregion
}

#region Data Records

public sealed record HelpNavItem(string Key, string Title, string Icon);

public sealed record QuickStep(string Icon, string Text);

public sealed record FaqItem(string Question, string Answer);

public sealed record TipItem(string Icon, string Title, string Description);

#endregion
