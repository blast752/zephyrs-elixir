
namespace ZephyrsElixir.UI.Pages;

public sealed partial class Home : UserControl
{
    private static readonly Lazy<string> AppVersion = new(GetAppVersion);

    private readonly Action<string> _navigate;

    public Home() : this(_ => { }) { }

    public Home(Action<string> requestNavigation)
    {
        _navigate = requestNavigation ?? throw new ArgumentNullException(nameof(requestNavigation));
        InitializeComponent();

        RefreshVersionText();
        TranslationManager.Instance.LanguageChanged += OnLanguageChanged;
        Loaded += OnLoaded;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshVersionText();

    private void RefreshVersionText() =>
        TxtVersion.Text = $"{Strings.Home_Version} {AppVersion.Value}";

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        this.SubscribeToDeviceState(BtnOptimize);

        (Resources["Anim.Entry"] as Storyboard)?.Begin();

        if (Resources["Anim.PulseGlow"] is Storyboard pulse)
            MascotGlow.BeginStoryboard(pulse);
    }

    private void OnStartOptimizationClick(object sender, RoutedEventArgs e)
        => _navigate("Optimize");

    private void OnWirelessConnectionClick(object sender, RoutedEventArgs e)
    {
        // The dialog parses each adb output itself; step-by-step lines go to the
        // diagnostic log instead of interrupting the flow with modal popups.
        new WirelessConnectionDialog(
            args => AdbExecutor.ExecuteCommandAsync(args),
            msg => AdbLogger.Instance.LogInfo("Wireless", msg.Trim()))
        {
            Owner = Window.GetWindow(this)
        }.ShowDialog();
    }

    private void OnChangelogClick(object sender, RoutedEventArgs e)
        => DialogService.Instance.ShowChangelog(Window.GetWindow(this));

    private void OnBannerClick(object sender, MouseButtonEventArgs e)
        => ShellUtils.OpenUrl("https://play.google.com/store/apps/details?id=com.paget96.batteryguru");

    private void OnBannerCloseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        AnimateBannerClose();
    }

    private void AnimateBannerClose()
    {
        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
        fade.Completed += (_, _) => PartnerBanner.Visibility = Visibility.Collapsed;

        PartnerBanner.BeginAnimation(OpacityProperty, fade);
        PartnerBanner.RenderTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(0, -20, TimeSpan.FromMilliseconds(300)));
    }

    private static string GetAppVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version is null) return "Unknown";

            var str = version.ToString(3);
            return version.Major == 0 ? $"{str} (Beta)" : str;
        }
        catch
        {
            return "Error";
        }
    }
}