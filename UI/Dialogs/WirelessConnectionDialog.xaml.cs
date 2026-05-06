
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
    }

    private readonly Func<string, Task> _executeAdbCommand;
    private readonly Action<string> _appendTerminal;
    private readonly string _networkPrefix;

    public WirelessConnectionDialog(Func<string, Task> executeAdbCommand, Action<string> appendTerminal)
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
                await Task.Delay(Config.SuccessCloseDelayMs);
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"❌ {string.Format(Strings.Wireless_Status_OperationFailed, ex.Message)}", StatusType.Error);
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

        var fullIp = $"{_networkPrefix}{lastOctet}";
        var port = Config.DefaultPort.ToString();

        ShowStatus(Strings.Wireless_Status_Connecting, StatusType.Progress);
        ConnectionProgress.Value = 75;
        LogToTerminal($"[Android 10] {Strings.Wireless_Status_Connecting} {fullIp}:{port}...");

        await _executeAdbCommand($"tcpip {port}");
        LogToTerminal("TCP/IP mode enabled");

        await Task.Delay(Config.ConnectionDelayMs);

        await _executeAdbCommand($"connect {fullIp}:{port}");
        LogToTerminal("Connection command sent");

        ShowStatus($"✔ {string.Format(Strings.Wireless_Status_ConnectionInitiated, $"{fullIp}:{port}")}", StatusType.Success);
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
        ConnectionProgress.Value = 75;
        LogToTerminal($"[Android 11+] {Strings.Wireless_Status_Pairing} {fullIp}:{port}...");

        await _executeAdbCommand($"pair {fullIp}:{port} {code}");
        LogToTerminal("Pairing command executed successfully");

        ShowStatus($"✔ {Strings.Wireless_Status_PairingComplete}", StatusType.Success);
        ConnectionProgress.Value = 100;
        SuccessMessage.Visibility = Visibility.Visible;

        return true;
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
        StatusText.Text = message;
        StatusText.Foreground = type switch
        {
            StatusType.Progress => ProgressBrush,
            StatusType.Success => SuccessBrush,
            StatusType.Error => ErrorBrush,
            _ => Brushes.White
        };
        StatusText.Visibility = Visibility.Visible;

        StatusText.BeginAnimation(OpacityProperty, 
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    private void ClearStatus()
    {
        StatusText.Visibility = Visibility.Collapsed;
    }

    private static string DetectNetworkPrefix()
    {
        try
        {
            var firstValidAddress = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsActiveNetworkInterface)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .FirstOrDefault(IsValidIpv4Address);

            if (firstValidAddress is not null)
            {
                var octets = firstValidAddress.Address.ToString().Split('.');
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
