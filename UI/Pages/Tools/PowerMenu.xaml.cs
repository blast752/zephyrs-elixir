
namespace ZephyrsElixir.UI.Pages;

public sealed partial class PowerMenu : UserControl, INotifyPropertyChanged
{
    private readonly Action _closeAction;
    private readonly DispatcherTimer _spinnerTimer;
    private bool _isExecuting;
    private double _spinnerAngle;

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<PowerMenuItem> StandardOptions { get; }
    public List<PowerMenuItem> AdvancedOptions { get; }
    public List<PowerMenuItem> PowerOptions { get; }

    public PowerMenu(Action closeAction)
    {
        InitializeComponent();
        _closeAction = closeAction;

        StandardOptions = new List<PowerMenuItem>
        {
            new("reboot", "\uE777", "Reboot", "Restart your device normally",
                UIHelpers.CreateGradientBrush("#00D68F", "#00B377"), Color.FromRgb(0, 214, 143)),
            new("recovery", "\uE90F", "Recovery Mode", "Boot into recovery for system updates and factory reset",
                UIHelpers.CreateGradientBrush("#7D64FF", "#5A3FD9"), Color.FromRgb(125, 100, 255)),
            new("bootloader", "\uE835", "Bootloader / Fastboot", "Boot into bootloader for flashing and unlocking",
                UIHelpers.CreateGradientBrush("#63B5FF", "#1175E6"), Color.FromRgb(99, 181, 255))
        };

        AdvancedOptions = new List<PowerMenuItem>
        {
            new("fastboot", "\uE943", "Fastbootd (Userspace)", "Boot into userspace fastboot for dynamic partitions (Android 10+)",
                UIHelpers.CreateGradientBrush("#00BFFF", "#0099CC"), Color.FromRgb(0, 191, 255)),
            new("sideload", "\uE896", "Sideload Mode", "Boot into sideload for ADB OTA package installation",
                UIHelpers.CreateGradientBrush("#FFD000", "#CC9900"), Color.FromRgb(255, 208, 0)),
            new("sideload_auto", "\uE8B5", "Sideload (Auto-reboot)", "Sideload mode with automatic reboot after installation",
                UIHelpers.CreateGradientBrush("#FF9F43", "#E67E22"), Color.FromRgb(255, 159, 67)),
            new("download", "\uE118", "Download Mode", "Boot into download/Odin mode (Samsung devices)",
                UIHelpers.CreateGradientBrush("#1175E6", "#0D3A78"), Color.FromRgb(17, 117, 230))
        };

        PowerOptions = new List<PowerMenuItem>
        {
            new("power_off", "\uE7E8", "Power Off", "Shut down your device completely",
                UIHelpers.CreateGradientBrush("#FF6B6B", "#DC143C"), Color.FromRgb(255, 107, 107))
        };

        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _spinnerTimer.Tick += (_, _) =>
        {
            _spinnerAngle = (_spinnerAngle + 6) % 360;
            SpinnerRotation.Angle = _spinnerAngle;
        };

        DataContext = this;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        DeviceManager.Instance.DeviceStatusChanged += OnDeviceStatusChanged;
        UpdateDeviceStatus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DeviceManager.Instance.DeviceStatusChanged -= OnDeviceStatusChanged;
        _spinnerTimer.Stop();
    }

    private void OnDeviceStatusChanged(object? sender, bool isConnected) =>
        Dispatcher.BeginInvoke(UpdateDeviceStatus);

