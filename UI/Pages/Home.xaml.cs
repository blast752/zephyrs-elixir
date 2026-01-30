
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
        
        TxtVersion.Text = $"{Strings.Home_Version} {AppVersion.Value}";
        Loaded += OnLoaded;
    }

    #region Lifecycle

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        this.SubscribeToDeviceState(BtnOptimize);
        
        (Resources["Anim.Entry"] as Storyboard)?.Begin();
        
        if (Resources["Anim.PulseGlow"] is Storyboard pulse)
            MascotGlow.BeginStoryboard(pulse);
    }

    #endregion

    #region Event Handlers

    private void OnStartOptimizationClick(object sender, RoutedEventArgs e) 
        => _navigate("Optimize");

    private void OnWirelessConnectionClick(object sender, RoutedEventArgs e)
    {
        new WirelessConnectionDialog(
            async args => await MainWindow.ExecuteAdbCommandWithOutputAsync(args),
            msg => Dispatcher.Invoke(() => DialogService.Instance.ShowInfoDirect(
                Strings.WirelessConnection_Log_Title, msg, Window.GetWindow(this))))
        { 
            Owner = Window.GetWindow(this) 
        }.ShowDialog();
    }

    private void OnChangelogClick(object sender, RoutedEventArgs e)
        => DialogService.Instance.ShowChangelog(Window.GetWindow(this));

    private void OnBannerClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://play.google.com/store/apps/details?id=com.paget96.batteryguru",
                UseShellExecute = true
            });
        }
        catch { /* Ignore navigation errors */ }
    }

    private void OnBannerCloseClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        AnimateBannerClose();
    }

    #endregion

    #region Helpers

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

    #endregion
}