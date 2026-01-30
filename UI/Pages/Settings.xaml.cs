
namespace ZephyrsElixir.UI.Pages;

public partial class Settings : UserControl
{
    private static string? _cachedAdbVersion;
    private static Style? _cachedAdbStatusStyle;

    public Settings()
    {
        InitializeComponent();
    }

    private async void OnSettingsLoaded(object sender, RoutedEventArgs e)
    {
        if (_cachedAdbVersion != null && _cachedAdbStatusStyle != null)
        {
            AdbVersionText.Text = _cachedAdbVersion;
            AdbStatusContainer.Style = _cachedAdbStatusStyle;
            return;
        }
        
        await CheckAdbVersionAsync();
    }
    
    private async Task CheckAdbVersionAsync()
    {
        try
        {
            string output = await AdbExecutor.ExecuteCommandAsync("version");
            
            var versionLine = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .FirstOrDefault(line => line.Contains("Version"));

            if (!string.IsNullOrEmpty(versionLine))
            {
                AdbVersionText.Text = versionLine.Split(' ').LastOrDefault() 
                                        ?? Strings.Settings_Adb_Status_Unknown;
                AdbStatusContainer.Style = (Style)FindResource("App.Style.StatusTag.Good");
            }
            else
            {
                AdbVersionText.Text = Strings.Settings_Adb_Status_InvalidOutput;
                AdbStatusContainer.Style = (Style)FindResource("App.Style.StatusTag.Bad");
            }
        }
        catch (Win32Exception)
        {
            AdbVersionText.Text = Strings.Settings_Adb_Status_NotFound;
            AdbStatusContainer.Style = (Style)FindResource("App.Style.StatusTag.Bad");
        }
        catch (Exception)
        {
            AdbVersionText.Text = Strings.Settings_Adb_Status_Error;
            AdbStatusContainer.Style = (Style)FindResource("App.Style.StatusTag.Bad");
        }
        finally
        {
            _cachedAdbVersion = AdbVersionText.Text;
            _cachedAdbStatusStyle = AdbStatusContainer.Style;
        }
    }

    private async void OnExportLogsClick(object sender, RoutedEventArgs e)
    {
        await ExportLogsAsync();
    }

    private async Task ExportLogsAsync()
    {
        if (!AdbLogger.Instance.HasLogs)
        {
            DialogService.Instance.ShowInfo(Strings.Settings_Export_NoLogs, Window.GetWindow(this));
            return;
        }

        var dialog = new SaveFileDialog 
        { 
            Title = Strings.Settings_MessageBox_ExportDialog_Title ?? "Export Diagnostic Log",
            FileName = $"zephyrs_elixir_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt", 
            DefaultExt = ".txt", 
            Filter = Strings.Settings_MessageBox_ExportDialog_Filter ?? "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) 
            return;
        
        string filePath = dialog.FileName;

        try
        {
            var logContent = AdbLogger.Instance.GetFormattedLog();
            var systemInfo = GetSystemInformation();
            
            var sb = new StringBuilder();
            sb.AppendLine(systemInfo);
            sb.Append(logContent);
            string fullLog = sb.ToString();
            
            await File.WriteAllTextAsync(filePath, fullLog, Encoding.UTF8);
            
            AdbLogger.Instance.LogSuccess("System", $"Diagnostic log exported to: {filePath}");
            
            DialogService.Instance.ShowInfoDirect(
                Strings.Settings_MessageBox_ExportComplete_Title,
                string.Format(Strings.Settings_MessageBox_ExportComplete_Message, filePath),
                Window.GetWindow(this));
        }
        catch (Exception ex)
        {
            AdbLogger.Instance.LogException("System", ex);
            
            string errorMessage = string.Format(
                Strings.Settings_MessageBox_ExportError_Message 
                    ?? "Failed to export log:\n{0}",
                ex.Message);

            DialogService.Instance.ShowInfoDirect(
                Strings.Settings_MessageBox_ExportError_Title,
                string.Format(Strings.Settings_Export_NoLogs, ex.Message),
                Window.GetWindow(this));
        }
    }

    private static class SystemInfoConstants
    {
        public const string HeaderLine = "═══════════════════════════════════════════════════════════════";
    }

    private static string GetSystemInformation()
    {
        var sb = new StringBuilder(1024);
        
        sb.AppendLine(SystemInfoConstants.HeaderLine);
        sb.AppendLine("  SYSTEM INFORMATION");
        sb.AppendLine(SystemInfoConstants.HeaderLine);
        sb.AppendLine();
        
        try
        {
            var assemblyName = Assembly.GetExecutingAssembly().GetName();
            sb.AppendLine($"Application: Zephyr's Elixir");
            sb.AppendLine($"Version: {assemblyName.Version}");
            sb.AppendLine($"Export Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"64-bit OS: {Environment.Is64BitOperatingSystem}");
            sb.AppendLine($".NET Version: {Environment.Version}");
            sb.AppendLine($"Working Directory: {Environment.CurrentDirectory}");
            
            try
            {
                var adbPath = AdbExecutor.GetAdbPath();
                sb.AppendLine($"ADB Path: {adbPath}");
                sb.AppendLine($"ADB Exists: {File.Exists(adbPath)}");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"ADB Path: Error - {ex.Message}");
            }
            
            sb.AppendLine($"Device Connected: {DeviceManager.Instance.IsConnected}");
            if (DeviceManager.Instance.IsConnected)
            {
                sb.AppendLine($"Device Name: {DeviceManager.Instance.DeviceName}");
                sb.AppendLine($"Battery Level: {DeviceManager.Instance.BatteryLevel}%");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"Error gathering system info: {ex.Message}");
        }
        
        sb.AppendLine();
        return sb.ToString();
    }
    
    private void OnResetToDefaultsClick(object sender, RoutedEventArgs e)
    {
        DialogService.Instance.ShowInfo("Settings_MessageBox_ResetInfo_Message", Window.GetWindow(this), "Settings_MessageBox_ResetInfo_Title");
    }

    private void OnLicenseClick(object sender, RoutedEventArgs e)
        => DialogService.Instance.ShowLicense(Window.GetWindow(this));

    private void OnPrivacyClick(object sender, RoutedEventArgs e)
        => DialogService.Instance.ShowPrivacy(Window.GetWindow(this));

    private void OnProLicenseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new LicenseDialog
        {
            Owner = Window.GetWindow(this)
        };
        dialog.ShowDialog();
    }
}