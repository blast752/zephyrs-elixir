
namespace ZephyrsElixir.UI.Shell;

public partial class Sidebar : UserControl
{
    private static readonly Dictionary<string, string> ButtonNameToKey = new()
    {
        ["BtnHome"] = "Home",
        ["BtnOptimize"] = "Optimize",
        ["BtnDebloat"] = "Debloat",
        ["BtnTools"] = "Tools",
        ["BtnAdvanced"] = "Advanced",
        ["BtnSettings"] = "Settings",
        ["BtnHelp"] = "Help"
    };

    public Sidebar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        TranslationManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    #region Lifecycle

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        this.SubscribeToDeviceUpdates(
            onStatusChanged: OnDeviceStatusChanged,
            onInfoUpdated: UpdateDeviceInfo
        );
        
        RefreshDeviceDisplay();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshDeviceDisplay();
    }

    private void RefreshDeviceDisplay()
    {
        if (DeviceManager.Instance.IsConnected)
            UpdateDeviceInfo(DeviceManager.Instance.DeviceName, DeviceManager.Instance.BatteryLevel);
        else
            UpdateDeviceInfo(Strings.DeviceStatus_NoDevice, 0);
    }

    #endregion

    #region Device Events

    private void OnDeviceStatusChanged(bool isConnected)
    {
        if (!isConnected)
            UpdateDeviceInfo(Strings.DeviceStatus_NoDevice, 0);
    }

    private void UpdateDeviceInfo(string name, int battery)
    {
        DeviceStatusText = name;
        DeviceBattery = battery;
    }

    #endregion

    #region Navigation

    public static readonly RoutedEvent NavigateRequestedEvent =
        EventManager.RegisterRoutedEvent(
            nameof(NavigateRequested), 
            RoutingStrategy.Bubble, 
            typeof(RoutedEventHandler), 
            typeof(Sidebar));

    public event RoutedEventHandler NavigateRequested
    {
        add => AddHandler(NavigateRequestedEvent, value);
        remove => RemoveHandler(NavigateRequestedEvent, value);
    }

    private void OnNavigationClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Name: { } name } || 
            !ButtonNameToKey.TryGetValue(name, out var key))
            return;

        SelectedKey = key;
        RaiseEvent(new RoutedEventArgs(NavigateRequestedEvent));
    }

    #endregion

    #region Dependency Properties

    public static readonly DependencyProperty SelectedKeyProperty =
        DependencyProperty.Register(
            nameof(SelectedKey), 
            typeof(string), 
            typeof(Sidebar),
            new FrameworkPropertyMetadata("Home", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public string SelectedKey
    {
        get => (string)GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    public static readonly DependencyProperty DeviceStatusTextProperty =
        DependencyProperty.Register(
            nameof(DeviceStatusText), 
            typeof(string), 
            typeof(Sidebar), 
            new PropertyMetadata(string.Empty));

    public string DeviceStatusText
    {
        get => (string)GetValue(DeviceStatusTextProperty);
        set => SetValue(DeviceStatusTextProperty, value);
    }

    public static readonly DependencyProperty DeviceBatteryProperty =
        DependencyProperty.Register(
            nameof(DeviceBattery), 
            typeof(double), 
            typeof(Sidebar), 
            new PropertyMetadata(0.0));

    public double DeviceBattery
    {
        get => (double)GetValue(DeviceBatteryProperty);
        set => SetValue(DeviceBatteryProperty, value);
    }

    #endregion
}