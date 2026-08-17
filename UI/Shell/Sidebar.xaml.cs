
namespace ZephyrsElixir.UI.Shell;

public partial class Sidebar : UserControl
{
    private static readonly Dictionary<string, string> ButtonNameToKey = new()
    {
        ["BtnHome"] = "Home",
        ["BtnOptimize"] = "Optimize",
        ["BtnDebloat"] = "Debloat",
        ["BtnRecipes"] = "Recipes",
        ["BtnTools"] = "Tools",
        ["BtnAdvanced"] = "Advanced",
        ["BtnSettings"] = "Settings",
        ["BtnHelp"] = "Help"
    };

    private sealed record DeviceListItem(string Name, string Serial, bool IsAuthorized, bool IsActive);

    public Sidebar()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        TranslationManager.Instance.LanguageChanged += OnLanguageChanged;
    }

    #region Lifecycle

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        this.SubscribeToDeviceUpdates(
            onStatusChanged: OnDeviceStatusChanged,
            onInfoUpdated: UpdateDeviceInfo
        );

        DeviceManager.Instance.DevicesChanged += OnDevicesChanged;
        RefreshDeviceDisplay();
        RefreshDeviceList(DeviceManager.Instance.Devices);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        DeviceManager.Instance.DevicesChanged -= OnDevicesChanged;

    private void OnDevicesChanged(object? sender, IReadOnlyList<AndroidDevice> devices) =>
        Dispatcher.BeginInvoke(() => RefreshDeviceList(devices));

    private void RefreshDeviceList(IReadOnlyList<AndroidDevice> devices)
    {
        var active = DeviceManager.Instance.ActiveSerial;
        DeviceList.ItemsSource = devices
            .Select(d => new DeviceListItem(d.Name, d.Serial, d.IsAuthorized, d.Serial == active))
            .ToList();

        NoDevicesText.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DeviceCountBadge.Visibility = devices.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        DeviceCountText.Text = devices.Count.ToString(CultureInfo.InvariantCulture);
    }

    private void OnDevicePanelClick(object sender, RoutedEventArgs e)
    {
        RefreshDeviceList(DeviceManager.Instance.Devices);
        DevicePopup.IsOpen = !DevicePopup.IsOpen;
    }

    private void OnDeviceItemClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is DeviceListItem item)
            DeviceManager.Instance.SetActiveDevice(item.Serial);
        DevicePopup.IsOpen = false;
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