    private void UpdateDeviceStatus()
    {
        var dm = DeviceManager.Instance;
        var connected = dm.IsConnected;

        DeviceNameText.Text = connected ? dm.DeviceName : "No device connected";
        StatusText.Text = connected ? "Connected" : "Disconnected";
        
        var statusColor = Color.FromRgb(connected ? (byte)50 : (byte)255, 
                                         connected ? (byte)205 : (byte)107, 
                                         connected ? (byte)50 : (byte)107);
        StatusText.Foreground = new SolidColorBrush(statusColor);
        StatusDot.Fill = new SolidColorBrush(statusColor);
        DeviceIcon.Foreground = new SolidColorBrush(connected 
            ? Color.FromRgb(255, 159, 67) 
            : Color.FromRgb(128, 128, 128));
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => _closeAction();

    private async void OnRebootOptionClick(object sender, RoutedEventArgs e)
    {
        if (_isExecuting || sender is not Button { Tag: string key }) return;

        if (!DeviceManager.Instance.IsConnected)
        {
            ShowResult(false, "No device connected. Please connect a device first.");
            return;
        }

        var option = FindOption(key);
        if (option == null) return;

        var confirmation = key switch
        {
            "power_off" => "Are you sure you want to power off your device?",
            "bootloader" => "This will boot into bootloader mode. Continue?",
            "recovery" => "This will boot into recovery mode. Continue?",
            "fastboot" => "This will boot into fastbootd (userspace fastboot). Continue?",
            _ => null
        };

        if (!DialogService.Instance.Confirm($"PowerMenu_Confirm_{UIHelpers.ToPascalCase(key)}", Window.GetWindow(this)))
        return;

        await ExecuteRebootCommandAsync(key, option.Title);
    }

    private PowerMenuItem? FindOption(string key) =>
        StandardOptions.FirstOrDefault(o => o.Key == key) ??
        AdvancedOptions.FirstOrDefault(o => o.Key == key) ??
        PowerOptions.FirstOrDefault(o => o.Key == key);

    private async Task ExecuteRebootCommandAsync(string key, string title)
    {
        _isExecuting = true;
        ShowOperationStatus($"Executing {title}...", "Sending ADB command to device");
        _spinnerTimer.Start();

        try
        {
            var command = key switch
            {
                "reboot" => "reboot",
                "recovery" => "reboot recovery",
                "bootloader" => "reboot bootloader",
                "fastboot" => "reboot fastboot",
                "sideload" => "reboot sideload",
                "sideload_auto" => "reboot sideload-auto-reboot",
                "download" => "reboot download",
                "power_off" => "shell reboot -p",
                _ => null
            };

            if (command == null)
            {
                ShowResult(false, "Unknown command");
                return;
            }

            var output = await AdbExecutor.ExecuteCommandAsync(command);
            var success = !ContainsError(output);

            _spinnerTimer.Stop();
            HideOperationStatus();

            if (success)
            {
                ShowResult(true, $"{title} sent. Rebooting...");
                AdbLogger.Instance.LogSuccess("PowerMenu", $"{title} executed successfully");
            }
            else
            {
                var friendlyError = ParseRebootError(output, key);
                ShowResult(false, friendlyError);
                AdbLogger.Instance.LogError("PowerMenu", $"{title} failed: {output}");
            }
        }
        catch (Exception ex)
        {
            _spinnerTimer.Stop();
            HideOperationStatus();
            ShowResult(false, $"Error: {ex.Message}");
            AdbLogger.Instance.LogError("PowerMenu", $"Exception during {title}: {ex.Message}");
        }
        finally
        {
            _isExecuting = false;
        }
    }

    private static bool ContainsError(string output)
    {
        var lowerOutput = output.ToLowerInvariant();
        return lowerOutput.Contains("error") || lowerOutput.Contains("failed") || 
               lowerOutput.Contains("not found") || lowerOutput.Contains("no devices") || 
               lowerOutput.Contains("unauthorized") || lowerOutput.Contains("offline");
    }

    private static string ParseRebootError(string output, string commandKey)
    {
        var lower = output.ToLowerInvariant();

        return lower switch
        {
            _ when lower.Contains("no devices") || lower.Contains("device not found") 
                => "Device not found. Please reconnect your device.",
            _ when lower.Contains("unauthorized") 
                => "USB debugging not authorized. Please accept the prompt on your device.",
            _ when lower.Contains("offline") 
                => "Device is offline. Try reconnecting the USB cable.",
            _ when commandKey == "fastboot" && (lower.Contains("unknown") || lower.Contains("error")) 
                => "Fastbootd mode not supported. This feature requires Android 10 or newer with dynamic partitions.",
            _ when commandKey == "download" && (lower.Contains("unknown") || lower.Contains("error")) 
                => "Download mode not supported on this device. This is typically a Samsung-only feature.",
            _ when commandKey == "sideload" && lower.Contains("error") 
                => "Sideload mode not available. Your device may not support direct sideload boot.",
            _ when lower.Contains("permission denied") 
                => "Permission denied. This operation may require root access.",
            _ when lower.Contains("protocol fault") 
                => "Communication error. Please try again.",
            _ => $"Command failed: {(output.Length > 100 ? output[..100] + "..." : output)}"
        };
    }

    private void ShowOperationStatus(string message, string detail)
    {
        Dispatcher.BeginInvoke(() =>
        {
            OperationText.Text = message;
            OperationDetailText.Text = detail;
            OperationPanel.Visibility = Visibility.Visible;
            ResultPanel.Visibility = Visibility.Collapsed;
        });
    }

    private void HideOperationStatus()
    {
        Dispatcher.BeginInvoke(() => OperationPanel.Visibility = Visibility.Collapsed);
    }

    private void ShowResult(bool success, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var color = success ? Color.FromRgb(50, 205, 50) : Color.FromRgb(255, 107, 107);
            
            ResultPanel.Background = new SolidColorBrush(Color.FromArgb(0x20, color.R, color.G, color.B));
            ResultPanel.BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, color.R, color.G, color.B));
            ResultPanel.BorderThickness = new Thickness(1);

            ResultIcon.Text = success ? "\uE73E" : "\uE711";
            ResultIcon.Foreground = new SolidColorBrush(color);

            ResultText.Text = message;
            ResultText.Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));

            ResultPanel.Visibility = Visibility.Visible;
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class PowerMenuItem
{
    public string Key { get; }
    public string Icon { get; }
    public string Title { get; }
    public string Description { get; }
    public Brush IconBrush { get; }
    public Color GlowColor { get; }

    public PowerMenuItem(string key, string icon, string title, string description, 
                            Brush iconBrush, Color glowColor)
    {
        Key = key;
        Icon = icon;
        Title = title;
        Description = description;
        IconBrush = iconBrush;
        GlowColor = glowColor;
    }
}