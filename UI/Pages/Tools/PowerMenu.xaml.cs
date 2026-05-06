using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ZephyrsElixir.UI.Pages;

public sealed partial class PowerMenu : UserControl, INotifyPropertyChanged
{
    private readonly Action _closeAction;
    private readonly DispatcherTimer _spinnerTimer;
    private bool _isExecuting;
    private double _spinnerAngle;

    public event PropertyChangedEventHandler? PropertyChanged;

    public List<MenuItemBase> StandardOptions { get; }
    public List<MenuItemBase> AdvancedOptions { get; }
    public List<MenuItemBase> PowerOptions { get; }

    public PowerMenu(Action closeAction)
    {
        InitializeComponent();
        _closeAction = closeAction;

        StandardOptions = new List<MenuItemBase>
        {
            new("reboot", "\uE777", () => Strings.PowerMenu_Option_Reboot, () => Strings.PowerMenu_Option_Reboot_Desc, AppBrushes.GradientGreen, Color.FromRgb(0, 214, 143)),
            new("recovery", "\uE90F", () => Strings.PowerMenu_Option_Recovery, () => Strings.PowerMenu_Option_Recovery_Desc, AppBrushes.GradientApkm, Color.FromRgb(125, 100, 255)),
            new("bootloader", "\uE835", () => Strings.PowerMenu_Option_Bootloader, () => Strings.PowerMenu_Option_Bootloader_Desc, AppBrushes.GradientApk, Color.FromRgb(99, 181, 255))
        };

        AdvancedOptions = new List<MenuItemBase>
        {
            new("fastboot", "\uE943", () => Strings.PowerMenu_Option_Fastbootd, () => Strings.PowerMenu_Option_Fastbootd_Desc, AppBrushes.GradientCyan, Color.FromRgb(0, 191, 255)),
            new("sideload", "\uE896", () => Strings.PowerMenu_Option_Sideload, () => Strings.PowerMenu_Option_Sideload_Desc, AppBrushes.GradientApks, Color.FromRgb(255, 208, 0)),
            new("sideload_auto", "\uE8B5", () => Strings.PowerMenu_Option_SideloadAuto, () => Strings.PowerMenu_Option_SideloadAuto_Desc, AppBrushes.GradientOrange, Color.FromRgb(255, 159, 67)),
            new("download", "\uE118", () => Strings.PowerMenu_Option_Download, () => Strings.PowerMenu_Option_Download_Desc, AppBrushes.GradientNavy, Color.FromRgb(17, 117, 230))
        };

        PowerOptions = new List<MenuItemBase>
        {
            new("power_off", "\uE7E8", () => Strings.PowerMenu_Option_PowerOff, () => Strings.PowerMenu_Option_PowerOff_Desc, AppBrushes.GradientRed, Color.FromRgb(255, 107, 107))
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
        TranslationManager.Instance.LanguageChanged += OnLanguageChanged;
        UpdateDeviceStatus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        DeviceManager.Instance.DeviceStatusChanged -= OnDeviceStatusChanged;
        TranslationManager.Instance.LanguageChanged -= OnLanguageChanged;
        _spinnerTimer.Stop();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            foreach (var list in new[] { StandardOptions, AdvancedOptions, PowerOptions })
                foreach (var item in list) item.Refresh();
            UpdateDeviceStatus();
        });
    }

    private void OnDeviceStatusChanged(object? sender, bool isConnected) =>
        Dispatcher.BeginInvoke(UpdateDeviceStatus);

    private static readonly SolidColorBrush ConnectedBrush = AppBrushes.Green;
    private static readonly SolidColorBrush DisconnectedBrush = AppBrushes.Failed;
    private static readonly SolidColorBrush DeviceConnectedBrush = UIHelpers.FrozenSolid(255, 159, 67);
    private static readonly SolidColorBrush DeviceDisconnectedBrush = UIHelpers.FrozenSolid(128, 128, 128);

    private void UpdateDeviceStatus()
    {
        var dm = DeviceManager.Instance;
        var connected = dm.IsConnected;

        DeviceNameText.Text = connected ? dm.DeviceName : Strings.DeviceStatus_NoDevice;
        StatusText.Text = connected ? Strings.PowerMenu_Status_Connected : Strings.PowerMenu_Status_Disconnected;
        
        var statusBrush = connected ? ConnectedBrush : DisconnectedBrush;
        StatusText.Foreground = statusBrush;
        StatusDot.Fill = statusBrush;
        DeviceIcon.Foreground = connected ? DeviceConnectedBrush : DeviceDisconnectedBrush;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => _closeAction();

    private async void OnRebootOptionClick(object sender, RoutedEventArgs e)
    {
        if (_isExecuting || sender is not Button { Tag: string key }) return;

        if (!DeviceManager.Instance.IsConnected)
        {
            ShowResult(false, Strings.PowerMenu_NoDevice);
            return;
        }

        var option = FindOption(key);
        if (option == null) return;

        if (!DialogService.Instance.Confirm($"PowerMenu_Confirm_{UIHelpers.ToPascalCase(key)}", Window.GetWindow(this)))
            return;

        await ExecuteRebootCommandAsync(key, option.Title);
    }

    private MenuItemBase? FindOption(string key) =>
        StandardOptions.FirstOrDefault(o => o.Key == key) ??
        AdvancedOptions.FirstOrDefault(o => o.Key == key) ??
        PowerOptions.FirstOrDefault(o => o.Key == key);

    private async Task ExecuteRebootCommandAsync(string key, string title)
    {
        _isExecuting = true;
        ShowOperationStatus(string.Format(Strings.PowerMenu_Executing, title), Strings.PowerMenu_SendingAdb);
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
                "power_off" => "shell svc power shutdown",
                _ => null
            };

            if (command == null)
            {
                ShowResult(false, Strings.PowerMenu_Error_Unknown);
                return;
            }

            var output = await AdbExecutor.ExecuteCommandAsync(command);
            var success = !ContainsError(output);

            if (!success && key == "power_off")
            {
                output = await AdbExecutor.ExecuteCommandAsync("shell reboot -p");
                success = !ContainsError(output);
            }

            _spinnerTimer.Stop();
            HideOperationStatus();

            if (success)
            {
                ShowResult(true, string.Format(Strings.PowerMenu_Success_Rebooting, title));
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
            ShowResult(false, $"{Strings.Dialog_Title_Error}: {ex.Message}");
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
                => Strings.PowerMenu_Error_DeviceNotFound,
            _ when lower.Contains("unauthorized") 
                => Strings.PowerMenu_Error_Unauthorized,
            _ when lower.Contains("offline") 
                => Strings.PowerMenu_Error_Offline,
            _ when commandKey == "fastboot" && (lower.Contains("unknown") || lower.Contains("error")) 
                => Strings.PowerMenu_Error_FastbootNotSupported,
            _ when commandKey == "download" && (lower.Contains("unknown") || lower.Contains("error")) 
                => Strings.PowerMenu_Error_DownloadNotSupported,
            _ when commandKey == "sideload" && lower.Contains("error") 
                => Strings.PowerMenu_Error_SideloadNotAvailable,
            _ when lower.Contains("permission denied") 
                => Strings.PowerMenu_Error_PermissionDenied,
            _ when lower.Contains("protocol fault") 
                => Strings.PowerMenu_Error_Communication,
            _ => string.Format(Strings.PowerMenu_Error_CommandFailed, output.Length > 100 ? output[..100] + "..." : output)
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

    private static readonly SolidColorBrush SuccessIconBrush = AppBrushes.Green;
    private static readonly SolidColorBrush FailIconBrush = AppBrushes.Failed;
    private static readonly SolidColorBrush ResultTextBrush = UIHelpers.FrozenSolid(220, 220, 220);

    private static readonly SolidColorBrush SuccessBg = UIHelpers.FrozenSolid(50, 205, 50, 0x20);
    private static readonly SolidColorBrush SuccessBorder = UIHelpers.FrozenSolid(50, 205, 50, 0x40);
    private static readonly SolidColorBrush FailBg = UIHelpers.FrozenSolid(255, 107, 107, 0x20);
    private static readonly SolidColorBrush FailBorder = UIHelpers.FrozenSolid(255, 107, 107, 0x40);

    private void ShowResult(bool success, string message)
    {
        Dispatcher.BeginInvoke(() =>
        {
            ResultPanel.Background = success ? SuccessBg : FailBg;
            ResultPanel.BorderBrush = success ? SuccessBorder : FailBorder;
            ResultPanel.BorderThickness = new Thickness(1);

            ResultIcon.Text = success ? "\uE73E" : "\uE711";
            ResultIcon.Foreground = success ? SuccessIconBrush : FailIconBrush;

            ResultText.Text = message;
            ResultText.Foreground = ResultTextBrush;

            ResultPanel.Visibility = Visibility.Visible;
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}