
namespace ZephyrsElixir.UI.Dialogs;

public sealed partial class WirelessConnectionDialog : Window
{
    private static class Config
    {
        public const int DefaultPort = 5555;
        public const int PairCodeLength = 6;
        public const int MaxPort = 65535;
        public const int MinOctet = 1;
        public const int MaxOctet = 254;
        public const int ConnectionDelayMs = 1000;
        public const int SuccessCloseDelayMs = 2500;
        public const int LoadingDotCount = 8;
        public const string FallbackNetworkPrefix = "192.168.1.";

        // After a successful "adb pair", adb auto-connects to the paired device via mDNS;
        // poll the device list so success is only reported when the device is really online.
        public const int PostPairConnectAttempts = 5;
        public const int PostPairConnectPollMs = 2000;
        public const int MaxErrorDetailLength = 160;
    }

    private readonly Func<string, Task<string>> _executeAdbCommand;
    private readonly Action<string> _appendTerminal;
    private readonly string _networkPrefix;

    public WirelessConnectionDialog(Func<string, Task<string>> executeAdbCommand, Action<string> appendTerminal)
    {
        _executeAdbCommand = executeAdbCommand ?? throw new ArgumentNullException(nameof(executeAdbCommand));
        _appendTerminal = appendTerminal ?? throw new ArgumentNullException(nameof(appendTerminal));
        
        _networkPrefix = DetectNetworkPrefix();

        InitializeComponent();
        InitializeNetworkDisplay();
        InitializeLoadingAnimation();
    }

    private void InitializeNetworkDisplay()
    {
        IpPrefixText.Text = _networkPrefix;
        PairIpPrefixText.Text = _networkPrefix;

        Loaded += (_, _) =>
        {
            UpdatePanelVisibility();
            FocusActiveInput();
        };
    }

    private void InitializeLoadingAnimation()
    {
        var brush = (Brush)FindResource("App.Brush.DeepSkyBlue");
        
        for (var i = 0; i < Config.LoadingDotCount; i++)
        {
            var angle = i * 45 * Math.PI / 180;
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = brush,
                Opacity = 0.3
            };

            Canvas.SetLeft(dot, 20 + 12 * Math.Cos(angle) - 3);
            Canvas.SetTop(dot, 20 + 12 * Math.Sin(angle) - 3);
            LoadingCanvas.Children.Add(dot);

            var animation = new DoubleAnimation(0.3, 1.0, TimeSpan.FromSeconds(0.8))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(i * 100)
            };
            dot.BeginAnimation(OpacityProperty, animation);
        }
    }

    private void OnVersionChanged(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        
        UpdatePanelVisibility();
        ClearStatus();
        SuccessMessage.Visibility = Visibility.Collapsed;
        
        Dispatcher.BeginInvoke(FocusActiveInput, DispatcherPriority.Input);
    }

    private void UpdatePanelVisibility()
    {
        var isAndroid10 = Android10Radio.IsChecked == true;

        Android10Panel.Visibility = isAndroid10 ? Visibility.Visible : Visibility.Collapsed;
        Android11Panel.Visibility = isAndroid10 ? Visibility.Collapsed : Visibility.Visible;

        ActionButton.Content = isAndroid10 ? Strings.Wireless_Button_Connect : Strings.Wireless_Button_Pair;
        StepIndicator.Text = isAndroid10 ? Strings.Wireless_Status_EnterIP : Strings.Wireless_Status_EnterPairing;
        ConnectionProgress.Value = 50;
    }

    private void FocusActiveInput()
    {
        if (Android10Radio.IsChecked == true)
            IpLastOctetTextBox.Focus();
        else
            PairIpLastOctetTextBox.Focus();
    }

    private async void OnActionClick(object sender, RoutedEventArgs e)
    {
        await PerformConnectionAsync();
    }

    private async void OnEnterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await PerformConnectionAsync();
            e.Handled = true;
        }
    }

    private void OnDigitOnlyPreviewInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !e.Text.All(char.IsDigit);
    }

    private async Task PerformConnectionAsync()
    {
        SetLoadingState(true);

        try
        {
            var success = Android10Radio.IsChecked == true
                ? await HandleAndroid10ConnectionAsync()
                : await HandleAndroid11PairingAsync();

            if (success)
            {
                // The header close button stays live through the success pause, and setting the result
                // on a window the user already closed throws and reports the connection as cancelled.
                await Task.Delay(Config.SuccessCloseDelayMs);
                if (!IsVisible) return;
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            ShowStatus(string.Format(Strings.Wireless_Status_OperationFailed, ex.Message), StatusType.Error);
            ConnectionProgress.Value = 50;
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    private async Task<bool> HandleAndroid10ConnectionAsync()
    {
        var lastOctet = IpLastOctetTextBox.Text?.Trim() ?? string.Empty;

        if (!ValidateInput(lastOctet, InputType.LastOctet))
            return false;

        var endpoint = $"{_networkPrefix}{lastOctet}:{Config.DefaultPort}";

        ShowStatus(Strings.Wireless_Status_Connecting, StatusType.Progress);
        ConnectionProgress.Value = 75;
        LogToTerminal($"[Android 10] {Strings.Wireless_Status_Connecting} {endpoint}...");

        // "adb tcpip" needs a USB-connected device; surface its failure instead of pretending.
        var tcpipOutput = await _executeAdbCommand($"tcpip {Config.DefaultPort}");
        LogToTerminal(tcpipOutput);
        if (IsAdbFailure(tcpipOutput))
        {
            ShowFailure(tcpipOutput);
            return false;
        }

        await Task.Delay(Config.ConnectionDelayMs);

        // Success is "connected to ..." / "already connected to ..."; anything else is a failure.
        var connectOutput = await _executeAdbCommand($"connect {endpoint}");
        LogToTerminal(connectOutput);
        if (!connectOutput.Contains("connected to", StringComparison.OrdinalIgnoreCase))
        {
            ShowFailure(connectOutput);
            return false;
        }

        ShowStatus(string.Format(Strings.Wireless_Status_ConnectionInitiated, endpoint), StatusType.Success);
        ConnectionProgress.Value = 100;

        return true;
    }

    private async Task<bool> HandleAndroid11PairingAsync()
    {
        var lastOctet = PairIpLastOctetTextBox.Text?.Trim() ?? string.Empty;
        var port = PairPortTextBox.Text?.Trim() ?? string.Empty;
        var code = PairCodeTextBox.Text?.Trim() ?? string.Empty;

        if (!ValidateInput(lastOctet, InputType.LastOctet) ||
            !ValidateInput(port, InputType.Port) ||
            !ValidateInput(code, InputType.PairingCode))
            return false;

        var fullIp = $"{_networkPrefix}{lastOctet}";

        ShowStatus(Strings.Wireless_Status_Pairing, StatusType.Progress);
        ConnectionProgress.Value = 60;
        LogToTerminal($"[Android 11+] {Strings.Wireless_Status_Pairing} {fullIp}:{port}...");

        var pairOutput = await _executeAdbCommand($"pair {fullIp}:{port} {code}");
        LogToTerminal(pairOutput);

        if (!pairOutput.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase))
        {
            ShowFailure(pairOutput);
            return false;
        }

        // Pairing alone does not connect: adb auto-connects to paired devices via mDNS.
        // Poll the device list so the dialog only auto-closes when the device is truly online.
        ShowStatus(Strings.Wireless_Status_Connecting, StatusType.Progress);
        ConnectionProgress.Value = 85;

        for (var attempt = 0; attempt < Config.PostPairConnectAttempts; attempt++)
        {
            await Task.Delay(Config.PostPairConnectPollMs);
            var devices = await _executeAdbCommand("devices");
            if (HasOnlineWirelessDevice(devices))
            {
                LogToTerminal("Device connected via wireless debugging.");
                ShowStatus(Strings.Wireless_Status_PairingComplete, StatusType.Success);
                ConnectionProgress.Value = 100;
                SuccessMessage.Visibility = Visibility.Visible;
                return true;
            }
        }

        // Paired, but the auto-connect hasn't landed yet: report the pairing honestly and
        // keep the dialog open so the user sees the next-step hint instead of a silent close.
        ShowStatus(Strings.Wireless_Status_PairingComplete, StatusType.Success);
        ConnectionProgress.Value = 100;
        SuccessMessage.Visibility = Visibility.Visible;
        return false;
    }

    /// <summary>Online wireless entries are "ip:port&#9;device" (TCP) or "adb-SERIAL-…&#9;device" (mDNS).</summary>
    private static bool HasOnlineWirelessDevice(string devicesOutput)
    {
        foreach (var line in devicesOutput.SplitLines())
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("List of", StringComparison.OrdinalIgnoreCase)) continue;
            if (!trimmed.EndsWith("device", StringComparison.OrdinalIgnoreCase)) continue;
            if (trimmed.Contains(':') || trimmed.StartsWith("adb-", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool IsAdbFailure(string output) =>
        output.Contains("error", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("no devices", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("cannot", StringComparison.OrdinalIgnoreCase) ||
        output.Contains("unable to", StringComparison.OrdinalIgnoreCase);

    private void ShowFailure(string adbOutput)
    {
        var detail = adbOutput.Trim();
        if (detail.Length > Config.MaxErrorDetailLength)
            detail = detail[..(Config.MaxErrorDetailLength - 3)] + "...";
        if (detail.Length == 0)
            detail = "ADB";

        ShowStatus(string.Format(Strings.Wireless_Status_OperationFailed, detail), StatusType.Error);
        ConnectionProgress.Value = 50;
    }

    private void LogToTerminal(string message) => _appendTerminal($"{message}\n");

    private enum InputType { LastOctet, Port, PairingCode }

    private bool ValidateInput(string value, InputType type)
    {
        var (isValid, errorMessage) = type switch
        {
            InputType.LastOctet => ValidateLastOctet(value),
            InputType.Port => ValidatePort(value),
            InputType.PairingCode => ValidatePairingCode(value),
            _ => (false, Strings.Common_Status_Error)
        };

        if (!isValid)
            ShowStatus(errorMessage, StatusType.Error);

        return isValid;
    }

    private static (bool IsValid, string ErrorMessage) ValidateLastOctet(string octet)
    {
        if (string.IsNullOrWhiteSpace(octet))
            return (false, Strings.Wireless_Validation_EnterLastIP);
        
        if (!int.TryParse(octet, out var number))
            return (false, Strings.Wireless_Validation_InvalidNumber);
        
        if (number < Config.MinOctet || number > Config.MaxOctet)
            return (false, string.Format(Strings.Wireless_Validation_OctetRange, Config.MinOctet, Config.MaxOctet));
        
        return (true, string.Empty);
    }

    private static (bool IsValid, string ErrorMessage) ValidatePort(string port)
    {
        if (string.IsNullOrWhiteSpace(port))
            return (false, Strings.Wireless_Validation_EnterPort);
        
        if (!int.TryParse(port, out var portNumber))
            return (false, Strings.Wireless_Validation_InvalidPort);
        
        if (portNumber <= 0 || portNumber > Config.MaxPort)
            return (false, string.Format(Strings.Wireless_Validation_PortRange, Config.MaxPort));
        
        return (true, string.Empty);
    }

    private static (bool IsValid, string ErrorMessage) ValidatePairingCode(string code) =>
        code.Length != Config.PairCodeLength || !code.All(char.IsDigit)
            ? (false, string.Format(Strings.Wireless_Validation_PairingCode, Config.PairCodeLength))
            : (true, string.Empty);

    private enum StatusType { Progress, Success, Error }

    private void SetLoadingState(bool isLoading)
    {
        ActionButton.IsEnabled = !isLoading;
        CancelButton.IsEnabled = !isLoading;
        LoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

        Android10Radio.IsEnabled = !isLoading;
        Android11Radio.IsEnabled = !isLoading;

        var isAndroid10 = Android10Radio.IsChecked == true;
        IpLastOctetTextBox.IsEnabled = !isLoading && isAndroid10;
        PairIpLastOctetTextBox.IsEnabled = !isLoading && !isAndroid10;
        PairPortTextBox.IsEnabled = !isLoading && !isAndroid10;
        PairCodeTextBox.IsEnabled = !isLoading && !isAndroid10;
    }

    private static readonly SolidColorBrush ProgressBrush = UIHelpers.FrozenSolid(0x00, 0xBF, 0xFF);
    private static readonly SolidColorBrush SuccessBrush = UIHelpers.FrozenSolid(0x90, 0xEE, 0x90);
    private static readonly SolidColorBrush ErrorBrush = UIHelpers.FrozenSolid(0xFF, 0x63, 0x47);

    private void ShowStatus(string message, StatusType type)
    {
        var brush = type switch
        {
            StatusType.Progress => ProgressBrush,
            StatusType.Success => SuccessBrush,
            StatusType.Error => ErrorBrush,
            _ => Brushes.White
        };

        StatusText.Text = message;
        StatusText.Foreground = brush;
        StatusIcon.Foreground = brush;
        StatusIcon.Kind = type switch
        {
            StatusType.Progress => "sync",
            StatusType.Success => "check-circle",
            StatusType.Error => "error-circle",
            _ => "info"
        };
        StatusPanel.Visibility = Visibility.Visible;

        StatusPanel.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    private void ClearStatus()
    {
        StatusPanel.Visibility = Visibility.Collapsed;
    }

    private static string DetectNetworkPrefix()
    {
        try
        {
            // Prefer the adapter that actually routes to the LAN: an IPv4 default gateway filters
            // out virtual adapters (Hyper-V, VMware, VPN TAP) whose subnet would mislead the user.
            // Among routed adapters, Wi-Fi wins — that's where the phone lives.
            var best = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsActiveNetworkInterface)
                .Select(ni => (Interface: ni, Props: ni.GetIPProperties()))
                .Select(x => (
                    x.Interface,
                    HasGateway: x.Props.GatewayAddresses.Any(g =>
                        g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)),
                    Address: x.Props.UnicastAddresses.FirstOrDefault(IsValidIpv4Address)))
                .Where(x => x.Address is not null)
                .OrderByDescending(x => x.HasGateway)
                .ThenByDescending(x => x.Interface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                .FirstOrDefault();

            if (best.Address is not null)
            {
                var octets = best.Address.Address.ToString().Split('.');
                if (octets.Length == 4)
                {
                    return $"{octets[0]}.{octets[1]}.{octets[2]}.";
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WirelessConnectionDialog] Network prefix detection failed: {ex.Message}");
        }

        return Config.FallbackNetworkPrefix;
    }

    private static bool IsActiveNetworkInterface(NetworkInterface ni) =>
        ni.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet &&
        ni.OperationalStatus == OperationalStatus.Up;

    private static bool IsValidIpv4Address(UnicastIPAddressInformation ip) =>
        ip.Address.AddressFamily == AddressFamily.InterNetwork &&
        !IPAddress.IsLoopback(ip.Address);
}
